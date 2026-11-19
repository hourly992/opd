"""Generate tiled ground and concrete edge footprints from the saved Unity meshes."""
import argparse
import math
from pathlib import Path
import struct

import numpy as np
import shapely as sh

TILE = 64
PRECISION = .002
CURB_WIDTH = .145
COURT = [
    (-7, -45), (16, -45), (16, -56), (37, -56), (37, -24.6), (24.6, -24.6),
    (24.6, 4.2), (10.8, 4.2), (10.8, 30.2), (-7, 30.2),
]


def polygons(geometry):
    if geometry.geom_type == "Polygon" and not geometry.is_empty:
        yield geometry
    elif hasattr(geometry, "geoms"):
        for child in geometry.geoms:
            yield from polygons(child)


def central_only(polys, tile):
    return overlay(sh.intersection, polys, tile)


def area_only(geometry):
    if geometry.geom_type in ("Polygon", "MultiPolygon"):
        return geometry
    return sh.union_all(list(polygons(geometry)), grid_size=PRECISION)


def buffered(poly, width):
    raw = poly.buffer(width, quad_segs=2, join_style="mitre", mitre_limit=2)
    shaped = sh.set_precision(raw, PRECISION)
    return area_only(sh.make_valid(shaped)) if not shaped.is_valid else shaped


def together(items):
    try:
        return sh.union_all(items, grid_size=PRECISION)
    except sh.errors.GEOSException:
        # GEOS can reject a snap-rounded shell that touches a narrow hole.
        # Dissolving the repaired boundaries at floating precision retains it.
        return sh.union_all([sh.set_precision(poly.buffer(0), 0) for poly in items])


def overlay(operation, first, second):
    first, second = area_only(first), area_only(second)
    try:
        return area_only(operation(first, second, grid_size=PRECISION))
    except sh.errors.GEOSException:
        return area_only(operation(sh.set_precision(first.buffer(0), 0), sh.set_precision(second.buffer(0), 0)))


def triangulate(surface):
    for poly in polygons(surface):
        if poly.area < .00004:
            continue
        for tri in sh.constrained_delaunay_triangles(poly).geoms:
            if tri.area >= .000001:
                yield from (coordinate for point in tuple(tri.exterior.coords)[:3] for coordinate in point)


def front_edges(curbs, street, bounds):
    for poly in polygons(curbs):
        for ring in [poly.exterior, *poly.interiors]:
            coords = tuple(ring.coords)
            for a, b in zip(coords[:-1], coords[1:]):
                # Tile-border edges are segmentation artifacts, not retaining faces.
                if any(abs(a[k] - limit) < .001 and abs(b[k] - limit) < .001
                       for k, limit in [(0,bounds[0]),(1,bounds[1]),(0,bounds[2]),(1,bounds[3])]):
                    # Covered by either the neighboring tile or the actual shape edge.
                    midpoint = sh.Point((a[0] + b[0]) * .5, (a[1] + b[1]) * .5)
                    if street.distance(midpoint) > .015:
                        continue
                # No walls where the two top surfaces share the strip's rear edge.
                midpoint = sh.Point((a[0] + b[0]) * .5, (a[1] + b[1]) * .5)
                if street.distance(midpoint) <= .022:
                    yield *a, *b


def build(source, destination):
    records = np.fromfile(source, dtype=[("kind","<i4"),("tri","<f4",(3,3))])
    court = sh.set_precision(sh.Polygon(COURT), PRECISION)
    groups = {}
    for kind in range(1,5):
        coords = records["tri"][records["kind"] == kind][:,:,[0,2]]
        native = sh.polygons(coords)
        groups[kind] = native, sh.STRtree(native)
        print(f"Surfaces {kind}: {len(native)}", flush=True)

    def region(kind, boundary):
        items, tree = groups[kind]
        keys = tree.query(boundary, predicate="intersects")
        if not len(keys):
            return sh.Polygon()
        selected = items[keys]
        return area_only(sh.union_all(selected, grid_size=PRECISION))

    bounds = sh.total_bounds(groups[1][0])
    pending = destination.with_suffix(".partial")
    with pending.open("wb") as out:
        out.write(struct.pack("<ii", 1, 0))
        tiles = 0
        counts = [0,0,0]
        for z in range(math.floor(bounds[1]/TILE), math.ceil(bounds[3]/TILE)):
            for x in range(math.floor(bounds[0]/TILE), math.ceil(bounds[2]/TILE)):
                tile = sh.box(x*TILE, z*TILE, (x+1)*TILE, (z+1)*TILE)
                search = buffered(tile, .6)
                original = region(1, search)
                if original.is_empty:
                    continue
                street, existing, retained = (region(kind, search) for kind in (2,3,4))
                # Adjoining pavement panels often have small horizontal trim gaps.
                # These gaps are part of the passage, not strips to kerb around.
                street = buffered(buffered(street, .09), -.09)
                reserve = together([street, existing, retained, court])
                grass = overlay(sh.difference, original, reserve)
                new_kerb = overlay(sh.intersection,
                    grass,
                    overlay(sh.difference, buffered(street, CURB_WIDTH), buffered(existing, CURB_WIDTH + .025)),
                )
                new_kerb = sh.union_all([p for p in polygons(new_kerb) if p.area > .027], grid_size=PRECISION)
                lawn = overlay(sh.difference, grass, new_kerb)
                # The old curb's width and alignment are kept. Locally level its
                # crown with the new lawn where the two polygons actually meet.
                old_edge = overlay(sh.intersection, existing, buffered(grass, .19))
                old_edge = overlay(sh.difference, old_edge, together([court, retained, street]))
                capped = together([new_kerb, old_edge])
                lawn = central_only(lawn, tile)
                capped = central_only(capped, tile)
                if lawn.is_empty and capped.is_empty:
                    continue
                lawn = sh.segmentize(lawn, 4)
                capped = sh.segmentize(capped, .66)
                turf_xy = np.fromiter(triangulate(lawn), dtype="<f4")
                curb_xy = np.fromiter(triangulate(capped), dtype="<f4")
                walls = np.asarray(list(front_edges(capped, street, tile.bounds)), dtype="<f4")
                lengths = [len(turf_xy)//6,len(curb_xy)//6,len(walls)]
                counts = [a+b for a,b in zip(counts,lengths)]
                out.write(struct.pack("<iiiii", x, z, *lengths))
                turf_xy.tofile(out)
                curb_xy.tofile(out)
                walls.tofile(out)
                tiles += 1
            print(f"Finished z={z}. Tiles {tiles}, turf/curb/walls {counts}", flush=True)
        out.seek(4)
        out.write(struct.pack("<i", tiles))
    pending.replace(destination)
    print(f"Complete: {tiles} tiles, {counts}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    build(args.input, args.output)
