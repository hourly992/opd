"""Expand asphalt around both saved garage pairs, once from c5d2798.

No street regeneration: retain all objects/transforms, ramp models, materials,
road profile, red paving and shared historical terrain. Only the main scene's
ground meshes and existing asphalt batch are edited.
"""
import json
import re
import subprocess
from collections import Counter
from copy import deepcopy
from functools import lru_cache

import numpy as np
import shapely as sh
from shapely.geometry import Point, Polygon

from connect_parking import asphalt_slot, ensure_asphalt_basis, hard_surface, save_scene
from garages import garage_mesh
from surfaces import ART, Sampler, cut, folders, load, polygons, projected, union
from unity_yaml import ROOT, field, guid, matrix, meta, save_mesh, scene, world


BASE = 'c5d2798'
PLAZAS = {
    'north': dict(bounds=(16.1, 91.8, 36.3, 137.5), old=(18.75, 97.8, 33.65, 130.),
                  baseY=-.009999990463256836, direction=(0., -1.), mouth=(26.2, 121.),
                  stations=[91.8, 97.8, 102.8, 102.83, 115., 121., 130., 137.5]),
    'west': dict(bounds=(-121., 60.5, -81., 81.7), old=(-117., 63.65, -84.8, 78.55),
                 baseY=.06499999761581421, direction=(1., 0.), mouth=(-108., 71.1),
                 stations=[-121., -117., -108., -102., -90., -89.83, -89.8, -84.8, -81.])}
RAMP_HOLES = union([sh.box(x - 2.47, 102.83, x + 2.47, 121.) for x in (22.1, 30.3)] +
                   [sh.box(-108., z - 2.47, -89.83, z + 2.47) for z in (67., 75.2)])
RETIRED_COMPONENTS = (4000000109, 4000000110)


def retire_empty_court(bs):
    # Its top/collision has moved into LocalAsphalt. Keep identities while
    # avoiding an enabled collider with an empty mesh.
    for component in RETIRED_COMPONENTS:
        typ, block = bs[component]
        assert typ in (23, 64)
        bs[component] = (typ, re.sub(r'^  m_Enabled: [01]$', '  m_Enabled: 0', block, flags=re.M))


def active(bs, item):
    if field(item['rb'], 'm_Enabled') != '1':
        return False
    tid = item['trans']
    while tid:
        block = bs[tid][1]
        go = re.search(r'm_GameObject: \{fileID: (\d+)', block)
        if go and int(go[1]) in bs and field(bs[int(go[1])][1], 'm_IsActive', '1') != '1':
            return False
        parent = re.search(r'm_Father: \{fileID: (\d+)', block)
        tid = int(parent[1]) if parent else 0
    return True


@lru_cache(None)
def is_paving(path):
    if not hasattr(path, 'read_text'):
        return False
    s = path.read_text()
    return abs(float(field(s, '- _SurfaceClass', '0')) - 6) < 1e-6 or any(
        name in field(s, 'm_Name', '') for name in ('Paving', 'Paver', 'RunningBond'))


def compact(m):
    """Keep every referenced channel value; discard vertices from removed ground."""
    used = np.unique(np.concatenate([t.reshape(-1) for t in m['tri']]))
    remap = np.full(len(m['a'][0]), -1, dtype=np.int64)
    remap[used] = np.arange(len(used))
    m['a'] = {k: a[used].copy() for k, a in m['a'].items()}
    m['tri'] = [remap[t] for t in m['tri']]


def clean(path):
    path.write_text('\n'.join(line.rstrip() for line in path.read_text().splitlines()) + '\n')


