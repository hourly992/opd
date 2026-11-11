using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Uses the existing OSM playgrounds, never invents a new yard or moves a building.
// Equipment and surfacing are a plausible reconstruction, explicitly marked in PlaygroundSite.
internal static class PlaygroundBuilder
{
    [Serializable] private sealed class Document { public Site[] sites; }
    [Serializable] private sealed class Site
    {
        public long id;
        public string source;
        public StreetBuilder.Point[] points;
        public bool equipmentConfirmed;
        private Vector3[] polygon;
        internal Vector3[] Polygon => polygon ?? (polygon = points.Select(p => p.V).ToArray());
    }
    private readonly struct Item
    {
        internal readonly string name;
        internal readonly Vector2 clearance;
        internal Item(string name, float x, float z) { this.name = name; clearance = new Vector2(x, z); }
    }
    private static readonly Item[] Items = {
        new Item("Slide", 4.0f, 7.4f), new Item("Swing", 5.6f, 6.8f),
        new Item("Climber", 4.2f, 4.2f), new Item("Sandpit", 4.0f, 4.0f)
    };
    private static Document evidence;
    private static readonly Dictionary<string, GameObject> equipmentPrefabs = new Dictionary<string, GameObject>();
    private static Material blue, red, yellow, steel, rubber, wood, sand;
    private static Vector3[][] localBuildings;
    private static readonly List<(Vector3 a, Vector3 b, float radius)> localRoads = new List<(Vector3, Vector3, float)>();

    private static Document Evidence => evidence ?? (evidence = JsonUtility.FromJson<Document>(
        File.ReadAllText("Assets/StreetSource/playground_evidence.json")));

    // Landscape generators can exclude the entire actual footprint, before placing vegetation.
    internal static bool Contains(Vector3 position, float margin = 0)
    {
        foreach (var site in Evidence.sites)
        {
            var polygon = site.Polygon;
            if (Inside(position, polygon)) return true;
            if (margin > 0 && EdgeDistance(position, polygon) < margin) return true;
        }
        return false;
    }

