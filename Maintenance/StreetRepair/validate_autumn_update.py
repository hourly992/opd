"""Independent saved-data audit of the autumn tone and two asphalt plazas.

Checks assets and saved collision/render geometry against c5d2798. These are
resource counts and CPU color calculations, never Unity import/FPS evidence.
"""
import argparse
import contextlib
import hashlib
import io
import json
import re
import subprocess
from functools import lru_cache
from pathlib import Path

import numpy as np
import shapely as sh
from shapely.ops import unary_union

from unity_yaml import ROOT, GUID, blocks, field, guid, matrix, mesh, scene, triangles, world
from validate import height_hits
from validate_parking_network import active, asphalt, paving, projected, scene_items, TARGETS


BASE = 'c5d2798'
REPO = ROOT.parent
ART = ROOT / 'Assets/Art/StreetRepair'
REGIONS = {'north': (16.1, 91.8, 36.3, 137.5), 'west': (-121., 60.5, -81., 81.7)}
SITES = {'north-west': (22.1, 121., 0., -1.), 'north-east': (30.3, 121., 0., -1.),
         'opposite-west': (-108., 67., 1., 0.), 'opposite-east': (-108., 75.2, 1., 0.)}
TEXTURES = {'.png', '.jpg', '.jpeg', '.tga', '.exr', '.hdr', '.tif', '.tiff', '.dds', '.webp'}


def git(*args):
    return subprocess.check_output(['git', *args], cwd=REPO)


@lru_cache(None)
def before(path):
    return git('show', BASE + ':' + str(path.relative_to(REPO)))


@lru_cache(maxsize=48)
def current_mesh(path):
    return mesh(path)


def tree():
    result = {}
    for line in git('ls-tree', '-r', BASE, '--', 'Unity_Walk').decode().splitlines():
        info, name = line.split('\t', 1)
        result[REPO / name] = info.split()[2]
    return result


def raw_blob_hash(path):
    data = path.read_bytes()
    return hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()


def mesh_counts(text):
    return {'vertices': int(field(text, 'm_VertexCount')),
            'triangles': sum(int(n) for n in re.findall(r'    indexCount: (\d+)', text)) // 3,
            'nonemptySubmeshes': sum(int(n) > 0 for n in re.findall(r'    indexCount: (\d+)', text))}


def remove_mesh_ref(text):
    return re.sub(r'^  m_Mesh: .*\n?', '', text, flags=re.M)