def build_top(label, definition, pad, sampler):
    """Sparse exact contour strips, split only at slope knots and edge band.

The 0.85 m edge band belongs to the outer clipped pad, before ramp openings
are subtracted: exterior asphalt consequently stays high beside the walls.
"""
    inner = pad.buffer(-.85, join_style=2)
    band = pad.difference(inner)
    base_y = definition['baseY']
    old = sh.box(*definition['old'])
    mouth = np.asarray(definition['mouth'])
    forward = np.asarray(definition['direction'])
    boundary = pad.boundary

    @lru_cache(None)
    def height(x, z):
        p = Point(x, z)
        distance = np.dot(np.array([x, z]) - mouth, forward)
        longitudinal = min(1., max(0., distance / 6.), max(0., (23.2 - distance) / 5.))
        lateral = min(1., boundary.distance(p) / .85)
        weight = min(longitudinal, lateral)
        # The earlier courtyard is already raised/excavated, so its visible
        # surface is not a valid sample of the original grade beneath it.
        ground_y = base_y if old.covers(p) else sampler.height(x, z, default=base_y)
        return ground_y * (1 - weight) + (base_y + .85) * weight

    vs, ts, vertex_ids = [], [], {}
    xmin, zmin, xmax, zmax = definition['bounds']
    stations = definition['stations']
    for a, b in zip(stations, stations[1:]):
        strip = sh.box(xmin, a, xmax, b) if label == 'north' else sh.box(a, zmin, b, zmax)
        for zone in (inner, band):
            part = zone.intersection(strip).difference(RAMP_HOLES)
            for poly in polygons(part):
                if poly.area < 1e-9:
                    continue
                for triangle in sh.constrained_delaunay_triangles(poly).geoms:
                    if triangle.area < 1e-9:
                        continue
                    pts = list(triangle.exterior.coords)[:3]
                    if sh.is_ccw(triangle.exterior):
                        pts = pts[::-1]
                    indices = []
                    for x, z in pts:
                        key = (round(x, 7), round(z, 7))
                        if key not in vertex_ids:
                            vertex_ids[key] = len(vs)
                            vs.append((x, height(*key), z))
                        indices.append(vertex_ids[key])
                    ts.append(indices)
    path = ART / ('AutumnPlazaAssembly_' + label.title() + '.asset')
    garage_mesh(path, vs, [ts])
    result = load(path)
    return path, result


def append_mesh(dest, source):
    start = len(dest['a'][0])
    assert set(dest['a']) == set(source['a']), ('Asphalt channel mismatch', set(dest['a']), set(source['a']))
    dest['a'] = {k: np.concatenate([a, source['a'][k].astype(a.dtype)]) for k, a in dest['a'].items()}
    dest['tri'][0] = np.concatenate([dest['tri'][0], source['tri'][0] + start])


