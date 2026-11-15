using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

internal static class DistrictLeafCarpets
{
    private static string AssetRoot => EditorSceneManager.GetActiveScene().path == "Assets/Scenes/Street.unity"
        ? "Assets/Art/FullVisual/Leaves" : "Assets/PhotoReconstruction/AutumnCourtyardCarpets";
    private const string GroupName = "Осень · ковры листьев во дворах";
    private const string ReportPath = "street_qa/autumn_courtyard_carpets.json";
    private const float CellSize = 9f;
    private static readonly Color[] Palette =
    {
        new Color(.72f, .40f, .065f), new Color(.68f, .23f, .035f),
        new Color(.55f, .10f, .024f), new Color(.42f, .07f, .020f),
        new Color(.78f, .49f, .12f), new Color(.38f, .18f, .045f),
        new Color(.45f, .08f, .025f), new Color(.55f, .28f, .065f),
    };
    private static readonly Vector2[][] LeafOutlines =
    {
        new[]
        {
            new Vector2(0, -.57f), new Vector2(-.23f, -.31f), new Vector2(-.35f, -.09f),
            new Vector2(-.28f, .17f), new Vector2(-.16f, .34f), new Vector2(.03f, .53f),
            new Vector2(.17f, .29f), new Vector2(.28f, .10f), new Vector2(.31f, -.12f),
            new Vector2(.20f, -.34f),
        },
        new[]
        {
            new Vector2(0, -.58f), new Vector2(-.31f, -.37f), new Vector2(-.50f, -.11f),
            new Vector2(-.28f, -.12f), new Vector2(-.47f, .28f), new Vector2(-.17f, .14f),
            new Vector2(.02f, .57f), new Vector2(.19f, .13f), new Vector2(.48f, .28f),
            new Vector2(.28f, -.12f), new Vector2(.48f, -.10f), new Vector2(.28f, -.36f),
        },
        new[]
        {
            new Vector2(0, -.56f), new Vector2(-.28f, -.33f), new Vector2(-.43f, -.10f),
            new Vector2(-.40f, .15f), new Vector2(-.19f, .38f), new Vector2(-.02f, .52f),
            new Vector2(.22f, .32f), new Vector2(.39f, .11f), new Vector2(.39f, -.15f),
            new Vector2(.24f, -.34f),
        },
    };
    private static readonly Vector2[] FarOutline =
    {
        new Vector2(0, -.55f), new Vector2(-.40f, -.06f),
        new Vector2(0, .52f), new Vector2(.39f, -.06f),
    };

    [Serializable]
    private sealed class Patch
    {
        public string sector;
        public Vector2 center, axis, radii;
        public int attempts, leaves;
    }

    [Serializable]
    private sealed class Report
    {
        public int leaves, cells, nearTriangles, farTriangles;
        public Patch[] patches;
    }

    private sealed class Batch
    {
        internal readonly LeafMesh near = new LeafMesh();
        internal readonly LeafMesh far = new LeafMesh();
        internal Vector3 origin;
    }

    private sealed class LeafMesh
    {
        internal readonly List<Vector3> vertices = new List<Vector3>();
        internal readonly List<Vector2> uv = new List<Vector2>();
        internal readonly List<Color32> colors = new List<Color32>();
        internal readonly List<int> triangles = new List<int>();

        internal Mesh Save(string name)
        {
            string path = $"{AssetRoot}/Meshes/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool existing = asset != null;
            if (!existing) asset = new Mesh { name = name };
            asset.Clear();
            asset.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            asset.SetVertices(vertices);
            asset.SetUVs(0, uv);
            asset.SetColors(colors);
            asset.SetTriangles(triangles, 0);
            asset.RecalculateNormals();
            if (existing) EditorUtility.SetDirty(asset);
            else AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }

    private sealed class Ground
    {
        private readonly HashSet<MeshCollider> paved;
        private readonly HashSet<MeshCollider> playgrounds;
        private readonly Vector3[][] parking;
        private readonly Dictionary<MeshCollider, (Material[] materials, int[] ends)> cache =
            new Dictionary<MeshCollider, (Material[], int[])>();

