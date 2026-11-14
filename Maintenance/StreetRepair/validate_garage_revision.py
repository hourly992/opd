"""Independent checks for the saved garage revision against PR #3's first head.

This script reads assets; it does not repair them or run Unity. Pass --output to
save its JSON result. A Unity import/player run is still required for GPU/FPS QA.
"""
import argparse
import json
import re
import subprocess
from functools import lru_cache
from pathlib import Path

import numpy as np
import shapely as sh
from shapely.ops import unary_union

from unity_yaml import ROOT, GUID, blocks, decode_name, field, matrix, mesh, scene, triangles, vec, world
from validate import height_hits


BASE = '00f63cb0102b1dd800d7a11a0a519ba42034c58f'
REPO = ROOT.parent
ART = ROOT / 'Assets/Art/StreetRepair'
EXPECTED_SITES = {'north-west': (22.1, 121.0), 'north-east': (30.3, 121.0)}
GARAGE_MESHES = ('GarageEntrance.asset', 'GarageBarrelCanopy.asset', 'GarageMouthAsphalt.asset')


def git(*args):
    return subprocess.check_output(['git', *args], cwd=REPO)


def original(path):
    return git('show', BASE + ':' + str(path.relative_to(REPO))).decode()


@lru_cache(None)
def current_mesh(path):
    return mesh(path)


def item_map(bs):
    """Only direct saved mesh renderers; imported prefabs are unchanged below."""
    render_gos = {int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1])
                  for t, b in bs.values() if t == 23 and re.search(r'm_GameObject: \{fileID: (\d+)', b)}
    out = {}
    for i, (typ, b) in bs.items():
        if typ != 33:
            continue
        go = re.search(r'm_GameObject: \{fileID: (\d+)', b)
        mo = re.search(r'm_Mesh: .*guid: (\w+)', b)
        p = GUID.get(mo[1]) if mo else None
        if p and p.suffix == '.asset' and go and int(go[1]) in render_gos:
            out[i] = p
    return out


def material_color(path, key='_Color'):
    value = field(path.read_text(), '- ' + key)
    assert value, ('Missing material color', str(path), key)
    return np.array([float(x) for x in re.findall(r'[rgba]: ([^,}]+)', value)])