    internal static void Generate(Transform parent)
    {
        evidence = null; equipmentPrefabs.Clear();
        EnvironmentMesh.EnsureFolders();
        blue = EnvironmentMesh.Material("PlaygroundBlue", new Color(.075f, .27f, .44f), 0, .38f, .2f);
        red = EnvironmentMesh.Material("PlaygroundRed", new Color(.62f, .13f, .075f), 0, .32f, .12f);
        yellow = EnvironmentMesh.Material("PlaygroundOchre", new Color(.85f, .58f, .10f), 0, .28f);
        steel = EnvironmentMesh.Material("PlaygroundSteel", new Color(.58f, .62f, .64f), 0, .62f, .75f);
        rubber = EnvironmentMesh.Material("PlaygroundRubber", new Color(.045f, .052f, .050f));
        StreetSurfaceLibrary.ApplyNamed(rubber, "Rubber");
        wood = EnvironmentMesh.Material("PlaygroundTimber", new Color(.65f, .45f, .22f));
        StreetSurfaceLibrary.ApplyNamed(wood, "Wood");
        sand = EnvironmentMesh.Material("PlaygroundSand", Color.white);
        StreetSurfaceLibrary.Apply(sand, "gravel_floor_02", 1.7f, new Color(1.20f, 1.06f, .78f), .34f);
        sand.SetFloat("_SurfaceClass", 0); sand.SetFloat("_Parallax", .001f);
        var data = JsonUtility.FromJson<StreetBuilder.Data>(File.ReadAllText("Assets/StreetSource/street.json"));
        var root = new GameObject("Детские площадки · контуры OSM, оборудование приблизительно").transform;
        root.SetParent(parent, false);
        foreach (var site in Evidence.sites)
        {
            Vector3[] polygon = site.Polygon;
            float minX = polygon.Min(p => p.x) - 2, maxX = polygon.Max(p => p.x) + 2;
            float minZ = polygon.Min(p => p.z) - 2, maxZ = polygon.Max(p => p.z) + 2;
            localBuildings = data.buildings.Select(b => b.points.Select(p => p.V).ToArray()).Where(p =>
                p.Max(v => v.x) >= minX && p.Min(v => v.x) <= maxX && p.Max(v => v.z) >= minZ && p.Min(v => v.z) <= maxZ).ToArray();
            localRoads.Clear();
            foreach (var road in data.roads)
                for (int i = 0; i < road.points.Length - 1; i++)
                {
                    Vector3 a = road.points[i].V, b = road.points[i + 1].V;
                    float radius = road.width * .5f + .25f;
                    if (Mathf.Max(a.x, b.x) + radius >= minX && Mathf.Min(a.x, b.x) - radius <= maxX &&
                        Mathf.Max(a.z, b.z) + radius >= minZ && Mathf.Min(a.z, b.z) - radius <= maxZ)
                        localRoads.Add((a, b, radius));
                }
            var surface = Surface(site.id, polygon);
            surface.transform.SetParent(root, false);
            var marker = surface.AddComponent<PlaygroundSite>();
            marker.osmId = site.id; marker.source = site.source;
            marker.equipmentPhotoConfirmed = site.equipmentConfirmed;
            marker.footprint = polygon;
            marker.ground = surface.AddComponent<MeshCollider>();
            marker.ground.sharedMesh = surface.GetComponent<MeshFilter>().sharedMesh;
            var reserved = new List<Rect>();
            var placed = new List<Transform>();
            foreach (var item in Items)
            {
                if (!FindSpace(polygon, item.clearance, reserved, out Vector3 position, out float angle, out Rect area)) continue;
                if (!equipmentPrefabs.TryGetValue(item.name, out var prefab))
                    equipmentPrefabs[item.name] = prefab = BuildEquipment(item.name);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, surface.transform);
                instance.transform.SetLocalPositionAndRotation(position + Vector3.up * .025f, Quaternion.Euler(0, angle, 0));
                placed.Add(instance.transform); reserved.Add(area);
            }
            marker.equipment = placed.ToArray();
            marker.worldBounds = new Bounds(polygon[0], Vector3.zero);
            foreach (var p in polygon) marker.worldBounds.Encapsulate(p + Vector3.up * 3.4f);
            var views = new List<Transform>();
            Vector3 center = marker.worldBounds.center; center.y = .8f;
            foreach (Vector3 offset in new[] { new Vector3(8, 5.2f, -11), new Vector3(-7, 2.1f, 8) })
            {
                var view = new GameObject("Площадка " + site.id + " · контрольный ракурс").transform;
                view.SetParent(surface.transform, false); view.position = center + offset;
                view.rotation = Quaternion.LookRotation(center - view.position); views.Add(view);
            }
            marker.inspectionViews = views.ToArray();
            Debug.Log("PLAYGROUND_GENERATED " + site.id + " equipment=" + placed.Count + " photoConfirmed=" + site.equipmentConfirmed);
        }
    }

    private static bool FindSpace(Vector3[] polygon, Vector2 dimensions,
        List<Rect> occupied, out Vector3 position, out float angle, out Rect area)
    {
        float minX = polygon.Min(p => p.x), maxX = polygon.Max(p => p.x);
        float minZ = polygon.Min(p => p.z), maxZ = polygon.Max(p => p.z);
        // Deterministic placement with clearance rectangles, clipped to the mapped boundary.
        // Two orientations retain full equipment scale rather than shrinking it into a narrow plot.
        foreach (float rotation in new[] { 0f, 90f })
        {
            Vector2 size = rotation == 0 ? dimensions : new Vector2(dimensions.y, dimensions.x);
            for (float z = minZ + size.y * .5f + .4f; z <= maxZ - size.y * .5f - .4f; z += .45f)
                for (float x = minX + size.x * .5f + .4f; x <= maxX - size.x * .5f - .4f; x += .45f)
                {
                    var candidate = new Rect(x - size.x * .5f, z - size.y * .5f, size.x, size.y);
                    if (occupied.Any(r => r.Overlaps(new Rect(candidate.xMin - .25f, candidate.yMin - .25f,
                        candidate.width + .5f, candidate.height + .5f)))) continue;
                    bool clear = true;
                    int nx = Mathf.CeilToInt(size.x / .5f), nz = Mathf.CeilToInt(size.y / .5f);
                    for (int iz = 0; iz <= nz && clear; iz++)
                        for (int ix = 0; ix <= nx; ix++)
                        {
                            var p = new Vector3(candidate.xMin + size.x * ix / nx, 0, candidate.yMin + size.y * iz / nz);
                            if (!Inside(p, polygon) || EdgeDistance(p, polygon) < .3f || Obstructed(p)) { clear = false; break; }
                        }
                    if (!clear) continue;
                    position = new Vector3(x, 0, z); angle = rotation; area = candidate; return true;
                }
        }
        position = default; angle = 0; area = default; return false;
    }

    private static bool Obstructed(Vector3 p)
    {
        if (StreetRoadBuilder.IsPaved(p)) return true;
        foreach (var road in localRoads)
            if (SegmentDistance(p, road.a, road.b) < road.radius) return true;
        foreach (var points in localBuildings)
        {
            if (Inside(p, points) || EdgeDistance(p, points) < 1) return true;
        }
        return false;
    }

    private static GameObject Surface(long id, Vector3[] polygon)
    {
        var mesh = new EnvironmentMesh();
        // Ear clipping supports the concave north-eastern playground without filling its notches.
        var remaining = Enumerable.Range(0, polygon.Length).ToList();
        float signed = 0;
        for (int i = 0; i < polygon.Length; i++) signed += polygon[i].x * polygon[(i + 1) % polygon.Length].z - polygon[(i + 1) % polygon.Length].x * polygon[i].z;
        if (signed < 0) remaining.Reverse();
        int guard = polygon.Length * polygon.Length;
        while (remaining.Count >= 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int k = 0; k < remaining.Count; k++)
            {
                int ia = remaining[(k + remaining.Count - 1) % remaining.Count], ib = remaining[k], ic = remaining[(k + 1) % remaining.Count];
                Vector3 a = polygon[ia], b = polygon[ib], c = polygon[ic];
                if (Cross(b - a, c - b) <= .00001f) continue;
                if (remaining.Any(j => j != ia && j != ib && j != ic && InTriangle(polygon[j], a, b, c))) continue;
                mesh.Face(sand, Vector3.up, a + Vector3.up * .025f, b + Vector3.up * .025f, c + Vector3.up * .025f);
                remaining.RemoveAt(k); clipped = true; break;
            }
            if (!clipped) throw new InvalidOperationException("Cannot triangulate mapped playground " + id);
        }
        // A low buried edging retains the sand; it is not a high obstacle around the site.
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector3 a = polygon[i], b = polygon[(i + 1) % polygon.Length], d = b - a;
            mesh.Box(wood, (a + b) * .5f - Vector3.up * .025f, new Vector3(.075f, .10f, d.magnitude), Quaternion.LookRotation(d));
        }
        return mesh.Create("MappedPlayground_" + id, "Площадка OSM " + id + " · оборудование по типовым размерам");
    }

    private static GameObject BuildEquipment(string kind)
    {
        var root = new GameObject(kind == "Swing" ? "Двойные качели с цепями" : kind == "Slide" ? "Горка с лестницей и площадкой" : kind == "Climber" ? "Лазалка с рукоходом и сеткой" : "Песочница с широким бортом");
        var renderers = new Renderer[2];
        for (int lod = 0; lod < 2; lod++)
        {
            var mesh = new EnvironmentMesh();
            if (kind == "Swing") Swing(mesh, lod);
            else if (kind == "Slide") Slide(mesh, lod);
            else if (kind == "Climber") Climber(mesh, lod);
            else Sandpit(mesh, lod);
            var part = mesh.Create("Playground" + kind + "_LOD" + lod, kind + " · LOD" + lod);
            part.transform.SetParent(root.transform, false); renderers[lod] = part.GetComponent<Renderer>();
            if (lod == 0)
            {
                // Static mesh collider follows steps, open frames and slide; no invisible solid bounding boxes.
                var collider = part.AddComponent<MeshCollider>(); collider.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
            }
        }
        var group = root.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(.045f, new[] { renderers[0] }), new LOD(.006f, new[] { renderers[1] }) });
        group.RecalculateBounds();
        return EnvironmentMesh.SavePrefab(root, "Playground" + kind);
    }

    private static void Tube(EnvironmentMesh mesh, Material material, Vector3 a, Vector3 b, float radius, int lod) =>
        mesh.Cylinder(material, a, b, radius, radius, lod == 0 ? 12 : 6);

    private static void Bolt(EnvironmentMesh mesh, Vector3 p, int lod)
    {
        if (lod > 0) return;
        mesh.Cylinder(steel, p, p + Vector3.forward * .014f, .022f, .022f, 6);
    }

    private static void Swing(EnvironmentMesh mesh, int lod)
    {
        // A-frame with braced feet, visible pivot hangers and two reinforced rubber seats.
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * 1.72f;
            foreach (int fore in new[] { -1, 1 })
            {
                Tube(mesh, blue, new Vector3(x, .06f, fore * 1.12f), new Vector3(x, 2.45f, 0), .055f, lod);
                mesh.RoundedBox(steel, new Vector3(x, .045f, fore * 1.12f), new Vector3(.19f, .06f, .22f), .025f);
                Bolt(mesh, new Vector3(x, .08f, fore * 1.12f), lod);
            }
            Tube(mesh, blue, new Vector3(x, .76f, -.79f), new Vector3(x, .76f, .79f), .035f, lod);
        }
        Tube(mesh, red, new Vector3(-1.90f, 2.45f, 0), new Vector3(1.90f, 2.45f, 0), .073f, lod);
        foreach (float x in new[] { -.78f, .78f })
        {
            mesh.RoundedBox(rubber, new Vector3(x, .46f, 0), new Vector3(.46f, .07f, .21f), .065f);
            mesh.Box(steel, new Vector3(x, .42f, 0), new Vector3(.37f, .025f, .16f));
            foreach (int side in new[] { -1, 1 })
            {
                float cx = x + side * .20f;
                Tube(mesh, steel, new Vector3(cx, 2.30f, 0), new Vector3(cx, 2.45f, 0), .026f, lod);
                if (lod == 0)
                    for (int link = 0; link < 39; link++)
                        ChainLink(mesh, new Vector3(cx, .54f + link * .044f, 0), link % 2 == 0);
                else Tube(mesh, steel, new Vector3(cx, .51f, 0), new Vector3(cx, 2.28f, 0), .009f, lod);
            }
        }
    }

    private static void ChainLink(EnvironmentMesh mesh, Vector3 center, bool alternate)
    {
        const int sides = 8;
        Vector3 P(int i)
        {
            float angle = i * Mathf.PI * 2 / sides;
            var v = new Vector3(Mathf.Cos(angle) * .014f, Mathf.Sin(angle) * .028f, 0);
            return center + (alternate ? Quaternion.Euler(0, 90, 0) * v : v);
        }
        for (int i = 0; i < sides; i++) mesh.Cylinder(steel, P(i), P(i + 1), .0035f, .0035f, 5);
    }

    private static void Slide(EnvironmentMesh mesh, int lod)
    {
        // 1.2 m deck, 200 mm steps, handrails, panel guards and folded stainless slide.
        foreach (float x in new[] { -.66f, .66f })
            foreach (float z in new[] { -.4f, .8f })
            {
                Tube(mesh, blue, new Vector3(x, .06f, z), new Vector3(x, 2.35f, z), .048f, lod);
                mesh.RoundedBox(steel, new Vector3(x, .055f, z), new Vector3(.14f, .10f, .14f), .02f);
            }
        for (int plank = 0; plank < 8; plank++)
            mesh.RoundedBox(wood, new Vector3(0, 1.2f, -.34f + plank * .15f), new Vector3(1.33f, .07f, .14f), .012f);
        foreach (int side in new[] { -1, 1 })
        {
            mesh.RoundedBox(yellow, new Vector3(side * .66f, 1.66f, .20f), new Vector3(.035f, .67f, 1.12f), .06f);
            Tube(mesh, red, new Vector3(side * .67f, .71f, -2.1f), new Vector3(side * .67f, 1.99f, -.40f), .027f, lod);
            Tube(mesh, blue, new Vector3(side * .62f, .06f, -2.1f), new Vector3(side * .62f, 1.16f, -.40f), .042f, lod);
            foreach (float z in new[] { -.25f, .15f, .55f }) Bolt(mesh, new Vector3(side * .685f, 1.87f, z), lod);
        }
        for (int step = 0; step < 6; step++)
            mesh.RoundedBox(wood, new Vector3(0, .20f * (step + 1), -2.08f + step * .29f), new Vector3(1.17f, .055f, .28f), .015f);
        // Entrance gap remains open between the side guards.
        Tube(mesh, red, new Vector3(-.66f, 2.05f, -.4f), new Vector3(.66f, 2.05f, -.4f), .028f, lod);
        foreach (int side in new[] { -1, 1 })
            Tube(mesh, red, new Vector3(side * .41f, 1.23f, .79f), new Vector3(side * .41f, 1.92f, .79f), .025f, lod);
        Tube(mesh, red, new Vector3(-.41f, 1.92f, .79f), new Vector3(.41f, 1.92f, .79f), .025f, lod);
        var profile = new[] { new Vector2(.75f, 1.24f), new Vector2(.98f, 1.22f), new Vector2(1.25f, 1.10f),
            new Vector2(2.38f, .30f), new Vector2(2.70f, .16f), new Vector2(3.03f, .12f), new Vector2(3.25f, .12f) };
        for (int i = 0; i < profile.Length - 1; i++)
        {
            var a = new Vector3(0, profile[i].y, profile[i].x); var b = new Vector3(0, profile[i + 1].y, profile[i + 1].x);
            var n = Vector3.Cross(Vector3.right, b - a).normalized;
            if (n.y < 0) n = -n;
            mesh.Face(steel, n, a + Vector3.left * .34f, a + Vector3.right * .34f, b + Vector3.right * .34f, b + Vector3.left * .34f);
            mesh.Face(steel, -n, a + Vector3.left * .34f - Vector3.up * .012f, b + Vector3.left * .34f - Vector3.up * .012f,
                b + Vector3.right * .34f - Vector3.up * .012f, a + Vector3.right * .34f - Vector3.up * .012f);
            foreach (int side in new[] { -1, 1 })
            {
                var c = a + Vector3.right * (side * .35f); var d = b + Vector3.right * (side * .35f);
                mesh.Face(steel, Vector3.right * side, c, d, d + Vector3.up * .14f, c + Vector3.up * .14f);
                mesh.Face(steel, Vector3.left * side, c, c + Vector3.up * .14f, d + Vector3.up * .14f, d);
                Tube(mesh, steel, c + Vector3.up * .14f, d + Vector3.up * .14f, .015f, lod);
            }
        }
        // Four roof panels have an actual pitched silhouette, not a flat blocking cube.
        var apex = new Vector3(0, 2.93f, .2f);
        var eaves = new[] { new Vector3(-.85f, 2.40f, -.59f), new Vector3(.85f, 2.40f, -.59f), new Vector3(.85f, 2.40f, .99f), new Vector3(-.85f, 2.40f, .99f) };
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = eaves[i], b = eaves[(i + 1) % 4];
            Vector3 n = Vector3.Cross(b - a, apex - a).normalized;
            if (n.y < 0) n = -n;
            mesh.Face(i % 2 == 0 ? red : yellow, n, a, b, apex);
            mesh.Face(wood, -n, a - Vector3.up * .02f, apex - Vector3.up * .02f, b - Vector3.up * .02f);
        }
    }

    private static void Climber(EnvironmentMesh mesh, int lod)
    {
        // Four steel posts, a horizontal monkey ladder and a vertical rope climbing net.
        foreach (float x in new[] { -.95f, .95f })
            foreach (float z in new[] { -.95f, .95f })
            {
                Tube(mesh, blue, new Vector3(x, .04f, z), new Vector3(x, 2.0f, z), .047f, lod);
                mesh.Cylinder(yellow, new Vector3(x, 1.98f, z), new Vector3(x, 2.04f, z), .054f, .043f, lod == 0 ? 12 : 6);
            }
        foreach (float x in new[] { -.95f, .95f })
            Tube(mesh, red, new Vector3(x, 1.95f, -.95f), new Vector3(x, 1.95f, .95f), .036f, lod);
        for (float z = -.9f; z < 1; z += .3f)
            Tube(mesh, yellow, new Vector3(-.95f, 1.95f, z), new Vector3(.95f, 1.95f, z), .025f, lod);
        foreach (float y in new[] { .35f, .65f, .95f, 1.25f, 1.55f, 1.85f })
            Tube(mesh, yellow, new Vector3(-.95f, y, -.95f), new Vector3(.95f, y, -.95f), .023f, lod);
        for (float x = -.9f; x < 1; x += .3f)
            Tube(mesh, rubber, new Vector3(x, .20f, .95f), new Vector3(x, 1.95f, .95f), .012f, lod);
        for (float y = .2f; y < 2; y += .29f)
        {
            Tube(mesh, rubber, new Vector3(-.95f, y, .95f), new Vector3(.95f, y, .95f), .012f, lod);
            if (lod == 0)
                for (float x = -.9f; x < 1; x += .3f)
                    mesh.Cylinder(red, new Vector3(x, y, .93f), new Vector3(x, y, .975f), .025f, .025f, 6);
        }
    }

    private static void Sandpit(EnvironmentMesh mesh, int lod)
    {
        foreach (int side in new[] { -1, 1 })
        {
            mesh.RoundedBox(wood, new Vector3(side * 1.23f, .18f, 0), new Vector3(.095f, .30f, 2.55f), .025f);
            mesh.RoundedBox(wood, new Vector3(0, .18f, side * 1.23f), new Vector3(2.38f, .30f, .095f), .025f);
            mesh.RoundedBox(yellow, new Vector3(side * 1.22f, .355f, 0), new Vector3(.30f, .065f, 2.64f), .045f);
            mesh.RoundedBox(yellow, new Vector3(0, .355f, side * 1.22f), new Vector3(2.12f, .065f, .30f), .045f);
            foreach (float x in new[] { -.95f, .95f }) Bolt(mesh, new Vector3(x, .24f, side * 1.284f), lod);
        }
        // Small surface undulations stay inside the rim; no perfectly flat top slab.
        const int cells = 10;
        Vector3 P(int x, int z) => new Vector3(-1.17f + 2.34f * x / cells,
            .105f + .018f * Mathf.Sin(x * 1.76f + z * .4f) * Mathf.Sin(z * 1.35f), -1.17f + 2.34f * z / cells);
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++) mesh.Face(sand, Vector3.up, P(x, z), P(x, z + 1), P(x + 1, z + 1), P(x + 1, z));
    }

    private static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;
    private static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c) =>
        Cross(b - a, p - a) >= -.00001f && Cross(c - b, p - b) >= -.00001f && Cross(a - c, p - c) >= -.00001f;
    private static bool Inside(Vector3 p, Vector3[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            if ((polygon[i].z > p.z) != (polygon[j].z > p.z) &&
                p.x < (polygon[j].x - polygon[i].x) * (p.z - polygon[i].z) / (polygon[j].z - polygon[i].z) + polygon[i].x)
                inside = !inside;
        return inside;
    }
    private static float EdgeDistance(Vector3 p, Vector3[] polygon)
    {
        float distance = float.MaxValue;
        for (int i = 0; i < polygon.Length; i++) distance = Mathf.Min(distance, SegmentDistance(p, polygon[i], polygon[(i + 1) % polygon.Length]));
        return distance;
    }
    private static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        return Vector3.Distance(p, a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(.00001f, d.sqrMagnitude)));
    }
}