        internal Ground(ResidentialAccess access)
        {
            paved = new HashSet<MeshCollider>(access.surfaces);
            var street = UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
            if (street) paved.UnionWith(street.surfaces);
            playgrounds = UnityEngine.Object.FindObjectsByType<PlaygroundSite>().Select(p => p.ground).ToHashSet();
            parking = UnityEngine.Object.FindObjectsByType<ParkingSite>().Select(p => p.boundary)
                .Where(boundary => boundary != null && boundary.Length >= 3).ToArray();
        }

        internal bool TrySample(Vector2 point, out RaycastHit hit)
        {
            if (!Physics.Raycast(new Vector3(point.x, 2.15f, point.y), Vector3.down, out hit, 2.78f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                hit.collider is not MeshCollider surface || !surface.sharedMesh ||
                hit.point.y > .34f || hit.normal.y < .81f || playgrounds.Contains(surface) ||
                surface.GetComponentInParent<StreetBuilding>())
                return false;

            var material = MaterialAt(surface, hit.triangleIndex);
            string name = material ? material.name : surface.name;
            if (name.StartsWith("ReferenceAsphalt", StringComparison.Ordinal) ||
                name.StartsWith("MainRoadAsphalt", StringComparison.Ordinal) ||
                name.Contains("RoadPaint", StringComparison.OrdinalIgnoreCase))
                return false;
            if (parking.Any(boundary => IsParkingInterior(boundary, point))) return false;
            if (paved.Contains(surface) || surface.name == "Grass") return true;
            return name.Contains("Grass", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Lawn", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Mulch", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Verge", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Pav", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Footpath", StringComparison.OrdinalIgnoreCase);
        }

        private Material MaterialAt(MeshCollider collider, int triangle)
        {
            if (!cache.TryGetValue(collider, out var ranges))
            {
                var renderer = collider.GetComponent<MeshRenderer>();
                if (!renderer || renderer.GetComponent<MeshFilter>().sharedMesh != collider.sharedMesh)
                    ranges = (Array.Empty<Material>(), Array.Empty<int>());
                else
                {
                    var ends = new int[collider.sharedMesh.subMeshCount];
                    int end = 0;
                    for (int i = 0; i < ends.Length; i++)
                    {
                        end += (int)collider.sharedMesh.GetIndexCount(i) / 3;
                        ends[i] = end;
                    }
                    ranges = (renderer.sharedMaterials, ends);
                }
                cache.Add(collider, ranges);
            }
            for (int i = 0; i < ranges.ends.Length && i < ranges.materials.Length; i++)
                if (triangle >= 0 && triangle < ranges.ends[i]) return ranges.materials[i];
            return null;
        }

        private static bool IsParkingInterior(Vector3[] boundary, Vector2 point)
        {
            bool inside = false;
            float edgeDistance = float.PositiveInfinity;
            for (int i = 0; i < boundary.Length; i++)
            {
                Vector2 a = XZ(boundary[i]), b = XZ(boundary[(i + 1) % boundary.Length]);
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < a.x + (b.x - a.x) * (point.y - a.y) / (b.y - a.y))
                    inside = !inside;
                Vector2 edge = b - a;
                float along = Mathf.Clamp01(Vector2.Dot(point - a, edge) / Mathf.Max(.0001f, edge.sqrMagnitude));
                edgeDistance = Mathf.Min(edgeDistance, (point - a - edge * along).sqrMagnitude);
            }
            return inside && edgeDistance > .55f * .55f;
        }
    }

    internal static void Apply(bool wholeDistrict = false)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != (wholeDistrict ? "Assets/Scenes/Street.unity" : LeafCourtyardStudy.ScenePath))
            throw new InvalidOperationException("Open the saved Street scene before adding leaf carpets.");
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/PhotoReconstruction/Stage08Autumn/Materials/FallenLeaves.mat");
        if (!access || !material)
            throw new InvalidOperationException("The saved access network and autumn leaf material are required.");

        var random = new Random(151110);
        var batches = new Dictionary<Vector2Int, Batch>();
        var occupancy = new Dictionary<Vector2Int, int>();
        var patches = MakePatches().Concat(wholeDistrict ? DistrictPatches(access) : Enumerable.Empty<Patch>()).ToArray();
        Physics.SyncTransforms();
        var ground = new Ground(access);
        foreach (var patch in patches)
            Scatter(patch, random, ground, batches, occupancy);

