"""Independent saved-data QA for the connected parking courts in PR #3.

Compares with 2fb3b79. Reads actual scene meshes and MeshColliders; does not
import the generator or substitute for Unity shader/player/frame-time checks.
"""
import argparse
import json
import re
import subprocess
from functools import lru_cache
from pathlib import Path

import numpy as np
import shapely as sh
from shapely.geometry import Point
from shapely.ops import unary_union

from unity_yaml import ROOT, GUID, blocks, decode_name, field, matrix, mesh, scene, triangles, world
from validate import height_hits


BASE = '2fb3b792ddba4966e3aceb8c74866f81827a36bc'
REPO = ROOT.parent
ART = ROOT / 'Assets/Art/StreetRepair'
SITES = {'north-west': (22.1, 121., 0., -1.),
         'north-east': (30.3, 121., 0., -1.),
         'opposite-west': (-108., 67., 1., 0.),
         'opposite-east': (-108., 75.2, 1., 0.)}
TARGETS = {'main-road-north': (-74.629, 76.02),
           'main-road-south': (-2.515, -55.615),
           'main-road-middle': (-51.082, 32.361),
           'upper-5-7-ring': (25.409, 135.265),
           'east-court-ring': (103.897, 38.149),
           'west-court-loop': (-108.182, 32.962),
           'north-frontage-parking': (-12.969, 98.307)}


def git(*args):
    return subprocess.check_output(['git', *args], cwd=REPO)


@lru_cache(None)
def before(path):
    return git('show', BASE + ':' + str(path.relative_to(REPO)))


@lru_cache(None)
def current_mesh(path):
    return mesh(path)


def refs(block, key):
    found = re.search(r'^  ' + key + r':\s*\n((?:  - .*\n)+)', block, re.M)
    return re.findall(r'guid: (\w+)', found[1]) if found else []


def scene_items(bs):
    transforms = {int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1]): i
                  for i, (typ, b) in bs.items()
                  if typ == 4 and re.search(r'm_GameObject: \{fileID: (\d+)', b)}
    renderers = {int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1]): (i, b)
                 for i, (typ, b) in bs.items()
                 if typ == 23 and re.search(r'm_GameObject: \{fileID: (\d+)', b)}
    result = {}
    for i, (typ, b) in bs.items():
        if typ != 33:
            continue
        mo = re.search(r'm_Mesh: .*guid: (\w+)', b)
        go = re.search(r'm_GameObject: \{fileID: (\d+)', b)
        if not mo or not go or mo[1] not in GUID:
            continue
        go = int(go[1])
        if go not in renderers or go not in transforms:
            continue
        ri, rb = renderers[go]
        result[i] = dict(go=go, filter=i, render=ri, rb=rb, trans=transforms[go],
                         path=GUID[mo[1]], materials=[GUID.get(g, g) for g in refs(rb, 'm_Materials')])
    return result


def active(bs, item):
    if field(item['rb'], 'm_Enabled') != '1':
        return False
    tid = item['trans']
    while tid:
        block = bs[tid][1]
        go_ref = re.search(r'm_GameObject: \{fileID: (\d+)', block)
        go = int(go_ref[1]) if go_ref else None
        if go in bs and field(bs[go][1], 'm_IsActive', '1') != '1':
            return False
        parent = re.search(r'm_Father: \{fileID: (\d+)', block)
        # A stripped transform inherits its parent from an unchanged prefab.
        # Treat it as active for this conservative renderer-level upper bound.
        tid = int(parent[1]) if parent else 0
    return True


@lru_cache(None)
def material_name(path):
    return field(path.read_text(), 'm_Name', '')


def asphalt(path):
    return isinstance(path, Path) and 'asphalt' in material_name(path).lower()


def paving(path):
    return isinstance(path, Path) and (abs(float(field(path.read_text(), '- _SurfaceClass', '0')) - 6) < 1e-6
                                      or any(k in material_name(path) for k in ['Paving', 'Paver', 'RunningBond']))


def projected(v, tt):
    pts = v[tt]
    cross = np.cross(pts[:, 1] - pts[:, 0], pts[:, 2] - pts[:, 0])
    return sh.polygons(pts[cross[:, 1] > .000001][:, :, [0, 2]])