def run():
    baseline = git('rev-parse', BASE).decode().strip()
    saved, bs, _ = scene()
    scene_path = ROOT / 'Assets/Scenes/Street.unity'
    old = blocks(before(scene_path).decode())
    assert bs.keys() == old.keys(), 'Existing scene identities/components were added or removed'
    items, olditems = scene_items(bs), scene_items(old)
    assert items.keys() == olditems.keys(), 'Direct renderer identities changed'
    out = {'baselineCommit': baseline, 'existingSceneBlocksRetained': len(old)}

    # Calculate the exact expected lighting changes independently of the scene
    # writer; all other permitted scene edits replace one ground mesh reference.
    from warm_autumn import apply
    desired = dict(old)
    warm_changes = apply(desired)
    assert set(warm_changes['changedSceneBlocks']) == {2, 1321379542, 1319908495}
    retired = (4000000109, 4000000110)
    for component in retired:
        typ, block = desired[component]
        desired[component] = (typ, re.sub(r'^  m_Enabled: 1$', '  m_Enabled: 0', block, flags=re.M))
        assert bs[component] == desired[component], 'Empty courtyard component must be disabled'
    empty = ROOT / 'Assets/Art/StreetRepair/GarageMouthAsphalt.asset'
    assert not len(current_mesh(empty)['idx']), 'Retired courtyard unexpectedly contains geometry'
    out['retiredEmptyGroundComponents'] = list(retired)
    mesh_ref_changes = []
    for block_id, (typ, block) in bs.items():
        if (typ, block) == desired[block_id]:
            continue
        assert typ in (33, 64), ('Unexpected saved scene change', block_id, typ)
        assert typ == desired[block_id][0] and remove_mesh_ref(block) == remove_mesh_ref(desired[block_id][1]), \
            ('Ground mesh replacement changed another property', block_id)
        target_guid = re.search(r'm_Mesh: .*guid: (\w+)', block)[1]
        path = GUID.get(target_guid)
        assert path and path.name.startswith('AutumnPlaza_') and path.is_relative_to(ART / 'Surfaces'), \
            ('Unexpected replacement mesh', block_id, path)
        mesh_ref_changes.append({'block': block_id, 'type': typ, 'mesh': str(path.relative_to(ROOT))})
    transform_ids = [i for i, (typ, _) in old.items() if typ in (4, 1001)]
    assert all(bs[i] == old[i] for i in transform_ids), 'A house/prop/prefab transform changed'
    out['unchangedTransformAndPrefabBlocks'] = len(transform_ids)
    out['sceneChangesLimitedToLightingGradeGroundMeshRefsAndRetiredEmptyCourt'] = True
    out['groundMeshReferenceChanges'] = mesh_ref_changes

    # Raw Git blob hashes check actual bytes, including image importer metadata.
    from install_asphalt020s import active_asphalt_materials, verify
    asphalt_materials = set(active_asphalt_materials())
    assert len(asphalt_materials) == 7
    baseline_files = tree()
    changed_paths = {REPO / p for p in git('diff', '--name-only', BASE, '--', 'Unity_Walk').decode().splitlines()}
    protected = {'nonAsphaltMaterials': [], 'texturesAndImporters': [],
                 'leaves': [], 'houseModelsAndImporters': [], 'garageMeshesAndImporters': [], 'roadProfile': []}
    for path in baseline_files:
        name = path.name[:-5] if path.name.endswith('.meta') else path.name
        suffix = Path(name).suffix.lower()
        lower = str(path.relative_to(ROOT)).lower() if path.is_relative_to(ROOT) else ''
        if suffix == '.mat' and Path(str(path)[:-5] if path.name.endswith('.meta') else str(path)) not in asphalt_materials:
            protected['nonAsphaltMaterials'].append(path)
        if suffix in TEXTURES:
            protected['texturesAndImporters'].append(path)
        if '/leaves/' in lower or 'leaf' in name.lower() or 'foliage' in name.lower():
            protected['leaves'].append(path)
        if suffix in ('.fbx', '.obj', '.blend'):
            protected['houseModelsAndImporters'].append(path)
        if name.endswith('.asset') and (name in ('GarageEntrance.asset', 'GarageBarrelCanopy.asset')
                                        or name.startswith(('GaragePairedBody_', 'GaragePairedCanopy_'))):
            protected['garageMeshesAndImporters'].append(path)
        if name in ('road_profiles.json', 'ReferenceRoad.asset', 'street.json'):
            protected['roadProfile'].append(path)
    hashes = {path: raw_blob_hash(path) for path in {p for paths in protected.values() for p in paths}}
    for category, paths in protected.items():
        assert paths, ('Protected asset class is empty', category)
        assert all(hashes[p] == baseline_files[p] for p in paths), \
            ('Protected assets differ from baseline bytes', category, [str(p) for p in paths if hashes[p] != baseline_files[p]])
    out['byteIdenticalProtectedAssets'] = {key: len(paths) for key, paths in protected.items()}
    out['byteIdentityMethod'] = 'Raw file bytes hashed as Git blobs against the baseline tree'

    # A shared mesh may contain both asphalt and paver slots. Verify the paver
    # vertex attributes/topology instead of assuming a whole-file hash can stay.
    paver_slots = 0
    for fid, item in items.items():
        slots = [j for j, p in enumerate(item['materials']) if paving(p)]
        if not slots:
            continue
        prev = olditems[fid]
        now = current_mesh(item['path'])
        oldmesh = mesh(prev['path'], before(prev['path']).decode())
        for j in slots:
            assert item['materials'][j] == prev['materials'][j]
            a, b = triangles(now, j), triangles(oldmesh, j)
            assert a.shape == b.shape, ('Paver triangle count changed', fid, j)
            assert all(k in now['a'] and np.array_equal(now['a'][k][a], oldmesh['a'][k][b]) for k in oldmesh['a']), \
                ('Paver geometry or shading attributes changed', fid, j)
            paver_slots += 1
    out['unchangedPaverSubmeshes'] = paver_slots

    vehicle_guid = guid(ROOT / 'Assets/Scripts/Environment/StreetVehicle.cs')
    assert not [i for i, (typ, b) in bs.items() if typ == 114 and vehicle_guid in b], 'A saved car component remains'
    parking_guid = guid(ROOT / 'Assets/Scripts/Environment/ParkingSite.cs')
    parkings = [b for typ, b in bs.values() if typ == 114 and parking_guid in b]
    assert parkings and all(field(b, 'vehicles') == '[]' for b in parkings), 'A parking site refers to a car'
    runtime = ROOT / 'Assets/Scripts'
    presentation = runtime / 'Environment/StreetPresentation.cs'
    runtime_paths = sorted(runtime.rglob('*.cs'))
    for path in runtime_paths:
        if path != presentation:
            assert path in baseline_files and raw_blob_hash(path) == baseline_files[path], ('Unexpected runtime code change', path)
        code = path.read_text()
        assert not re.search(r'(?:Instantiate|AddComponent)\s*(?:<\s*StreetVehicle\s*>|\([^;\n]*(?:[Cc]ar|[Vv]ehicle))', code), \
            ('Runtime vehicle construction found', path)
    out['cars'] = {'savedVehicleComponents': 0, 'emptyParkingVehicleArrays': len(parkings),
                   'runtimeVehicleConstructionFound': False, 'otherRuntimeSourceFilesByteIdentical': len(runtime_paths) - 1}

    with contextlib.redirect_stdout(io.StringIO()):
        out['asphaltInstallation'] = verify()
    from validate_warm_autumn import run as check_warm
    out['warmAutumn'] = check_warm(check_saved=True)

    # Actual saved mesh resource counts, counting all explicit LOD renderers.
    # Imported prefab renderers are identical and excluded from this upper bound.
    totals = {label: {'activeRenderers': 0, 'triangles': 0, 'vertices': 0, 'nonemptySubmeshes': 0}
              for label in ('before', 'after')}
    counts_cache = {}
    for label, records, state in [('before', olditems, old), ('after', items, bs)]:
        for item in records.values():
            if item['path'].suffix != '.asset' or not active(state, item):
                continue
            read_original = label == 'before' and item['path'] in changed_paths
            key = ('original' if read_original else 'current', item['path'])
            if key not in counts_cache:
                content = before(item['path']).decode() if read_original else item['path'].read_text()
                counts_cache[key] = mesh_counts(content)
            counts = counts_cache[key]
            if not counts['triangles']:
                continue
            totals[label]['activeRenderers'] += 1
            for k, n in counts.items():
                totals[label][k] += n
    assert totals['after']['activeRenderers'] <= totals['before']['activeRenderers'], ('Renderer count increased', totals)
    assert totals['after']['triangles'] <= totals['before']['triangles'], ('Triangle count increased', totals)
    assert totals['after']['nonemptySubmeshes'] <= totals['before']['nonemptySubmeshes'], ('Submesh count increased', totals)
    out['activeDirectSavedRenderersAllLodsUpperBound'] = totals

    # Exact intended footprint from independent raw regions, existing pavers
    # and the original source building outlines, not the mesh writer's mask.
    paver_parts = []
    for item in items.values():
        if item['path'].suffix != '.asset' or not active(bs, item):
            continue
        slots = [j for j, p in enumerate(item['materials']) if paving(p)]
        if slots:
            m = current_mesh(item['path'])
            vertices = world(bs, item, m['a'][0])
            for j in slots:
                paver_parts.extend(projected(vertices, triangles(m, j)))
    pavers = unary_union(paver_parts)
    source = json.loads((ROOT / 'Assets/StreetSource/street.json').read_text())
    houses = unary_union([sh.Polygon([(p['x'], p['z']) for p in b['points']]) for b in source['buildings']
                          if len(b['points']) >= 3])
    reserved = pavers.union(houses)
    holes = unary_union([sh.box(x - 2.47, z - 18.17, x + 2.47, z) if dz == -1
                         else sh.box(x, z - 2.47, x + 18.17, z + 2.47)
                         for x, z, dx, dz in SITES.values()])
    local = next(item for item in items.values() if item['path'].name == 'LocalAsphalt.asset')
    lm = current_mesh(local['path'])
    local_top = unary_union(projected(world(bs, local, lm['a'][0]), triangles(lm, 0)))
    oldlocal = olditems[local['filter']]
    om = mesh(oldlocal['path'], before(oldlocal['path']).decode())
    old_top = unary_union(projected(world(old, oldlocal, om['a'][0]), triangles(om, 0)))
    plaza_checks = {}
    pad_masks = []
    for label, bounds in REGIONS.items():
        raw = sh.box(*bounds)
        pad_masks.append(raw.difference(reserved))
        expected = raw.difference(reserved).difference(holes)
        actual = local_top.intersection(raw).difference(reserved)
        diff = expected.symmetric_difference(actual).area
        assert diff < .01, ('Plaza coverage/ramp-hole mismatch', label, diff)
        prior_overlap = old_top.intersection(raw).intersection(reserved).area
        final_overlap = local_top.intersection(raw).intersection(reserved).area
        assert final_overlap <= prior_overlap + .005, ('New asphalt overlaps houses or paving', label, prior_overlap, final_overlap)
        plaza_checks[label] = {'rawBounds': list(bounds), 'expectedAreaSquareMeters': expected.area,
                               'coverageSymmetricDifferenceSquareMeters': diff,
                               'priorLegacyReservedAreaOverlapSquareMeters': prior_overlap,
                               'finalReservedAreaOverlapSquareMeters': final_overlap}
    out['plazaCoverageAndExistingPavingHouseExclusion'] = plaza_checks

    # Check every active lawn/grass/clover triangle against the hard footprint.
    # Include zero-area XZ cards as line segments so upright blades cannot evade
    # an area-only test. A 5 mm inset ignores only float32 boundary seam noise.
    hard_mask = unary_union(pad_masks)
    inner_mask = hard_mask.buffer(-.005)
    vegetation_meshes = vegetation_triangles = 0
    for item in items.values():
        path = item['path']
        if path.suffix != '.asset' or not active(bs, item) or any(k in str(path).lower() for k in ('/leaves/', 'leaf')):
            continue
        whole_grass = any(k in path.stem.lower() for k in ('grass', 'clover'))
        slots = [j for j, p in enumerate(item['materials']) if whole_grass or isinstance(p, Path) and
                 (any(k in field(p.read_text(), 'm_Name', '').lower() for k in ('grass', 'clover', 'lawn', 'meadow'))
                  or field(p.read_text(), '- _SurfaceClass') == '1')]
        if not slots:
            continue
        m = current_mesh(path)
        if not len(m['idx']):
            continue
        v = world(bs, item, m['a'][0])
        if not hard_mask.intersects(sh.box(v[:, 0].min(), v[:, 2].min(), v[:, 0].max(), v[:, 2].max())):
            continue
        vegetation_meshes += 1
        for j in slots:
            tv = v[triangles(m, j)][:, :, [0, 2]]
            if not len(tv):
                continue
            poly = sh.polygons(tv)
            areas = sh.area(poly)
            use = (areas > 1e-9) & sh.intersects(poly, inner_mask)
            overlap = float(sh.area(sh.intersection(poly[use], inner_mask)).sum())
            assert overlap < 1e-7, ('Grass/lawn remains inside asphalt plaza', path, j, overlap)
            flat = tv[areas <= 1e-9]
            if len(flat):
                lines = sh.linestrings(np.concatenate([flat, flat[:, :1]], axis=1))
                assert not sh.intersects(lines, inner_mask).any(), ('Upright grass cards remain inside asphalt plaza', path, j)
            vegetation_triangles += len(tv)
    out['vegetationExclusion'] = {'activeGroundMeshesInspected': vegetation_meshes,
                                 'candidateTrianglesInspected': vegetation_triangles,
                                 'survivingGrassLawnCloverInsidePlazas': 0, 'boundaryInsetToleranceMeters': .005}

    # Construct actual saved collision triangles in both sectors. Every ramp
    # floor must remain unblocked, and the centre/outer passages need support.
    transforms = {int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1]): i
                  for i, (typ, b) in bs.items() if typ == 4 and re.search(r'm_GameObject: \{fileID: (\d+)', b)}
    collisions = []
    components = [b for typ, b in bs.values() if typ == 114 and '::UndergroundParkingEntrance' in b]
    assert len(components) == 4
    sites = {}
    for b in components:
        go = int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1])
        mat = matrix(bs, transforms[go])
        key = next(k for k, (x, z, dx, dz) in SITES.items() if np.allclose(mat[[0, 2], 3], [x, z], atol=1e-5))
        cid = int(re.search(r'floorAndWalls: \{fileID: (\d+)', b)[1])
        assert bs[cid] == old[cid], ('Ramp collider component changed', key)
        sites[key] = (cid, mat)
    region = unary_union([sh.box(*bounds) for bounds in REGIONS.values()])
    for cid, (typ, b) in bs.items():
        if typ != 64 or field(b, 'm_Enabled') != '1':
            continue
        mo = re.search(r'm_Mesh: .*guid: (\w+)', b)
        go = re.search(r'm_GameObject: \{fileID: (\d+)', b)
        path = GUID.get(mo[1]) if mo else None
        if not path or path.suffix != '.asset' or not go or int(go[1]) not in transforms:
            continue
        m = current_mesh(path)
        if not len(m['idx']):
            continue
        v = world(bs, {'trans': transforms[int(go[1])]}, m['a'][0])
        if not region.intersects(sh.box(v[:, 0].min(), v[:, 2].min(), v[:, 0].max(), v[:, 2].max())):
            continue
        tt = v[np.concatenate([triangles(m, j) for j in range(len(m['subs']))])]
        collisions.append((cid, path.name, tt, tt[:, :, [0, 2]].min((0, 1)), tt[:, :, [0, 2]].max((0, 1))))

    def hits(x, z):
        result = []
        for cid, name, tt, lo, hi in collisions:
            if lo[0] - 1e-6 <= x <= hi[0] + 1e-6 and lo[1] - 1e-6 <= z <= hi[1] + 1e-6:
                result.extend((y, cid, name) for y in height_hits(tt, x, z))
        return result

    def floor_at(distance):
        if distance < 1.5:
            return -.16 * distance * distance / 3
        if distance > 16.5:
            return -2.64 + .16 * (18 - distance) ** 2 / 3
        return -.12 - .16 * (distance - 1.5)

    ramp_probes = 0
    for key, (cid, mat) in sites.items():
        for distance in np.arange(.375, 18, .375):
            for lateral in (-2., 0., 2.):
                p = mat @ np.array([lateral, floor_at(distance), distance, 1.])
                hh = hits(p[0], p[2])
                own_floor = [y for y, c, _ in hh if c == cid and y <= mat[1, 3] + .025]
                assert own_floor and abs(max(own_floor) - p[1]) < .012, ('Ramp floor collision/profile', key, distance, lateral, hh)
                assert not [(y, c, n) for y, c, n in hh if c != cid and y > max(own_floor) + .025], \
                    ('New ground blocks descending ramp', key, distance, lateral, hh)
                ramp_probes += 1
    yard_probes = 0
    garage_cids = {cid for cid, mat in sites.values()}
    asphalt_triangles = world(bs, local, lm['a'][0])[triangles(lm, 0)]
    for label, center, forward, side, base_y in [
            ('north', np.array([26.2, 121.]), np.array([0., -1.]), np.array([1., 0.]), -.01),
            ('west', np.array([-108., 71.1]), np.array([1., 0.]), np.array([0., 1.]), .065)]:
        for lateral in (-6.9, 0., 6.9):
            line_samples = []
            for distance in np.arange(-12.8, 26.81, .4):
                p = center + forward * distance + side * lateral
                if not inner_mask.covers(sh.Point(*p)) or holes.covers(sh.Point(*p)):
                    continue
                hh = [(y, c, n) for y, c, n in hits(*p) if c not in garage_cids]
                assert hh, ('Plaza passage or side strip collision hole', label, lateral, distance)
                y = max(t[0] for t in hh)
                asphalt_y = height_hits(asphalt_triangles, *p)
                assert asphalt_y and abs(y - max(asphalt_y)) < .025, ('Collision differs from asphalt top', label, lateral, distance, hh)
                if lateral == 0 and 6.1 <= distance <= 18.1:
                    assert abs(y - base_y - .85) < .025, ('Raised courtyard is not flat', label, distance, y)
                assert base_y - .09 <= y <= base_y + .89, ('Plaza height range', label, lateral, distance, y)
                line_samples.append((distance, y))
                yard_probes += 1
            if lateral == 0:
                for (d0, y0), (d1, y1) in zip(line_samples, line_samples[1:]):
                    if d1 - d0 < .41:
                        assert abs(y1 - y0) / (d1 - d0) < .22, ('Abrupt central courtyard approach', label, d0, d1, y0, y1)
    out['collision'] = {'clearDescendingRampProbes': ramp_probes,
                        'courtyardCentreSideStripsAndRearApproachProbes': yard_probes,
                        'independentRampColliderComponentsUnchanged': 4}

    region = sh.box(-130, -75, 120, 155)
    asphalt_parts = []
    for item in items.values():
        slots = [j for j, p in enumerate(item['materials']) if asphalt(p)]
        if not slots or item['path'].suffix != '.asset' or not active(bs, item):
            continue
        m = current_mesh(item['path'])
        v = world(bs, item, m['a'][0])
        for slot in slots:
            parts = projected(v, triangles(m, slot))
            if len(parts):
                asphalt_parts.extend(parts[sh.intersects(parts, region)])
    actual_asphalt = unary_union(asphalt_parts).intersection(region)
    welded = actual_asphalt.buffer(.012)
    components = [welded] if welded.geom_type == 'Polygon' else list(welded.geoms)
    connected = [p for p in components if p.covers(sh.Point(TARGETS['main-road-middle']))]
    assert len(connected) == 1, 'Missing main-road asphalt component'
    connected = connected[0]
    targets = dict(TARGETS)
    targets.update({key: value[:2] for key, value in SITES.items()})
    assert all(connected.covers(sh.Point(*p)) for p in targets.values()), 'A court or ramp is disconnected'
    connections = [[(22.1, 121), (22.1, 135.14)], [(30.3, 121), (30.3, 135.46)],
                   [(-108, 57.7), (-113, 61), (-113, 71.1), (-108, 71.1)]]
    link_probes = 0
    for points in connections:
        line = sh.LineString(points)
        for distance in np.linspace(0, line.length, int(line.length / .4) + 2):
            p = line.interpolate(distance)
            assert connected.covers(p) and hits(p.x, p.y), 'Disconnected or uncollidable driveway'
            link_probes += 1
    out['connectivity'] = {'asphaltTargetsConnectedToMainStreet': list(targets),
                           'asphaltSeamWeldToleranceMeters': .012,
                           'continuousConnectorCollisionProbes': link_probes}

    out['limitations'] = [
        'Unity shader/asset import, C# compilation and a game build/run were not executed.',
        'Resource counts include all direct saved LODs; they do not measure draw calls, GPU memory, frame time or FPS.',
        'The asphalt RGB result is intrinsic albedo calculated from source pixels, not the measured color of a game screenshot.',
        'Existing hidden asphalt beneath reserved house/paver areas is preserved; the overlap check prohibits adding more there.',
    ]
    return out


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=REPO / 'Reports/StreetRepair/autumn-update-validation.json')
    args = parser.parse_args()
    result = run()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    content = json.dumps(result, indent=2, ensure_ascii=False) + '\n'
    args.output.write_text(content)
    print(content, end='')
