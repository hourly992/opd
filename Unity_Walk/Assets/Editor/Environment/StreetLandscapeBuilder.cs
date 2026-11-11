using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Coverage follows the street, its courtyards and saved land-cover/playground extents.
// The cell grid partitions saved meshes only; it does not dictate visible planting patterns.
internal static class StreetLandscapeBuilder
{
    private const float CellSize = 24;
    [Serializable] private sealed class Evidence { public AreaRecord[] areas; }
    [Serializable] private sealed class AreaRecord { public long id; public string kind; public StreetBuilder.Point[] points; }
    private enum Kind { Corridor, Courtyard, Wood, Grass, PlaygroundBorder, Pilot }
    private sealed class Area
    {
        internal Kind kind;
        internal string label;
        internal Rect bounds;
        internal Vector3[] polygon;
        internal Vector3 a, b;
        internal float margin;
        internal int accepted;
        internal float Weight(Vector3 p, out float interior)
        {
            interior = 0;
            if (!bounds.Contains(new Vector2(p.x, p.z))) return 0;
            if (kind == Kind.Corridor)
            {
                Vector3 d = b - a;
                float distance = Vector3.Distance(p, a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(.001f, d.sqrMagnitude)));
                return Mathf.SmoothStep(0, 1, Mathf.Clamp01((margin - distance) / 3));
            }
            if (polygon == null)
            {
                float edge = Mathf.Min(Mathf.Min(p.x - bounds.xMin, bounds.xMax - p.x), Mathf.Min(p.z - bounds.yMin, bounds.yMax - p.z));
                return Mathf.SmoothStep(0, 1, Mathf.Clamp01(edge / 3));
            }
            bool inside = GeographicLandscape.Contains(polygon, p);
            float nearest = float.MaxValue;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector3 x = polygon[i], d = polygon[(i + 1) % polygon.Length] - x;
                nearest = Mathf.Min(nearest, Vector3.Distance(p, x + d * Mathf.Clamp01(Vector3.Dot(p - x, d) / Mathf.Max(.001f, d.sqrMagnitude))));
            }
            interior = inside ? nearest : -nearest;
            if (kind == Kind.PlaygroundBorder)
                return inside ? 0 : Mathf.SmoothStep(0, 1, Mathf.Clamp01((margin - nearest) / 2.5f));
            return inside ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01((margin - nearest) / 2));
        }
    }

    private sealed class VegetationMask
    {
        private const float Size = 8;
        private readonly Dictionary<Vector2Int, List<(Vector3 p, float hard, float shade)>> cells = new Dictionary<Vector2Int, List<(Vector3, float, float)>>();
        private static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Size), Mathf.FloorToInt(p.z / Size));
        internal void Add(Vector3 p, float hard, float shade)
        {
            var key = Key(p);
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<(Vector3, float, float)>();
            list.Add((p, hard, Mathf.Min(7.5f, shade)));
        }
        internal bool Evaluate(Vector3 p, out float light)
        {
            light = 1;
            var key = Key(p);
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    if (cells.TryGetValue(key + new Vector2Int(x, z), out var list))
                        foreach (var plant in list)
                        {
                            float dx = p.x - plant.p.x, dz = p.z - plant.p.z, distance = dx * dx + dz * dz;
                            if (distance < plant.hard * plant.hard) return false;
                            if (distance < plant.shade * plant.shade)
                                light = Mathf.Min(light, Mathf.Lerp(.42f, 1, Mathf.Sqrt(distance) / plant.shade));
                        }
            return true;
        }
    }

    private readonly struct Site
    {
        internal readonly Vector3 point;
        internal readonly float height, angle, shade;
        internal readonly bool woodland;
        internal Site(Vector3 point, float height, float angle, float shade, bool woodland)
        { this.point = point; this.height = height; this.angle = angle; this.shade = shade; this.woodland = woodland; }
    }

    internal static void Generate(Transform parent)
    {
        var root = new GameObject("08 · Озеленение и травостой всей улицы").transform;
        root.SetParent(parent, false);
        Physics.SyncTransforms();
        var areas = new List<Area>();
        var cells = new Dictionary<Vector2Int, List<Area>>();
        void Add(Area area)
        {
            areas.Add(area);
            for (int x = Mathf.FloorToInt(area.bounds.xMin / CellSize); x <= Mathf.FloorToInt(area.bounds.xMax / CellSize); x++)
                for (int z = Mathf.FloorToInt(area.bounds.yMin / CellSize); z <= Mathf.FloorToInt(area.bounds.yMax / CellSize); z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<Area>();
                    list.Add(area);
                }
        }
        Rect Bounds(Vector3[] points, float margin) => Rect.MinMaxRect(points.Min(p => p.x) - margin,
            points.Min(p => p.z) - margin, points.Max(p => p.x) + margin, points.Max(p => p.z) + margin);
        var road = StreetBuilder.FocalRoadSamples(12, true).Select(s => s.position).ToArray();
        for (int i = 1; i < road.Length; i++)
            Add(new Area { kind = Kind.Corridor, a = road[i - 1], b = road[i], margin = 42, bounds = Bounds(new[] { road[i - 1], road[i] }, 42) });
        Add(new Area { kind = Kind.Pilot, label = "pilot5", bounds = new Rect(-48, -72, 96, 120) });
        var evidence = JsonUtility.FromJson<Evidence>(File.ReadAllText("Assets/StreetSource/vegetation_evidence.json"));
        foreach (var area in evidence.areas.Where(a => a.kind == "wood" || a.kind == "grass"))
        {
            var polygon = area.points.Select(p => p.V).ToArray();
            float margin = area.kind == "wood" ? 3 : 1.5f;
            Add(new Area { kind = area.kind == "wood" ? Kind.Wood : Kind.Grass, label = area.kind + area.id,
                polygon = polygon, margin = margin, bounds = Bounds(polygon, margin) });
        }
        var data = JsonUtility.FromJson<StreetBuilder.Data>(File.ReadAllText("Assets/StreetSource/street.json"));
        foreach (var building in data.buildings.Where(b => (b.street ?? "").Contains("Дунина")))
        {
            var polygon = building.points.Select(p => p.V).ToArray();
            // Full footprint envelope also includes the open centre of U-shaped courtyard buildings.
            Add(new Area { kind = Kind.Courtyard, label = building.address + "-" + building.id, bounds = Bounds(polygon, 22) });
        }
        foreach (var playground in UnityEngine.Object.FindObjectsByType<PlaygroundSite>())
            Add(new Area { kind = Kind.PlaygroundBorder, label = "playground" + playground.osmId,
                polygon = playground.footprint, margin = 10, bounds = Bounds(playground.footprint, 10) });
        var mask = new VegetationMask();
        var forest = UnityEngine.Object.FindAnyObjectByType<GeographicLandscape>();
        if (forest && forest.patches != null)
            foreach (var patch in forest.patches)
            {
                foreach (var tree in patch.trees)
                {
                    float size = Mathf.Max(tree.lossyScale.x, tree.lossyScale.z);
                    mask.Add(tree.position, .50f * Mathf.Max(.7f, size), 2.9f * size);
                }
                foreach (var shrub in patch.understorey)
                {
                    float size = Mathf.Max(shrub.lossyScale.x, shrub.lossyScale.z);
                    mask.Add(shrub.position, .85f * size + .12f, 1.5f * size);
                }
            }
        int chunks = 0, tufts = 0;
        foreach (var cell in cells.OrderBy(c => c.Key.x).ThenBy(c => c.Key.y))
        {
            int count = Grass(root, cell.Key, cell.Value, mask);
            if (count > 0) { chunks++; tufts += count; }
        }
        Debug.Log("STREET_LANDSCAPE grassChunks=" + chunks + " tufts=" + tufts
            + " woodlandAreas=" + areas.Count(a => a.kind == Kind.Wood) + " mappedGrassAreas=" + areas.Count(a => a.kind == Kind.Grass)
            + " courtyardAreas=" + areas.Count(a => a.kind == Kind.Courtyard) + " playgroundBorders=" + areas.Count(a => a.kind == Kind.PlaygroundBorder)
            + " roadsidePlanting=referenceOnly");
        foreach (var area in areas.Where(a => a.kind != Kind.Corridor && a.kind != Kind.Pilot))
            Debug.Log("GRASS_COVERAGE " + area.kind + " " + area.label + " tufts=" + area.accepted);
    }

    private static int Grass(Transform parent, Vector2Int cell, List<Area> areas, VegetationMask mask)
    {
        var random = new System.Random(cell.x * 73856093 ^ cell.y * 19349663 ^ 2957);
        var sites = new List<Site>();
        bool urban = areas.Any(a => a.kind == Kind.Corridor || a.kind == Kind.Courtyard || a.kind == Kind.Pilot || a.kind == Kind.PlaygroundBorder);
        int attempts = urban ? 2400 : 1900;
        for (int i = 0; i < attempts; i++)
        {
            var p = new Vector3((cell.x + (float)random.NextDouble()) * CellSize, 0, (cell.y + (float)random.NextDouble()) * CellSize);
            float coverage = 0, forestEdge = -999;
            bool woodland = false;
            foreach (var area in areas)
            {
                float weight = area.Weight(p, out float edge);
                coverage = Mathf.Max(coverage, weight);
                if (area.kind == Kind.Wood && edge >= 0 && weight > 0) { woodland = true; forestEdge = Mathf.Max(forestEdge, edge); }
            }
            if (coverage <= 0 || !mask.Evaluate(p, out float light)) continue;
            float cluster = Mathf.PerlinNoise(p.x * .11f + 23.7f, p.z * .11f + 61.1f);
            float density = woodland ? Mathf.Lerp(.90f, .48f, Mathf.Clamp01(forestEdge / 5)) * Mathf.Lerp(.45f, 1, cluster) * light
                : Mathf.Lerp(.73f, 1, cluster);
            if (random.NextDouble() > density * coverage) continue;
            if (!StreetBuilder.CanPlant(p, .18f)) continue;
            if (!Physics.Raycast(p + Vector3.up * 6, Vector3.down, out var hit, 6.2f, ~0, QueryTriggerInteraction.Ignore) || hit.collider.name != "Grass") continue;
            // Exact surface collider check rejects residential connectors, playgrounds and yard paving.
            p.y = hit.point.y + .002f;
            float border = Mathf.Lerp(.72f, 1, coverage);
            float height = Mathf.Lerp(.10f, woodland ? .23f : .20f, cluster) * border * (.82f + (float)random.NextDouble() * .35f);
            float angle = (float)random.NextDouble() * Mathf.PI * 2;
            float shade = Mathf.Lerp(.82f, 1.02f, Mathf.PerlinNoise(p.x * .24f, p.z * .24f));
            sites.Add(new Site(p, height, angle, shade, woodland));
            foreach (var area in areas)
                if (area.kind != Kind.Corridor && area.Weight(p, out _) > 0) area.accepted++;
        }
        if (sites.Count < 12) return 0;
        var root = new GameObject("Трава · ячейка " + cell.x + ", " + cell.y);
        root.transform.SetParent(parent, false);
        Vector3 origin = new Vector3((cell.x + .5f) * CellSize, 0, (cell.y + .5f) * CellSize);
        root.transform.localPosition = origin;
        var material = StreetSurfaceLibrary.GrassBlades();
        var clover = StreetSurfaceLibrary.Foliage("MeadowClover", "tree_small_02", new Color(.57f, .74f, .36f));
        clover.SetFloat("_WindStrength", .018f);
        clover.SetFloat("_WindStemHeight", .02f); clover.SetFloat("_WindHeightScale", 2);
        var petal = EnvironmentMesh.Material("MeadowPetal", new Color(.88f, .87f, .72f));
        var pollen = EnvironmentMesh.Material("MeadowPollen", new Color(.84f, .57f, .08f));
        var renderers = new Renderer[3];
        for (int lod = 0; lod < 3; lod++)
        {
            var mesh = new EnvironmentMesh();
            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                if (lod == 1 && i % 3 != 0 || lod == 2 && i % 8 != 0) continue;
                Vector3 point = site.point - origin;
                mesh.GrassTuft(material, point, site.height, site.angle, new Color(site.shade * .91f, site.shade, site.shade * .83f, 1));
                if (lod == 0 && !site.woodland && i % 13 == 0)
                    for (int leaf = 0; leaf < 3; leaf++)
                    {
                        float a = site.angle * Mathf.Rad2Deg + leaf * 120;
                        Vector3 offset = Quaternion.Euler(0, a, 0) * Vector3.forward * .036f;
                        mesh.FoliageCard(clover, point + offset + Vector3.up * .06f,
                            Quaternion.Euler(78, a, 0), new Vector2(.075f, .095f), Vector3.up, new Color(.9f, 1, .86f, .9f));
                    }
                if (lod == 0 && !site.woodland && i % 113 == 0)
                {
                    Vector3 center = point + Vector3.up * (site.height * .8f);
                    for (int flower = 0; flower < 6; flower++)
                    {
                        float a = flower * Mathf.PI / 3;
                        Vector3 along = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .03f;
                        Vector3 side = new Vector3(-along.z, .003f, along.x) * .32f;
                        mesh.Face(petal, Vector3.up, center, center + along + side, center + along * 1.35f, center + along - side);
                    }
                    mesh.Cylinder(pollen, center - Vector3.up * .002f, center + Vector3.up * .004f, .009f, .008f, 8);
                }
            }
            var part = mesh.Create("StreetGrass_" + cell.x + "_" + cell.y + "_LOD" + lod, "LOD " + lod);
            part.transform.SetParent(root.transform, false);
            renderers[lod] = part.GetComponent<Renderer>(); renderers[lod].shadowCastingMode = ShadowCastingMode.Off;
        }
        var group = root.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(.65f, new[] { renderers[0] }), new LOD(.30f, new[] { renderers[1] }), new LOD(.13f, new[] { renderers[2] }) });
        group.localReferencePoint = Vector3.up * .15f; group.size = CellSize;
        return sites.Count;
    }
}