def run():
    s, bs, _ = scene()
    old = blocks(before(ROOT / 'Assets/Scenes/Street.unity').decode())
    items, original_items = scene_items(bs), scene_items(old)
    changed = set(git('diff', '--name-only', BASE, '--').decode().splitlines())
    out = {'baseCommit': BASE}
    assert all(i in bs for i in old), 'An existing scene block was removed'
    protected = [i for i, (typ, _) in old.items() if typ in (4, 1001, 104, 108, 20, 29, 157)]
    assert all(bs[i] == old[i] for i in protected), 'A pre-existing transform, prefab, light, camera or setting changed'
    out['existingSceneBlocksRetained'] = len(old)
    out['unchangedProtectedSceneBlocks'] = len(protected)
    forbidden = [p for p in changed if p.startswith('Unity_Walk/') and
                 any(k in p.lower() for k in ('/leaves/', 'leaf', 'leaves', 'lighting', 'streetlamps', 'road_profiles.json'))]
    assert not forbidden, ('Leaves, lighting or street profile changed', forbidden)
    concrete = ART / 'GarageWhiteConcrete.mat'
    assert concrete.read_bytes() == before(concrete), 'White concrete material changed'
    out['leavesLightingProfileAndWhiteConcreteUnchanged'] = True

    paver_slots = 0
    for fid, item in items.items():
        if fid not in original_items:
            continue
        slots = [i for i, p in enumerate(item['materials']) if paving(p)]
        if not slots:
            continue
        prev = original_items[fid]
        m, bm = current_mesh(item['path']), mesh(prev['path'], before(prev['path']).decode())
        assert item['trans'] == prev['trans']
        for slot in slots:
            p = item['materials'][slot]
            assert p == prev['materials'][slot] and p.read_bytes() == before(p), ('Paving material changed', p)
            now_t, old_t = triangles(m, slot), triangles(bm, slot)
            assert now_t.shape == old_t.shape, ('Paving topology changed', p, item['path'])
            for channel in bm['a']:
                assert channel in m['a'] and np.array_equal(m['a'][channel][now_t], bm['a'][channel][old_t]), \
                    ('Paving vertex attributes changed', item['path'], slot, channel)
            paver_slots += 1
    out['unchangedPavingSubmeshes'] = paver_slots
    road = next(x['path'] for x in items.values() if x['path'].name == 'ReferenceRoad.asset')
    assert road.read_bytes() == before(road), 'ReferenceRoad geometry/profile changed'
    out['carriagewayAndRoadProfileMeshByteIdentical'] = True

    # Metadata identifies all four small independent colliders, including the
    # collider-only opposite pair whose visuals are in the original renderers.
    component_blocks = [b for typ, b in bs.values() if typ == 114 and '::UndergroundParkingEntrance' in b]
    assert len(component_blocks) == 4, ('Expected four entrance components', len(component_blocks))
    transforms = {int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1]): i
                  for i, (typ, b) in bs.items() if typ == 4 and 'm_GameObject:' in b}
    sites = {}
    for b in component_blocks:
        go = int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1])
        name = decode_name(bs[go][1])
        key = next(k for k in SITES if k in name)
        assert key not in sites
        cid = int(re.search(r'floorAndWalls: \{fileID: (\d+)', b)[1])
        assert cid in bs and bs[cid][0] == 64
        cb = bs[cid][1]
        assert field(cb, 'm_Enabled') == '1' and f'm_GameObject: {{fileID: {go}}}' in cb
        cg = re.search(r'm_Mesh: .*guid: (\w+)', cb)[1]
        assert GUID[cg].name == 'GarageEntrance.asset', ('Collider should retain the small shared model', key)
        mat = matrix(bs, transforms[go])
        x, z, dx, dz = SITES[key]
        assert np.allclose(mat[[0, 2], 3], [x, z], atol=1e-5), (key, mat[:, 3])
        assert np.allclose(mat[[0, 2], 2], [dx, dz], atol=1e-5), ('Entrance orientation', key, mat[:, 2])
        for field_name, expected in [('length', 18), ('clearWidth', 4.4), ('depth', 2.64), ('maximumGrade', .16)]:
            assert abs(float(field(b, field_name)) - expected) < 1e-6, (key, field_name)
        sites[key] = {'matrix': mat, 'collider': cid, 'go': go, 'path': GUID[cg]}
    out['fourIndependentEntranceCollidersAndPoses'] = True

    # The continuous shell must cover the full roof footprint. Opaque body no
    # longer contains the former floating arch ribs/rails above the parapets.
    shell = current_mesh(ART / 'GarageBarrelCanopy.asset')
    sv, sn = shell['a'][0], shell['a'][1]
    assert np.all(sn[:, 1] >= -1e-5) and np.all(sv[:, 0] * sn[:, 0] >= -1e-5)
    shell_poly = unary_union(projected(sv, triangles(shell, 0)))
    assert 4.4 <= np.ptp(sv[:, 0]) <= 5.0 and abs(np.ptp(sv[:, 2]) - 12) < 1e-4
    shell_bounds = sh.box(sv[:, 0].min(), sv[:, 2].min(), sv[:, 0].max(), sv[:, 2].max())
    assert shell_bounds.symmetric_difference(shell_poly).area < .005, ('Canopy shell has holes', shell_poly.area)
    body = current_mesh(ART / 'GarageEntrance.asset')
    assert body['a'][0][:, 1].max() < 1.02, 'Opaque floating frames remain above garage parapets'
    basis_results = {}
    garage_paths = {x['path'] for x in items.values() if x['path'].name.startswith('GaragePaired')}
    garage_paths.update([ART / 'GarageEntrance.asset', ART / 'GarageBarrelCanopy.asset'])
    assert len(garage_paths) == 6
    for path in sorted(garage_paths):
        m = current_mesh(path)
        assert {0, 1, 2, 3, 4}.issubset(m['a'])
        v, n, tangent = (m['a'][i] for i in (0, 1, 2))
        assert all(len(a) == len(v) and np.isfinite(a).all() for a in m['a'].values())
        assert np.allclose(np.linalg.norm(n, axis=1), 1, atol=2e-4)
        assert np.allclose(np.linalg.norm(tangent[:, :3], axis=1), 1, atol=2e-4)
        assert np.max(abs(np.sum(n * tangent[:, :3], axis=1))) < 2e-4
        for slot in range(len(m['subs'])):
            tt = triangles(m, slot)
            assert not tt.size or (tt.min() >= 0 and tt.max() < len(v))
            tv = v[tt]
            cross = np.cross(tv[:, 1] - tv[:, 0], tv[:, 2] - tv[:, 0])
            assert np.all(np.linalg.norm(cross, axis=1) > 1e-9)
            assert np.all(np.sum(cross * n[tt].mean(1), axis=1) > 1e-9), ('Garage winding', path, slot)
        basis_results[path.name] = {'vertices': len(v), 'triangles': len(m['idx']) // 3}
    out['continuousCanopyFootprintSquareMeters'] = shell_poly.area
    out['garageMeshBasisAndWinding'] = basis_results

    local_asphalt = current_mesh(ART / 'LocalAsphalt.asset')
    assert 2 in local_asphalt['a'], 'Connected asphalt lacks normal-map tangent basis'
    ln = local_asphalt['a'][1]
    lt = local_asphalt['a'][2]
    assert np.isfinite(lt).all() and len(lt) == len(ln)
    assert np.allclose(np.linalg.norm(lt[:, :3], axis=1), 1, atol=2e-4)
    assert np.max(abs(np.sum(ln * lt[:, :3], axis=1))) < 2e-4
    out['connectedAsphaltTangentBasisValidated'] = True

    collision = []
    for cid, (typ, block) in bs.items():
        if typ != 64 or field(block, 'm_Enabled') != '1':
            continue
        mo = re.search(r'm_Mesh: .*guid: (\w+)', block)
        go = re.search(r'm_GameObject: \{fileID: (\d+)', block)
        path = GUID.get(mo[1]) if mo else None
        if not path or path.suffix != '.asset' or not go or int(go[1]) not in transforms:
            continue
        m = current_mesh(path)
        if not len(m['idx']):
            continue
        v = world(bs, {'trans': transforms[int(go[1])]}, m['a'][0])
        tt = v[np.concatenate([triangles(m, j) for j in range(len(m['subs']))])]
        collision.append((cid, path, tt, tt[:, :, [0, 2]].min((0, 1)), tt[:, :, [0, 2]].max((0, 1))))

    def hits(x, z):
        result = []
        for cid, path, tt, lo, hi in collision:
            if lo[0] - 1e-6 <= x <= hi[0] + 1e-6 and lo[1] - 1e-6 <= z <= hi[1] + 1e-6:
                result.extend((y, cid, path.name) for y in height_hits(tt, x, z))
        return result

    def original_floor(distance):
        if distance < 1.5:
            return -.16 * distance * distance / 3
        if distance > 16.5:
            return -2.64 + .16 * (18 - distance) ** 2 / 3
        return -.12 - .16 * (distance - 1.5)

    probes = 0
    for key, record in sites.items():
        mat, cid = record['matrix'], record['collider']
        for distance in np.arange(.375, 18, .375):
            for lateral in (-2., 0., 2.):
                p = mat @ np.array([lateral, original_floor(distance), distance, 1.])
                hh = hits(p[0], p[2])
                floor_hits = [y for y, c, _ in hh if c == cid and y <= mat[1, 3] + .025]
                assert floor_hits, ('Ramp floor collision hole', key, distance, lateral)
                y = max(floor_hits)
                assert abs(y - p[1]) < .012, ('Ramp driving profile', key, distance, lateral, y, p[1])
                blocking = [(a, c, name) for a, c, name in hh if c != cid and a > y + .025]
                assert not blocking, ('Foreign ground blocks ramp', key, distance, lateral, blocking)
                probes += 1
    out['clearRampCollisionProbes'] = probes

    # The middle asphalt passages rise to the common 0.85 m courtyard deck and
    # return to saved ground at the back, independently of the generation math.
    yard_probes = 0
    for label, center, forward, base_y in [('north', np.array([26.2, 121.]), np.array([0., -1.]), sites['north-west']['matrix'][1, 3]),
                                          ('west', np.array([-108., 71.1]), np.array([1., 0.]), sites['opposite-west']['matrix'][1, 3])]:
        samples = []
        garage_cids = {r['collider'] for r in sites.values()}
        for distance in np.arange(-8.8, 23.11, .3):
            p = center + forward * distance
            hh = [(y, c, n) for y, c, n in hits(*p) if c not in garage_cids]
            assert hh, ('Yard passage collision hole', label, distance, p.tolist())
            y = max(a for a, _, _ in hh)
            if 6.1 <= distance <= 18.1:
                assert abs(y - base_y - .85) < .025, ('Courtyard not flat', label, distance, y)
            assert base_y - .08 <= y <= base_y + .89, ('Yard height range', label, distance, y)
            samples.append((distance, y))
            yard_probes += 1
        for (d0, y0), (d1, y1) in zip(samples, samples[1:]):
            assert abs(y1 - y0) / (d1 - d0) < .22, ('Abrupt courtyard approach', label, d0, d1, y0, y1)
    out['courtyardMiddlePassageCollisionProbes'] = yard_probes

    local = next(item for item in items.values() if item['path'].name == 'LocalAsphalt.asset')
    lm = current_mesh(local['path'])
    lv = world(bs, local, lm['a'][0])
    west_bounds = sh.box(-117, 63.65, -84.8, 78.55)
    local_parts = projected(lv, triangles(lm, 0))
    local_parts = local_parts[sh.intersects(local_parts, west_bounds)]
    actual_west_yard = unary_union(local_parts).intersection(west_bounds)
    west_holes = unary_union([sh.box(-108, z - 2.47, -89.83, z + 2.47) for z in (67, 75.2)])
    expected_west_yard = west_bounds.difference(west_holes)
    yard_difference = expected_west_yard.symmetric_difference(actual_west_yard).area
    assert yard_difference < .002, ('West yard outline or ramp holes mismatch', yard_difference)
    out['westYardFootprintSymmetricDifferenceSquareMeters'] = yard_difference

    # Assemble the actual rendered asphalt tops, clipped to the two connected
    # courts. A 12 mm weld tolerates only float32 seams, never metre-wide gaps.
    region = sh.box(-130, -75, 120, 155)
    asphalt_parts = []
    for item in items.values():
        slots = [j for j, p in enumerate(item['materials']) if asphalt(p)]
        if not slots or item['path'].suffix != '.asset' or not active(bs, item):
            continue
        m = current_mesh(item['path'])
        v = world(bs, item, m['a'][0])
        for slot in slots:
            polys = projected(v, triangles(m, slot))
            if len(polys):
                selected = polys[sh.intersects(polys, region)]
                asphalt_parts.extend(selected)
    actual_asphalt = unary_union(asphalt_parts).intersection(region)
    welded = actual_asphalt.buffer(.012)
    components = [welded] if welded.geom_type == 'Polygon' else list(welded.geoms)
    main_point = Point(TARGETS['main-road-middle'])
    connected = [p for p in components if p.covers(main_point)]
    assert len(connected) == 1, 'Cannot identify the main-street asphalt component'
    connected = connected[0]
    targets = dict(TARGETS)
    targets.update({k: (p[0], p[1]) for k, p in SITES.items()})
    distances = {}
    for label, p in targets.items():
        point = Point(p)
        distances[label] = actual_asphalt.distance(point)
        assert connected.covers(point), ('Asphalt target disconnected from main street', label, p, distances[label])
        assert hits(*p), ('Connected rendered asphalt lacks saved ground collision', label, p)
    out['asphaltTargetsConnectedToMainStreet'] = distances
    out['asphaltSeamWeldToleranceMeters'] = .012

    # Connector centrelines must have ground under every sample; their ends lie
    # in the connected asphalt component, so no new strip ends in bare lawn.
    connections = [[(22.1, 121), (22.1, 135.14)], [(30.3, 121), (30.3, 135.46)],
                   [(-108, 57.7), (-113, 61), (-113, 71.1), (-108, 71.1)]]
    link_probes = 0
    for line in connections:
        shape = sh.LineString(line)
        for distance in np.linspace(0, shape.length, int(shape.length / .4) + 2):
            p = shape.interpolate(distance)
            assert connected.covers(p), ('Connector ends or crosses lawn', line, distance, list(p.coords))
            hh = hits(p.x, p.y)
            assert hh, ('Connector collision hole', p.x, p.y)
            link_probes += 1
    out['continuousConnectorCollisionProbes'] = link_probes

    totals = {'before': {'activeRenderers': 0, 'triangles': 0, 'vertices': 0, 'nonemptySubmeshes': 0},
              'after': {'activeRenderers': 0, 'triangles': 0, 'vertices': 0, 'nonemptySubmeshes': 0}}
    original_meshes = {}
    for label, mapping, blocks_for_run in [('before', original_items, old), ('after', items, bs)]:
        for item in mapping.values():
            if item['path'].suffix != '.asset' or not active(blocks_for_run, item):
                continue
            path = item['path']
            if label == 'before':
                if str(path.relative_to(REPO)) not in changed:
                    m = current_mesh(path)
                else:
                    if path not in original_meshes:
                        original_meshes[path] = mesh(path, before(path).decode())
                    m = original_meshes[path]
            else:
                m = current_mesh(path)
            if not len(m['idx']):
                continue
            totals[label]['activeRenderers'] += 1
            totals[label]['triangles'] += len(m['idx']) // 3
            totals[label]['vertices'] += len(m['a'][0])
            totals[label]['nonemptySubmeshes'] += sum(c > 0 for _, c, _, _ in m['subs'])
    for key in ('activeRenderers', 'triangles', 'nonemptySubmeshes'):
        assert totals['after'][key] <= totals['before'][key], ('Visual resource budget increased', key, totals)
    assert set(original_items) == set(items), 'Direct renderer identities changed'
    out['activeDirectSavedRenderersAllLodsUpperBound'] = totals
    out['asphaltTextureStatus'] = 'Check Asphalt020S source/license and importer report separately; geometry QA does not prove texture installation'
    out['unityImportPlayerAndFrameTimes'] = 'NOT RUN: saved-data checks do not measure Unity or FPS'
    return out


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=REPO / 'Reports/StreetRepair/parking-network-validation.json')
    args = parser.parse_args()
    result = run()
    content = json.dumps(result, indent=2, ensure_ascii=False) + '\n'
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(content)
    print(content, end='')
