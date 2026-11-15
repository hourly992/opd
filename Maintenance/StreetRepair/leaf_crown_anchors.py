"""Bind rare falling leaves to existing, saved deciduous foliage triangles.

The caller owns scene serialization. This edits path metadata only: no tree,
renderer, transform, collider, material or mesh resource is created or moved.
"""
import json
import re
from functools import lru_cache

import numpy as np
import shapely as sh
from shapely.ops import unary_union

from unity_yaml import ROOT, GUID, blocks, field, guid, matrix, mesh, refs, scene, triangles, world
from surfaces import Sampler


MAX_ANCHORS = 48
LEAF_GUID = '9fd1a8ea87afadc468ea0e8185da9969'


def point(value):
    return np.array([float(v) for v in re.findall(r'[xyz]: ([^,}]+)', value)])


@lru_cache(None)
def crown_triangles(path):
    """Actual LOD0 leaf triangles in prefab-root coordinates, not a guessed ellipsoid."""
    bs = blocks(path.read_text())
    names = {i: field(b, 'm_Name', '') for i, (typ, b) in bs.items() if typ == 1}
    transforms = {int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1]): i
                  for i, (typ, b) in bs.items() if typ == 4}
    renderers = {int(re.search(r'm_GameObject: \{fileID: (\d+)', b)[1]): b
                 for typ, b in bs.values() if typ == 23}
    result = []
    for typ, block in bs.values():
        if typ != 33:
            continue
        go = int(re.search(r'm_GameObject: \{fileID: (\d+)', block)[1])
        if names.get(go) != 'LOD 0':
            continue
        material_paths = [GUID.get(g) for g in refs(renderers[go], 'm_Materials')]
        slots = [j for j, p in enumerate(material_paths) if p and
                 any(s in field(p.read_text(), 'm_Name', '').lower() for s in ('leaf', 'leaves', 'crown'))]
        source = GUID[re.search(r'm_Mesh: .*guid: (\w+)', block)[1]]
        m = mesh(source)
        v = world(bs, {'trans': transforms[go]}, m['a'][0])
        for slot in slots:
            tt = triangles(m, slot)
            result.extend((source, slot, int(j), p) for j, p in zip(range(len(tt)), v[tt].mean(1)))
    assert result, ('No actual LOD0 leaf triangles', path)
    return result


def tree_instances(bs):
    result = []
    for iid, (typ, b) in bs.items():
        if typ != 1001:
            continue
        source = re.search(r'm_SourcePrefab: .*guid: (\w+)', b)
        prefab = GUID.get(source[1]) if source else None
        if not prefab or not any(k in prefab.name for k in ('Broadleaf', 'Birch')):
            continue
        values = dict(re.findall(r'propertyPath: (m_Local(?:Position|Rotation|Scale)\.\w)\n\s+value: ([^\n]+)', b))
        if 'm_LocalPosition.x' not in values:
            continue
        parent = int(re.search(r'm_TransformParent: \{fileID: (\d+)', b)[1])
        # Reuse the Unity quaternion/scale helper with an isolated synthetic
        # transform. Overrides are the saved instance's root local values.
        fields = []
        for name, axes, defaults in [('Position', 'xyz', (0, 0, 0)), ('Rotation', 'xyzw', (0, 0, 0, 1)), ('Scale', 'xyz', (1, 1, 1))]:
            channels = ', '.join(f'{axis}: {values.get("m_Local"+name+"."+axis, default)}'
                                 for axis, default in zip(axes, defaults))
            fields.append(f'  m_Local{name}: {{{channels}}}')
        block = '\n'.join(fields) + f'\n  m_Father: {{fileID: {parent}}}\n'
        temp = dict(bs)
        temp[-1] = (4, block)
        mat = matrix(temp, -1)
        result.append(dict(instance=iid, prefab=prefab, matrix=mat, origin=mat[:3, 3]))
    return result