        var report = new Report
        {
            leaves = patches.Sum(p => p.leaves), cells = batches.Count, patches = patches,
            nearTriangles = batches.Values.Sum(batch => batch.near.triangles.Count / 3),
            farTriangles = batches.Values.Sum(batch => batch.far.triangles.Count / 3),
        };
        if (report.leaves < 1900 || report.leaves > 30000)
            throw new InvalidOperationException($"Unexpected total of supported leaf placements: {report.leaves}.");

        Directory.CreateDirectory(AssetRoot + "/Meshes");
        AssetDatabase.Refresh();
        var old = GameObject.Find(GroupName);
        if (old) UnityEngine.Object.DestroyImmediate(old);
        var root = new GameObject(GroupName).transform;
        foreach (var pair in batches.OrderBy(pair => pair.Key.x).ThenBy(pair => pair.Key.y))
        {
            string name = $"AutumnCarpet_{pair.Key.x}_{pair.Key.y}";
            var batch = pair.Value;
            var cell = new GameObject(name);
            cell.transform.SetParent(root, false);
            cell.transform.localPosition = batch.origin;
            var near = MakeRenderer(cell.transform, "Ближняя листва", batch.near.Save(name + "_Near"), material);
            var far = MakeRenderer(cell.transform, "Дальняя листва", batch.far.Save(name + "_Far"), material);
            var lod = cell.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.16f, new Renderer[] { near }), new LOD(.048f, new Renderer[] { far }) });
            lod.RecalculateBounds();
        }
        Directory.CreateDirectory("street_qa");
        File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"DISTRICT_LEAF_CARPETS leaves={report.leaves} cells={report.cells} patches={patches.Count(p => p.leaves > 0)} " +
                  $"nearTriangles={report.nearTriangles} farTriangles={report.farTriangles}");
    }

    private static IEnumerable<Patch> MakePatches()
    {
        // Low drifts gather around the existing walks in the single study court.
        foreach (var item in new[]
        {
            ("court7", 27.3f, 36.05f, 2.80f, 1.70f, 14f, 1050),
            ("court7", 31.25f, 36.32f, 2.95f, 1.26f, 7f, 880),
            ("court7", 32.8f, 40.2f, 3.20f, 1.36f, 11f, 910),
            ("court7", 40.75f, 35.85f, 2.36f, 1.18f, -6f, 735),
            ("court7", 45.75f, 41.35f, 2.72f, 1.26f, 19f, 810),
            ("court7", 51.95f, 36.4f, 3.42f, 1.44f, 11f, 880),
            ("court7", 56.2f, 39.82f, 2.28f, .96f, 11f, 685),
        })
        {
            float angle = item.Item6 * Mathf.Deg2Rad;
            yield return new Patch
            {
                sector = item.Item1, center = new Vector2(item.Item2, item.Item3),
                axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                radii = new Vector2(item.Item4, item.Item5), attempts = item.Item7,
            };
        }

    }

    private static IEnumerable<Patch> DistrictPatches(ResidentialAccess access)
    {
        foreach (var anchor in new[]
        {
            new Vector2(-25, 15), new Vector2(-22, 10), new Vector2(-10, -15),
            new Vector2(8, 15), new Vector2(22, -12), new Vector2(15, -39)
        })
            yield return new Patch { sector = "house5", center = anchor, axis = Vector2.right,
                radii = new Vector2(2.4f, 1.25f), attempts = 790 };

        foreach (var (sector, endpoint) in new[]
        {
            ("north", new Vector2(-103.582f, 156.452f)),
            ("west", new Vector2(-46.623f, -47.697f)),
            ("south", new Vector2(-18.790f, -104.057f)),
            ("south", new Vector2(16.102f, -174.723f)),
            ("south", new Vector2(72.810f, -107.209f)),
            ("east", new Vector2(125.454f, 5.869f)),
            ("east", new Vector2(166.854f, 98.192f)),
            ("east", new Vector2(178.355f, 32.318f))
        })
        {
            var route = access.routes.FirstOrDefault(r => r.points.Length > 1 &&
                (XZ(r.points[0]) - endpoint).sqrMagnitude < .12f * .12f);
            if (route == null) continue;
            float walked = 0, next = 2.5f;
            int drifts = 0;
            for (int i = 1; i < route.points.Length && drifts < 3; i++)
            {
                Vector2 a = XZ(route.points[i - 1]), b = XZ(route.points[i]);
                float length = Vector2.Distance(a, b);
                if (length < .01f) continue;
                Vector2 axis = (b - a) / length;
                Vector2 across = new Vector2(-axis.y, axis.x);
                for (; next <= walked + length && drifts < 3; next += 3.7f, drifts++)
                    yield return new Patch
                    {
                        sector = sector, center = a + axis * (next - walked) + across * (drifts == 1 ? -1.03f : 1.03f),
                        axis = axis, radii = new Vector2(1.8f, .95f), attempts = 540
                    };
                walked += length;
            }
        }
    }

    private static void Scatter(Patch patch, Random random, Ground ground,
        Dictionary<Vector2Int, Batch> batches, Dictionary<Vector2Int, int> occupancy)
    {
        Vector2 across = new Vector2(-patch.axis.y, patch.axis.x);
        float phase = Next(random) * Mathf.PI * 2f;
        for (int i = 0; i < patch.attempts; i++)
        {
            float angle = Next(random) * Mathf.PI * 2f;
            float radius = Mathf.Pow(Next(random), .60f);
            float outline = .94f + .14f * Mathf.Sin(angle * 3f + phase) + .13f * Mathf.Sin(angle * 5f - phase);
            float spread = radius * outline;
            if (i % 11 == 0) spread *= 1.40f;
            Vector2 position = patch.center + (patch.axis * (Mathf.Cos(angle) * patch.radii.x) +
                across * (Mathf.Sin(angle) * patch.radii.y)) * spread;
            float noise = Mathf.PerlinNoise(position.x * 1.27f + 340f, position.y * 1.27f + 260f);
            float density = Mathf.Lerp(1, Mathf.Lerp(.12f, .86f, noise), Mathf.InverseLerp(.48f, 1f, radius));
            if (Next(random) > density || !ground.TrySample(position, out var hit)) continue;

            var key = new Vector2Int(Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.y / CellSize));
            if (!batches.TryGetValue(key, out var batch))
            {
                batch = new Batch { origin = new Vector3(key.x * CellSize, 0, key.y * CellSize) };
                batches.Add(key, batch);
            }
            var stack = new Vector2Int(Mathf.FloorToInt(position.x / .095f), Mathf.FloorToInt(position.y / .095f));
            occupancy.TryGetValue(stack, out int existing);
            occupancy[stack] = existing + 1;
            float height = .007f + Mathf.Min(existing, 3) * .007f + Next(random) * .003f;
            Vector3 center = hit.point - batch.origin + hit.normal * height;
            Vector3 axis = Quaternion.AngleAxis(360f * Next(random), hit.normal) *
                Vector3.ProjectOnPlane(Vector3.forward, hit.normal).normalized;
            Vector3 width = Vector3.Cross(hit.normal, axis);
            float length = Mathf.Lerp(.11f, .225f, Next(random));
            float broadness = Mathf.Lerp(.89f, 1.12f, Next(random));
            Color tint = Palette[random.Next(Palette.Length)] * Mathf.Lerp(.80f, 1.06f, Next(random));
            float shape = Next(random);
            var outlineShape = LeafOutlines[shape < .47f ? 0 : shape < .79f ? 1 : 2];
            AddLeaf(batch.near, outlineShape, center, axis * length, width * (length * broadness), hit.normal, tint, true);
            AddLeaf(batch.far, FarOutline, center, axis * (length * .95f), width * (length * broadness * 1.06f), hit.normal, tint, false);
            patch.leaves++;
        }
    }

    private static void AddLeaf(LeafMesh mesh, Vector2[] outline, Vector3 center, Vector3 axis,
        Vector3 width, Vector3 normal, Color tint, bool detail)
    {
        AutumnLeafShape.Append(mesh.vertices, mesh.uv, mesh.colors, mesh.triangles,
            center, axis, width, normal, tint, mesh.vertices.Count / (detail ? 6 : 4) % 3, detail);
    }

    private static MeshRenderer MakeRenderer(Transform cell, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(cell, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return renderer;
    }

    private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
    private static float Next(Random random) => (float)random.NextDouble();
}