def run():
    s, bs, items = scene()
    source_scene = subprocess.check_output(['git', 'show', BASE + ':Unity_Walk/Assets/Scenes/Street.unity'],
                                            cwd=ROOT.parent, text=True)
    if s != source_scene:
        raise SystemExit('Requires the unchanged saved Street.unity at ' + BASE + '; refusing to overwrite edits.')
    folders(ART / 'Surfaces')
    active_items = [x for x in items if active(bs, x)]
    paving = union([projected(load(x['path']), [j for j, p in enumerate(x['materials']) if is_paving(p)], bs, x)
                    for x in active_items if x['path'].suffix == '.asset' and any(is_paving(p) for p in x['materials'])])
    data = json.loads((ROOT / 'Assets/StreetSource/street.json').read_text())
    houses = union([Polygon([(p['x'], p['z']) for p in b['points']]) for b in data['buildings']])
    pads = {label: sh.box(*definition['bounds']).difference(paving.union(houses)) for label, definition in PLAZAS.items()}
    mask = union(list(pads.values()))
    # Ramp bodies/canopies and the carriageway are immutable. Raised yard is
    # ground; its old surface is removed before the expanded top is appended.
    ground = [x for x in active_items if hard_surface(x) and not any(
        word in x['path'].name for word in ('GarageEntrance', 'GaragePaired', 'GarageBarrelCanopy'))
        and x['path'].name != 'ReferenceRoad.asset' and not all(is_paving(p) for p in x['materials'])]
    sampler = Sampler([x for x in ground if 'Grass_' not in x['path'].name and x['path'].name != 'GarageMouthAsphalt.asset'], bs)
    assemblies = [build_top(label, definition, pads[label], sampler) for label, definition in PLAZAS.items()]
    refs = Counter(x['path'] for x in items)
    changed, clones = [], []
    for x in ground:
        # Shared source meshes must start from the same unmodified geometry
        # for each instance's own world transform and isolated clone.
        m = deepcopy(load(x['path'])) if refs[x['path']] > 1 else load(x['path'])
        v = world(bs, x, m['a'][0])
        if not len(v) or not mask.intersects(sh.box(v[:, 0].min(), v[:, 2].min(), v[:, 0].max(), v[:, 2].max())):
            continue
        protected = {j: t.copy() for j, t in enumerate(m['tri']) if is_paving(x['materials'][j])}
        if len(protected) == len(m['tri']):
            continue
        if not cut(m, bs, x, mask):
            continue
        # Even vertical paver edges are preserved, not just their top projection.
        for j, t in protected.items():
            m['tri'][j] = t
        compact(m)
        original = x['path']
        if not original.is_relative_to(ART) or refs[original] > 1:
            target = ART / 'Surfaces' / ('AutumnPlaza_' + original.stem + '_' + str(x['go']) + '.asset')
            old_guid = guid(original)
            save_mesh(m, target)
            meta(target)
            new_guid = guid(target)
            for cid, (typ, block) in list(bs.items()):
                if typ in (33, 64) and f'm_GameObject: {{fileID: {x["go"]}}}' in block:
                    bs[cid] = (typ, block.replace(old_guid, new_guid))
            x['path'] = target
            clones.append(dict(source=str(original.relative_to(ROOT)), repaired=str(target.relative_to(ROOT)), gameObject=x['go']))
            clean(target)
            clean(type(target)(str(target) + '.meta'))
        else:
            save_mesh(m)
            clean(original)
        changed.append(str(x['path'].relative_to(ROOT)))
    destination = next(x for x in items if x['path'].name == 'LocalAsphalt.asset')
    assert np.allclose(matrix(bs, destination['trans']), np.eye(4)), 'Expected the existing world-space asphalt batch'
    dest = load(destination['path'])
    ensure_asphalt_basis(dest)
    for _, assembly in assemblies:
        append_mesh(dest, assembly)
    compact(dest)
    ensure_asphalt_basis(dest)
    save_mesh(dest)
    clean(destination['path'])
    retire_empty_court(bs)
    save_scene(s, bs)
    report = dict(baseCommit=subprocess.check_output(['git', 'rev-parse', BASE], cwd=ROOT.parent, text=True).strip(),
                  unchangedRampModels=True, unchangedMaterials=True, unchangedRoadProfile=True,
                  groundAssetsChanged=changed, isolatedGroundClones=clones,
                  asphaltBatch=str(destination['path'].relative_to(ROOT)),
                  asphaltBatchTriangles=sum(len(t) for t in dest['tri']), asphaltBatchVertices=len(dest['a'][0]),
                  newRenderers=0, retiredEmptyGroundComponents=list(RETIRED_COMPONENTS), sideEdgeBlendMeters=.85,
                  riseMeters=.85, frontRiseLengthMeters=6., rearReturnLengthMeters=5.,
                  plazas={label: dict(rawBounds=list(definition['bounds']), clippedPadArea=pads[label].area,
                                      asphaltTopArea=pads[label].difference(RAMP_HOLES).area,
                                      projectedFootprint=sh.geometry.mapping(pads[label].difference(RAMP_HOLES)),
                                      fullGroundCutFootprint=sh.geometry.mapping(pads[label]),
                                      baseY=definition['baseY'], originalGradeExtraApron=True,
                                      generatedTriangles=sum(len(t) for t in assembly['tri']))
                          for (label, definition), (_, assembly) in zip(PLAZAS.items(), assemblies)})
    for path, _ in assemblies:
        path.unlink()
        type(path)(str(path) + '.meta').unlink()
    output = ROOT.parent / 'work/plaza-autumn'
    output.mkdir(parents=True, exist_ok=True)
    (output / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    run()
