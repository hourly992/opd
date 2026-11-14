"""Bake mirrored garage visuals into the two existing renderer pairs.

The mirrored entrances keep their own transforms, metadata and small original
MeshColliders. This helper only changes the four original MeshFilters; it does
not add renderers or make the original colliders span the street.
"""
import re

import numpy as np

from garages import ART
from surfaces import folders
from unity_yaml import guid, matrix, mesh, meta, save_mesh, triangles


def _site_matrix(site):
    angle = np.deg2rad(float(site['yaw']))
    c, s = np.cos(angle), np.sin(angle)
    result = np.eye(4)
    result[:3, :3] = [[c, 0, s], [0, 1, 0], [-s, 0, c]]
    result[:3, 3] = [float(site[k]) for k in ('x', 'y', 'z')]
    return result


def _check_basis_and_winding(m):
    v, n, tangent = (m['a'][k] for k in (0, 1, 2))
    assert all(len(a) == len(v) and np.isfinite(a).all() for a in m['a'].values())
    assert np.allclose(np.linalg.norm(n, axis=1), 1, atol=2e-4)
    assert np.allclose(np.linalg.norm(tangent[:, :3], axis=1), 1, atol=2e-4)
    assert np.max(abs(np.sum(n * tangent[:, :3], axis=1))) < 2e-4
    assert np.allclose(abs(tangent[:, 3]), 1, atol=1e-6)
    for indices in m['tri']:
        assert not indices.size or (indices.min() >= 0 and indices.max() < len(v))
        p = v[indices]
        cross = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        assert np.all(np.linalg.norm(cross, axis=1) > 1e-9)
        assert np.all(np.sum(cross * n[indices].mean(1), axis=1) > 1e-9)


def _pair_mesh(source, relative):
    """Return original + rotated stamp without writing anything to disk."""
    m = mesh(source)
    assert {0, 1, 2, 3, 4}.issubset(m['a']), 'Garage mesh is missing its saved basis/color/UV channels'
    linear = relative[:3, :3]
    determinant = np.linalg.det(linear)
    assert determinant > 1e-8, 'Garage batching expects positive-scale rotation/translation transforms'
    # Source and destination transforms are normally rigid. Inverse transpose
    # keeps the normal basis correct if an existing positive scale is present.
    normal_matrix = np.linalg.inv(linear).T
    count = len(m['a'][0])
    stamp = {k: a.copy() for k, a in m['a'].items()}
    stamp[0] = m['a'][0] @ linear.T + relative[:3, 3]
    stamp[1] = m['a'][1] @ normal_matrix.T
    stamp[1] /= np.linalg.norm(stamp[1], axis=1)[:, None]
    tangent = m['a'][2][:, :3] @ linear.T
    tangent -= stamp[1] * np.sum(tangent * stamp[1], axis=1)[:, None]
    tangent /= np.linalg.norm(tangent, axis=1)[:, None]
    stamp[2] = np.column_stack([tangent, m['a'][2][:, 3]])
    m['a'] = {k: np.concatenate([a, stamp[k].astype(a.dtype)], axis=0) for k, a in m['a'].items()}
    m['tri'] = [np.concatenate([triangles(m, j), triangles(m, j) + count])
                for j in range(len(m['subs']))]
    _check_basis_and_winding(m)
    return m


def batch(bs, items, mirror_sites):
    """Modify four existing MeshFilters in-place and return geometry counters.

    ``mirror_sites`` contains two dictionaries with id, x/y/z and yaw degrees.
    IDs contain ``west`` / ``east`` to associate each stamp with its original
    entrance. The caller creates mirrored collider-only objects separately and
    serializes ``bs`` after this helper returns.
    """
    assert len(mirror_sites) == 2, 'Exactly one mirrored pair is expected'
    sites = {}
    for side in ('west', 'east'):
        matches = [p for p in mirror_sites if side in p['id'].lower()]
        assert len(matches) == 1, ('Mirrored site needs an unambiguous west/east ID', side)
        sites[side] = matches[0]
    assert sites['west'] is not sites['east']
    folders(ART)
    report = {'existingRenderersReused': 4, 'newRenderers': 0, 'meshes': []}
    unchanged_colliders = {i: b for i, (typ, b) in bs.items() if typ == 64}
    for source_name, label, slots in [('GarageEntrance.asset', 'Body', 2),
                                      ('GarageBarrelCanopy.asset', 'Canopy', 1)]:
        # Collider-only mirrored records are allowed in the caller's list.
        originals = [x for x in items if x.get('filter') in bs and
                     getattr(x.get('path'), 'name', None) == source_name]
        assert len(originals) == 2, ('Expected two original shared garage meshes', source_name)
        for side in ('west', 'east'):
            matches = [x for x in originals if side in x['name'].lower()]
            assert len(matches) == 1, ('Original garage label is ambiguous', source_name, side)
            item = matches[0]
            relative = np.linalg.inv(matrix(bs, item['trans'])) @ _site_matrix(sites[side])
            combined = _pair_mesh(item['path'], relative)
            assert len(combined['tri']) == slots == len(item['materials'])
            path = ART / f'GaragePaired{label}_{side.title()}.asset'
            save_mesh(combined, path)
            path.write_text('\n'.join(line.rstrip() for line in path.read_text().splitlines()) + '\n')
            meta(path)
            mp = path.with_suffix(path.suffix + '.meta')
            mp.write_text('\n'.join(line.rstrip() for line in mp.read_text().splitlines()) + '\n')
            component_type, block = bs[item['filter']]
            assert component_type == 33
            block, replacements = re.subn(r'm_Mesh: .*',
                f'm_Mesh: {{fileID: 4300000, guid: {guid(path)}, type: 2}}', block)
            assert replacements == 1
            bs[item['filter']] = (component_type, block)
            item['path'] = path
            report['meshes'].append({'mesh': str(path.relative_to(ART)),
                                    'vertices': len(combined['a'][0]),
                                    'triangles': sum(len(t) for t in combined['tri']),
                                    'nonemptySubmeshes': sum(bool(len(t)) for t in combined['tri']),
                                    'mirroredSite': sites[side]['id']})
    assert unchanged_colliders == {i: b for i, (typ, b) in bs.items() if typ == 64}, \
        'Batching must not modify or widen any collider'
    report['allFourEntrancesTriangles'] = sum(p['triangles'] for p in report['meshes'])
    report['nonemptyGarageSubmeshes'] = sum(p['nonemptySubmeshes'] for p in report['meshes'])
    return report