def obstacle_triangles(bs, items):
    """Saved non-vegetation direct geometry above ground, including thin props."""
    records = []
    for x in items:
        p = x['path']
        if p.suffix != '.asset' or any(k in p.stem.lower() for k in ('grass', 'leaf', 'leaves', 'foliage')):
            continue
        m = mesh(p)
        if not len(m['idx']):
            continue
        v = world(bs, x, m['a'][0])
        if v[:, 1].max() <= .6:
            continue
        tt = v[np.concatenate([triangles(m, j) for j in range(len(m['subs']))])]
        tt = tt[tt[:, :, 1].max(1) > .6]
        records.append((p, tt, tt.min((0, 1)), tt.max((0, 1))))
    return records


def clear_segment(start, end, obstacles):
    # Check a 22 cm swept envelope with five parallel segment tests. House
    # footprints additionally retain a 40 cm margin for the leaf's flutter.
    direction = end-start
    lo, hi = np.minimum(start, end)-.22, np.maximum(start, end)+.22
    for path, tt, bounds_lo, bounds_hi in obstacles:
        if np.any(bounds_lo > hi) or np.any(bounds_hi < lo):
            continue
        e1, e2 = tt[:, 1]-tt[:, 0], tt[:, 2]-tt[:, 0]
        h = np.cross(np.broadcast_to(direction, e2.shape), e2)
        a = np.einsum('ij,ij->i', e1, h)
        valid = abs(a) > 1e-8
        inverse = np.zeros_like(a)
        inverse[valid] = 1/a[valid]
        for shift in ((0, 0, 0), (.22, 0, 0), (-.22, 0, 0), (0, 0, .22), (0, 0, -.22)):
            s = start+np.array(shift)-tt[:, 0]
            u = inverse*np.einsum('ij,ij->i', s, h)
            q = np.cross(s, e1)
            v = inverse*np.einsum('j,ij->i', direction, q)
            t = inverse*np.einsum('ij,ij->i', e2, q)
            if np.any(valid & (u >= 0) & (v >= 0) & (u+v <= 1) & (t > .001) & (t < .985)):
                return False
    return True


