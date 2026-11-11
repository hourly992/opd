using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Geographic extent is evidence-based. Poisson-like stem locations are a reconstruction inside it.
internal static class GeographicLandscapeBuilder
{
    [Serializable] private sealed class Evidence { public string sourceSha256; public Area[] areas; }
    [Serializable] private sealed class Area { public long id; public string kind, source; public StreetBuilder.Point[] points; public float areaSquareMeters; }

    private sealed class StemSites
    {
        private const float Cell = 4;
        private readonly Dictionary<Vector2Int, List<(Vector3 point, float radius)>> cells = new Dictionary<Vector2Int, List<(Vector3, float)>>();
        private static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
        internal void Add(Vector3 p, float radius)
        {
            var key = Key(p);
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<(Vector3, float)>();
            list.Add((p, radius));
        }
        internal bool Clear(Vector3 p, float radius, bool fixedDistance = false)
        {
            var key = Key(p);
            int range = Mathf.CeilToInt((radius + (fixedDistance ? 0 : 2.6f)) / Cell);
            for (int x = -range; x <= range; x++)
                for (int z = -range; z <= range; z++)
                    if (cells.TryGetValue(key + new Vector2Int(x, z), out var list))
                        foreach (var site in list)
                        {
                            float d = radius + (fixedDistance ? 0 : site.radius);
                            float dx = p.x - site.point.x, dz = p.z - site.point.z;
                            if (dx * dx + dz * dz < d * d) return false;
                        }
            return true;
        }
    }