def run():
    s, bs, items = scene()
    base = blocks(original(ROOT / 'Assets/Scenes/Street.unity'))
    changed = set(git('diff', '--name-only', BASE, '--').decode().splitlines())
    out = {'baseCommit': BASE}

    protected_files = sorted(p for p in changed if any(q in p.lower() for q in
                              ('/leaves/', 'leaf', 'leaves', 'lighting', 'streetlamps', 'road_profiles.json')))
    assert not protected_files, ('Leaves, lighting, or road-profile files changed', protected_files)
    out['leafLightingAndRoadProfileFilesUnchanged'] = True
    assert all(i in bs for i in base), 'Existing scene blocks were removed'
    entrances = [x for x in items if x['path'].name == 'GarageEntrance.asset']
    roofs = [x for x in items if x['path'].name == 'GarageBarrelCanopy.asset']
    decks = [x for x in items if x['path'].name == 'GarageMouthAsphalt.asset']
    components = [b for t, b in bs.values() if t == 114 and '::UndergroundParkingEntrance' in b]
    assert len(entrances) == len(roofs) == len(components) == 2, 'Expected exactly two shared-mesh entrances'
    assert len(decks) == 1, 'The shared courtyard/mouth renderer must be reused'
    west = next(x for x in entrances if 'north-west' in x['name'])
    moved_transform = west['trans']
    protected = [i for i, (typ, b) in base.items()
                 if typ in (4, 1001, 104, 108, 20, 29, 157) and i != moved_transform]
    assert all(bs[i] == base[i] for i in protected), 'A house/prop/prefab/light/camera/settings block changed'
    oldwest, newwest = base[moved_transform][1], bs[moved_transform][1]
    assert re.sub(r'm_LocalPosition: .*', '', oldwest) == re.sub(r'm_LocalPosition: .*', '', newwest), \
        'West entrance rotation/scale/hierarchy changed'
    assert np.allclose(vec(oldwest, 'm_LocalPosition'), [23.8, -.01, 121], atol=1e-5)
    assert np.allclose(vec(newwest, 'm_LocalPosition'), [22.1, -.01, 121], atol=1e-5)
    out['unchangedProtectedSceneBlocks'] = len(protected)
    out['allExistingSceneBlocksRetained'] = len(base)
    out['movedEntrance'] = {'from': [23.8, 121], 'to': [22.1, 121]}

    for x in entrances:
        key = next(k for k in EXPECTED_SITES if k in x['name'])
        p = matrix(bs, x['trans'])[:3, 3]
        assert np.allclose(p[[0, 2]], EXPECTED_SITES[key], atol=1e-5), (key, p)
    assert len({x['path'] for x in entrances}) == len({x['path'] for x in roofs}) == 1
    for b in components:
        for key, value in [('length', 18), ('clearWidth', 4.4), ('depth', 2.64), ('maximumGrade', .16)]:
            assert abs(float(field(b, key)) - value) < 1e-6, (key, field(b, key))
        assert field(b, 'placementProvisional') == '1'
    out['entranceCount'] = 2

    basis_checks = {}
    for name in GARAGE_MESHES:
        m = current_mesh(ART / name)
        assert {0, 1, 2, 3, 4}.issubset(m['a']), ('Missing normal/tangent/UV/color', name)
        n = len(m['a'][0])
        assert all(len(a) == n and np.isfinite(a).all() for a in m['a'].values()), name
        norm, tangent = m['a'][1], m['a'][2]
        assert np.allclose(np.linalg.norm(norm, axis=1), 1, atol=2e-4), ('Non-unit normals', name)
        assert np.allclose(np.linalg.norm(tangent[:, :3], axis=1), 1, atol=2e-4), ('Non-unit tangents', name)
        assert np.max(abs(np.sum(norm * tangent[:, :3], axis=1))) < 2e-4, ('Non-orthogonal tangent basis', name)
        assert np.all(abs(abs(tangent[:, 3]) - 1) < 1e-6), ('Invalid tangent handedness', name)
        tri_count = 0
        for j in range(len(m['subs'])):
            tt = triangles(m, j)
            assert not len(tt) or (tt.min() >= 0 and tt.max() < n), ('Index bounds', name, j)
            v = m['a'][0][tt]
            cross = np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0])
            assert np.all(np.linalg.norm(cross, axis=1) > 1e-9), ('Degenerate triangle', name, j)
            normal = m['a'][1][tt].mean(1)
            assert np.all(np.sum(cross * normal, axis=1) > 1e-9), ('Reversed winding', name, j)
            tri_count += len(tt)
        basis_checks[name] = {'vertices': n, 'triangles': tri_count}
    roof = current_mesh(ART / 'GarageBarrelCanopy.asset')
    assert roof['a'][1][:, 1].min() >= -1e-5, 'Canopy faces downwards'
    roofv = roof['a'][0]
    # Both sides of the arch face away from the longitudinal centre plane.
    assert np.all(roofv[:, 0] * roof['a'][1][:, 0] >= -1e-5), 'Canopy side normals face inward'
    out['garageMeshBasisAndWinding'] = basis_checks
    deck = current_mesh(ART / 'GarageMouthAsphalt.asset')
    dt = deck['a'][0][triangles(deck, 0)]
    actual_deck = unary_union(sh.polygons(dt[:, :, [0, 2]]))
    expected_holes = unary_union([sh.box(cx - 2.47, 102.83, cx + 2.47, 121)
                                  for cx, cz in EXPECTED_SITES.values()])
    expected_deck = sh.box(18.75, 97.8, 33.65, 130).difference(expected_holes)
    footprint_difference = expected_deck.symmetric_difference(actual_deck).area
    assert footprint_difference < .001, ('Courtyard outline/hole mismatch', footprint_difference)
    out['courtyardFootprintSymmetricDifferenceSquareMeters'] = footprint_difference

    concrete = material_color(ART / 'GarageWhiteConcrete.mat')
    canopy = material_color(ART / 'GarageCanopy.mat')
    assert concrete[:3].min() >= .70 and concrete[3] == 1, ('Concrete must be light and opaque', concrete)
    assert canopy[:3].min() > .35 and 0 < canopy[3] < .8, ('Canopy must be light and transparent', canopy)
    assert current_mesh(ART / 'GarageEntrance.asset')['a'][3][:, :3].min() >= 100, 'Black vertex tint in garage'
    out['lightConcreteRGBA'] = concrete.tolist()
    out['transparentCanopyRGBA'] = canopy.tolist()

    paver_checks = []
    for path in sorted({p for x in items for p in x['materials'] if isinstance(p, Path)}):
        mat = path.read_text()
        if abs(float(field(mat, '- _SurfaceClass', '0')) - 6) > 1e-6:
            continue
        name = field(mat, 'm_Name')
        fraction = float(field(mat, '- _PaverGreyFraction', '.23'))
        pattern = float(field(mat, '- _PaverPattern', '0'))
        if 'CourtGrey' in name:
            assert fraction == 1, ('Intentional grey courtyard finish changed', str(path))
        else:
            assert fraction == 0 and pattern == 1, ('Red sidewalk has grey/basket pavers', str(path), fraction, pattern)
        if name == 'RedGreyRunningBond':
            assert np.allclose(material_color(path, '_PaverRedTone'), material_color(path, '_PaverFarTone')), \
                'The main red sidewalk changes tone at distance'
        paver_checks.append({'material': str(path.relative_to(ROOT)), 'name': name,
                             'greyFraction': fraction, 'pattern': pattern})
    assert paver_checks and any(x['name'] == 'RedGreyRunningBond' for x in paver_checks)
    out['activePaverMaterials'] = paver_checks

    # Build independent world-space collision triangles, including all ground
    # meshes rather than trusting the generated garage's manifest.
    trans = {int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1]): i
             for i, (typ, b) in bs.items() if typ == 4 and re.search(r'm_GameObject: \{fileID: (\d+)', b)}
    collisions = []
    for typ, b in bs.values():
        if typ != 64 or field(b, 'm_Enabled') != '1':
            continue
        mo = re.search(r'm_Mesh: .*guid: (\w+)', b)
        go = re.search(r'm_GameObject: \{fileID: (\d+)', b)
        p = GUID.get(mo[1]) if mo else None
        if not p or p.suffix != '.asset' or not go or int(go[1]) not in trans:
            continue
        m = current_mesh(p)
        if not len(m['idx']):
            continue
        v = world(bs, {'trans': trans[int(go[1])]}, m['a'][0])
        tt = v[np.concatenate([triangles(m, j) for j in range(len(m['subs']))])]
        collisions.append((p, tt, tt[:, :, [0, 2]].min((0, 1)), tt[:, :, [0, 2]].max((0, 1))))

    def hits(x, z):
        result = []
        for p, tt, lo, hi in collisions:
            if lo[0] - 1e-6 <= x <= hi[0] + 1e-6 and lo[1] - 1e-6 <= z <= hi[1] + 1e-6:
                result.extend((y, p.name) for y in height_hits(tt, x, z))
        return result

    def floor_at(distance):
        # Original 16% ramp with 1.5 m vertical easements at either end.
        if distance < 1.5:
            return -.16 * distance * distance / 3
        if distance > 16.5:
            return -2.64 + .16 * (18 - distance) ** 2 / 3
        return -.12 - .16 * (distance - 1.5)

    ramp_probes = 0
    for key, (cx, cz) in EXPECTED_SITES.items():
        ground_y = matrix(bs, next(x['trans'] for x in entrances if key in x['name']))[1, 3]
        for distance in np.arange(.375, 18, .375):
            for lateral in (-2.0, 0, 2.0):
                hh = hits(cx + lateral, cz - distance)
                # The saved body collider also includes the canopy's ribs.
                # Select the descending road surface, not the ceiling above it.
                floors = [y for y, p in hh if p == 'GarageEntrance.asset' and y <= ground_y + .025]
                assert floors, ('Ramp collision hole', key, distance, lateral)
                y = max(floors)
                assert abs(y - (ground_y + floor_at(distance))) < .012, ('Ramp profile changed', key, distance, y)
                assert not [(a, p) for a, p in hh if p != 'GarageEntrance.asset' and a > y + .025], \
                    ('Foreign ground blocks ramp', key, distance, lateral, hh)
                ramp_probes += 1
    out['clearRampCollisionProbes'] = ramp_probes

    ground_y = float(matrix(bs, west['trans'])[1, 3])
    deck_probes = 0
    # The previous west excavation's outer edge is now part of the passage.
    # Probe there, in the enlarged middle passage, and on both outer shoulders.
    for x in (19.1, 25.0, 26.2, 33.25):
        samples = []
        for z in np.arange(97.9, 121.001, .3):
            hh = hits(x, z)
            ys = [y for y, p in hh if p == 'GarageMouthAsphalt.asset']
            assert ys, ('Courtyard/passage collision hole', x, z, hh)
            y = max(ys)
            assert not [(a, p) for a, p in hh if p != 'GarageMouthAsphalt.asset' and a > y + .03], \
                ('Object blocks courtyard passage', x, z, hh)
            if x in (25.0, 26.2) and 103.0 <= z <= 114.9:
                assert abs(y - ground_y - .85) < .025, ('Courtyard is not flat', x, z, y)
            assert ground_y - .03 <= y <= ground_y + .88, ('Invalid courtyard height', x, z, y)
            samples.append((z, y))
            deck_probes += 1
        # Approaches must rise/fall smoothly instead of producing a fence/step.
        for (z0, y0), (z1, y1) in zip(samples, samples[1:]):
            assert abs(y1 - y0) / (z1 - z0) <= .20, ('Steep courtyard edge', x, z0, z1, y0, y1)
            if z1 <= 102.7:
                assert y1 >= y0 - .002, ('Rear courtyard approach is not monotonic', x, z0)
            if z0 >= 115.1:
                assert y1 <= y0 + .002, ('Front courtyard approach is not monotonic', x, z0)
    out['courtyardPassageAndRestoredOldFootprintProbes'] = deck_probes
    out['clearPassageWidthMeters'] = (30.3 - 2.495) - (22.1 + 2.495)
    assert out['clearPassageWidthMeters'] >= 3.2 - 1e-5

    # All unchanged meshes cancel exactly; changed ones are read from the base.
    oldmap, newmap = item_map(base), item_map(bs)
    assert oldmap.keys() == newmap.keys(), 'Direct mesh renderers were added/removed'
    totals = {'before': {'triangles': 0, 'nonemptySubmeshes': 0},
              'after': {'triangles': 0, 'nonemptySubmeshes': 0}}
    oldcache = {}
    for i, p in newmap.items():
        now = current_mesh(p)
        op = oldmap[i]
        rel = str(op.relative_to(REPO))
        if op == p and rel not in changed:
            old = now
        else:
            if op not in oldcache:
                oldcache[op] = mesh(op, original(op))
            old = oldcache[op]
        for key, m in [('before', old), ('after', now)]:
            totals[key]['triangles'] += len(m['idx']) // 3
            totals[key]['nonemptySubmeshes'] += sum(count > 0 for _, count, _, _ in m['subs'])
    assert totals['after']['triangles'] <= totals['before']['triangles'], ('Triangle budget increased', totals)
    assert totals['after']['nonemptySubmeshes'] <= totals['before']['nonemptySubmeshes'], ('Submesh budget increased', totals)
    out['directSavedRenderersAllLodsUpperBound'] = totals
    for x in items:
        if x['path'].name == 'ReferenceRoad.asset':
            assert x['path'].read_text() == original(x['path']), 'Carriageway/profile mesh changed'
    out['carriagewayMeshByteIdentical'] = True
    out['unityImportPlayerAndFrameTimes'] = 'NOT RUN by this saved-data validator'
    return out


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, help='Optional JSON report path')
    args = parser.parse_args()
    result = run()
    content = json.dumps(result, indent=2, ensure_ascii=False) + '\n'
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(content)
    print(content, end='')