def apply_leaf_crowns(bs, items=None):
    if items is None:
        _, _, items = scene()
    from connect_parking import hard_surface
    ground = [x for x in items if x['path'].suffix == '.asset' and hard_surface(x)
              and not any(w in x['path'].name for w in ('Grass_', 'GarageEntrance', 'GaragePaired', 'Canopy'))]
    sampler = Sampler(ground, bs)
    source = json.loads((ROOT / 'Assets/StreetSource/street.json').read_text())
    buildings = unary_union([sh.Polygon([(p['x'], p['z']) for p in b['points']])
                             for b in source['buildings'] if len(b['points']) >= 3]).buffer(.4)
    trees = tree_instances(bs)
    obstacles = obstacle_triangles(bs, items)
    origins = np.array([t['origin'][[0, 2]] for t in trees])
    assert len(trees) > MAX_ANCHORS
    group_id, original = next((i, b) for i, (typ, b) in bs.items() if typ == 114 and
                             LEAF_GUID in b and '    flightTime: 0\n' not in b)
    sections = re.findall(r'  - renderer:.*?(?=  - renderer:|\Z)', original, re.S)
    assert len(sections) >= MAX_ANCHORS
    selected = set(np.linspace(0, len(sections)-1, MAX_ANCHORS, dtype=int).tolist())
    used, anchors, output = set(), [], []
    wind = np.array([.82, .57])
    for n, section in enumerate(sections):
        # The bounded existing renderer pool remains saved, but only 48 paths
        # have a nonzero repeat period. Runtime caps nearby active flights too.
        if n not in selected:
            output.append(re.sub(r'    period: .*', '    period: 0', section))
            continue
        target = point(field(section, 'to'))
        candidates = sorted(trees, key=lambda t: np.linalg.norm((t['origin']-target)[[0, 2]]))
        accepted = None
        for tree in candidates[:100]:
            if tree['instance'] in used:
                continue
            data = crown_triangles(tree['prefab'])
            v = np.array([r[3] for r in data]) @ tree['matrix'][:3, :3].T + tree['matrix'][:3, 3]
            lo, hi = v[:, 1].min(), v[:, 1].max()
            center = tree['origin'][[0, 2]]
            radial = np.linalg.norm(v[:, [0, 2]]-center, axis=1)
            ids = np.flatnonzero((v[:, 1] >= lo+(hi-lo)*.25) & (v[:, 1] <= lo+(hi-lo)*.67)
                                 & (radial > max(.7, radial.max()*.55)))
            if not len(ids):
                continue
            # Prefer the outer downwind canopy so the descent avoids its trunk.
            order = ids[np.argsort(-(v[ids][:, [0, 2]]-center) @ wind)]
            for index in order[::max(1, len(order)//16)][:16]:
                start = v[index]
                end_xz = start[[0, 2]] + wind * 1.7
                line = sh.LineString([start[[0, 2]], end_xz])
                if line.intersects(buildings):
                    continue
                delta = end_xz-start[[0, 2]]
                along = np.clip((origins-start[[0, 2]]) @ delta / (delta @ delta), 0, 1)
                if np.any(np.linalg.norm(origins-(start[[0, 2]]+along[:, None]*delta), axis=1) < .4):
                    continue
                y = sampler.height(*end_xz, default=float('nan'))
                if not np.isfinite(y) or not -.2 <= y <= .4 or start[1] < y+2:
                    continue
                end = np.array([end_xz[0], y+.065, end_xz[1]])
                if not clear_segment(start, end, obstacles):
                    continue
                accepted = (tree, data[index], start, end)
                break
            if accepted:
                break
        assert accepted, ('No clear saved crown/ground path', n, target)
        tree, evidence, start, end = accepted
        used.add(tree['instance'])
        def vector(v):
            return '{' + ', '.join(f'{k}: {x:.8g}' for k, x in zip('xyz', v)) + '}'
        section = re.sub(r'    from: .*', '    from: ' + vector(start), section)
        section = re.sub(r'    to: .*', '    to: ' + vector(end), section)
        period = 34 + (n * 7 % 23)
        section = re.sub(r'    period: .*', f'    period: {period}', section)
        section = re.sub(r'    phase: .*', f'    phase: {(n*11.731)%period:.7g}', section)
        section = re.sub(r'    flightTime: .*', f'    flightTime: {min(8, max(4.7, (start[1]-end[1])*.95)):.7g}', section)
        output.append(section)
        anchors.append(dict(pathIndex=n, treeInstance=tree['instance'], prefab=str(tree['prefab'].relative_to(ROOT)),
                            foliageMesh=str(evidence[0].relative_to(ROOT)), foliageSubmesh=evidence[1],
                            foliageTriangle=evidence[2],
                            fromPosition=start.tolist(), toPosition=end.tolist(), periodSeconds=period))
    block = original[:original.index('  - renderer:')] + ''.join(output)
    bs[group_id] = (114, block)
    report = dict(component=group_id, availableDeciduousTreeInstances=len(trees),
                  existingDescendingRenderers=len(sections), enabledCrownPaths=len(anchors),
                  disabledPaths=len(sections)-len(anchors), additionalRenderers=0,
                  groundAndBuildingFootprintChecks=True,
                  directObstacleMeshesChecked=len(obstacles), sampledPathOffsetMeters=.22,
                  parallelPathSamples=5, trunkClearanceMeters=.4, anchors=anchors)
    return report


if __name__ == '__main__':
    source, bs, items = scene()
    report = apply_leaf_crowns(bs, items)
    output = ROOT.parent / 'work/atmosphere/crown-anchors.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: v for k, v in report.items() if k != 'anchors'}, indent=2))