    internal static void Generate(Transform parent)
    {
        var evidence = JsonUtility.FromJson<Evidence>(File.ReadAllText("Assets/StreetSource/vegetation_evidence.json"));
        var root = new GameObject("09 · Лесные участки · исходные границы OSM");
        root.transform.SetParent(parent, false);
        var saved = root.AddComponent<GeographicLandscape>(); saved.sourceSha256 = evidence.sourceSha256;
        var patches = new List<GeographicLandscape.Patch>();
        var stems = new StemSites(); var understoreySites = new StemSites();
        foreach (var collider in UnityEngine.Object.FindObjectsByType<CapsuleCollider>()) stems.Add(collider.transform.position, 2.6f);
        // Residential approaches/playground surfaces are built before the forest. Their actual
        // colliders also exclude planting, including paths not present in the original OSM lines.
        Physics.SyncTransforms();
        var surfaceColliders = new HashSet<Collider>(UnityEngine.Object.FindObjectsByType<Collider>()
            .Where(c => c.enabled && !c.isTrigger && !(c is CapsuleCollider) && !(c is CharacterController)
                && !(c is TerrainCollider) && c.name != "Grass"));
        var surfaceHits = new RaycastHit[64];
        bool CanPlant(Vector3 p, float clearance)
        {
            if (!StreetBuilder.CanPlant(p, clearance)) return false;
            // Centre plus eight points check the occupied footprint, not only the stem. Non-convex
            // MeshCollider.Raycast is supported; ClosestPoint is deliberately not used on road meshes.
            for (int sample = 0; sample < 9; sample++)
            {
                float a = (sample - 1) * Mathf.PI * .25f;
                var probe = sample == 0 ? p : p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * clearance;
                int count = Physics.RaycastNonAlloc(probe + Vector3.up * 12, Vector3.down, surfaceHits, 14, ~0, QueryTriggerInteraction.Ignore);
                if (count == surfaceHits.Length) return false; // A crowded surface is not a safe planting gap.
                for (int i = 0; i < count; i++)
                    if (surfaceHits[i].point.y >= -.08f && surfaceColliders.Contains(surfaceHits[i].collider)) return false;
            }
            return true;
        }
        var floor = EnvironmentMesh.Material("WoodlandFloor", Color.white);
        floor.shader = Shader.Find("Street/WoodlandGround");
        floor.SetTexture("_MainTex", StreetSurfaceLibrary.Texture("Graphics2K/leafy_grass", "Albedo"));
        floor.SetTexture("_GrassTex", StreetSurfaceLibrary.Texture("Graphics2K/grass_ground", "Albedo"));
        floor.SetColor("_GrassTint", AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Grass.mat").GetColor("_Color"));
        floor.SetTexture("_BumpMap", StreetSurfaceLibrary.Texture("Graphics2K/leafy_grass", "Normal"));
        floor.enableInstancing = true; EditorUtility.SetDirty(floor);
        int total = 0, bushes = 0, youngTotal = 0, canopyTotal = 0, emergentTotal = 0;
        foreach (var area in evidence.areas.Where(a => a.kind == "wood"))
        {
            var boundary = area.points.Select(p => p.V).ToArray();
            if ((boundary[0] - boundary[boundary.Length - 1]).sqrMagnitude < .0001f) boundary = boundary.Take(boundary.Length - 1).ToArray();
            var patchRoot = new GameObject("Лес OSM " + area.id).transform; patchRoot.SetParent(root.transform, false);
            var random = new System.Random(unchecked((int)area.id));
            float minX = boundary.Min(p => p.x), maxX = boundary.Max(p => p.x), minZ = boundary.Min(p => p.z), maxZ = boundary.Max(p => p.z);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
            float Edge(Vector3 p) => boundary.Select((a, i) => {
                var d = boundary[(i + 1) % boundary.Length] - a;
                return Vector3.Distance(p, a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(.001f, d.sqrMagnitude)));
            }).Min();
            var trees = new List<Transform>(); var shrubs = new List<Transform>();
            int target = Mathf.Clamp(Mathf.RoundToInt(area.areaSquareMeters / 18), 10, 1250);
            // No grid and no repeated roadside sequence. Existing buildings and all road widths remain exclusions.
            for (int attempt = 0; attempt < target * 65 && trees.Count < target; attempt++)
            {
                var p = new Vector3(Range(minX, maxX), 0, Range(minZ, maxZ));
                if (!GeographicLandscape.Contains(boundary, p)) continue;
                float edge = Edge(p);
                if (edge < 1.05f) continue;
                float cluster = Mathf.PerlinNoise(p.x * .026f + (area.id % 281) * .13f, p.z * .026f + 9.7f);
                if (random.NextDouble() > Mathf.Lerp(.58f, .99f, cluster)) continue;
                bool young = edge < 2.7f || random.NextDouble() < .30 + (cluster - .5f) * .18f;
                bool emergent = !young && edge > 4 && random.NextDouble() < .20;
                float spacingRadius = young ? 1.14f : emergent ? 1.82f : 1.57f;
                if (!stems.Clear(p, spacingRadius) || !CanPlant(p, young ? .75f : 1.35f)) continue;
                // Species proportions are provisional regional art direction; OSM has no botanical inventory.
                bool conifer = random.NextDouble() < .28;
                int variant = random.Next(4);
                var prefab = conifer ? SummerVegetationBuilder.Spruce(variant) : SummerVegetationBuilder.SelectBroadleaf(9.5f, variant);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, patchRoot);
                instance.name = (young ? "Молодое" : emergent ? "Верхний ярус" : "Основной полог") + " · "
                    + (conifer ? "хвойное" : "лиственное") + " · лес " + area.id + " · " + trees.Count;
                instance.transform.SetLocalPositionAndRotation(p, Quaternion.Euler(0, Range(0, 360), 0));
                float edgeAge = Mathf.Lerp(.85f, 1, Mathf.Clamp01(edge / 5));
                float scale = (young ? Range(.40f, .69f) : emergent ? Range(1.22f, 1.49f) : Range(.86f, 1.22f)) * edgeAge;
                instance.transform.localScale = new Vector3(Range(.96f, 1.18f), 1, Range(.96f, 1.18f)) * scale;
                // Forest-only screen-space budgets: broadleaf is 99924 / 35991 / 13092 / 2511
                // triangles; conifer is 31596 / 7090 / 5934. Keep the original close meshes,
                // but stop paying their alpha-tested canopy cost across the whole dense stand.
                var lodGroup = instance.GetComponent<LODGroup>();
                if (lodGroup)
                {
                    var lods = lodGroup.GetLODs();
                    float[] thresholds = lods.Length == 4 ? new[] { .55f, .27f, .11f, .014f } : new[] { .36f, .12f, .014f };
                    for (int lod = 0; lod < lods.Length; lod++) lods[lod].screenRelativeTransitionHeight = thresholds[Mathf.Min(lod, thresholds.Length - 1)];
                    lodGroup.SetLODs(lods);
                }
                trees.Add(instance.transform); stems.Add(p, spacingRadius);
                if (young) youngTotal++; else if (emergent) emergentTotal++; else canopyTotal++;
            }
            // Understorey belongs to this same observed wood extent, never a random city-wide scatter.
            int shrubTarget = Mathf.RoundToInt(trees.Count * .80f);
            for (int attempt = 0; attempt < target * 30 && shrubs.Count < shrubTarget; attempt++)
            {
                var p = new Vector3(Range(minX, maxX), 0, Range(minZ, maxZ));
                if (!GeographicLandscape.Contains(boundary, p) || Edge(p) < .75f || !CanPlant(p, .65f)) continue;
                if (!stems.Clear(p, .85f, true) || !understoreySites.Clear(p, .87f)) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(SummerVegetationBuilder.Shrub(false, random.Next(4)), patchRoot);
                instance.transform.SetLocalPositionAndRotation(p, Quaternion.Euler(0, Range(0, 360), 0));
                float cluster = Mathf.PerlinNoise(p.x * .042f + 17, p.z * .042f + (area.id % 113) * .11f);
                instance.transform.localScale = Vector3.one * Range(.55f, 1.20f) * Mathf.Lerp(.85f, 1.12f, cluster);
                shrubs.Add(instance.transform); understoreySites.Add(p, .87f);
            }
            // Fine shallow litter layer follows the actual polygon. It stays below paved surfaces.
            var mesh = new EnvironmentMesh();
            void Fill(Vector3 a, Vector3 b, Vector3 c, int depth = 0)
            {
                float ab = (b - a).sqrMagnitude, bc = (c - b).sqrMagnitude, ca = (a - c).sqrMagnitude;
                if (depth >= 22 || Mathf.Max(ab, bc, ca) <= 9)
                {
                    mesh.Face(floor, Vector3.up, a + Vector3.up * .002f, b + Vector3.up * .002f, c + Vector3.up * .002f);
                    return;
                }
                // Longest-edge bisection also handles the very thin ears of the concave OSM polygons.
                // A uniform N*N subdivision needlessly created hundreds of thousands of tiny faces there.
                if (ab >= bc && ab >= ca) { var m = (a + b) * .5f; Fill(a, m, c, depth + 1); Fill(m, b, c, depth + 1); }
                else if (bc >= ca) { var m = (b + c) * .5f; Fill(a, b, m, depth + 1); Fill(a, m, c, depth + 1); }
                else { var m = (c + a) * .5f; Fill(a, b, m, depth + 1); Fill(m, b, c, depth + 1); }
            }
            foreach (var tri in StreetBuilder.Triangulate(boundary)) Fill(boundary[tri[0]], boundary[tri[1]], boundary[tri[2]]);
            var ground = mesh.Create("WoodlandFloor_" + area.id, "Лесная подстилка · OSM " + area.id);
            ground.transform.SetParent(patchRoot, false); ground.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var floorMesh = ground.GetComponent<MeshFilter>().sharedMesh;
            floorMesh.colors = floorMesh.vertices.Select(p => new Color(1, 1, 1, Mathf.SmoothStep(0, 1, Mathf.Clamp01(Edge(p) / 5)) * .8f)).ToArray();
            EditorUtility.SetDirty(floorMesh);
            patches.Add(new GeographicLandscape.Patch { osmId = area.id, source = area.source, boundary = boundary, trees = trees.ToArray(), understorey = shrubs.ToArray() });
            total += trees.Count; bushes += shrubs.Count;
        }
        saved.patches = patches.ToArray();
        Debug.Log("GEOGRAPHIC_LANDSCAPE woodlandPolygons=" + patches.Count + " trees=" + total + " understorey=" + bushes
            + " young=" + youngTotal + " canopy=" + canopyTotal + " emergent=" + emergentTotal);
    }
}
