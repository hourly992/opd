using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

internal static class PaintedDistrictLeaves
{
    private const string RootName = "Осень · листва улицы и дворов";
    private const string AssetRoot = "Assets/Art/PaintedDistrict/Leaves";
    private const float TileSize = 18f;
    private const float SampleHeight = 12f;
    private const float SampleDistance = 16f;
    private const int Layers = Physics.DefaultRaycastLayers;
    private const int LeafLimit = 38000;
    private const int CarpetLimit = 6800;
    private const int KerbLimit = 6700;
    private const int CanopyLimit = 8600;
    private const int WallLimit = 2500;
    private const int FieldLimit = 4400;
    private static readonly float[] Sides = { -1f, 1f };
    private static readonly Color[] Palette =
    {
        new Color(.79f, .37f, .023f), new Color(.86f, .53f, .046f),
        new Color(.75f, .24f, .019f), new Color(.64f, .15f, .024f),
        new Color(.54f, .091f, .017f), new Color(.72f, .32f, .036f),
        new Color(.43f, .15f, .027f), new Color(.63f, .41f, .037f)
    };
    private static readonly Vector2[][] Outlines =
    {
        new[] { new Vector2(0, -.53f), new Vector2(-.27f, -.24f), new Vector2(-.38f, .07f),
            new Vector2(0, .49f), new Vector2(.32f, .10f), new Vector2(.25f, -.20f) },
        new[] { new Vector2(0, -.53f), new Vector2(-.41f, -.17f), new Vector2(-.32f, .20f),
            new Vector2(.07f, .50f), new Vector2(.40f, .19f), new Vector2(.29f, -.23f) },
        new[] { new Vector2(0, -.52f), new Vector2(-.43f, -.04f), new Vector2(-.23f, .12f),
            new Vector2(0, .51f), new Vector2(.22f, .17f), new Vector2(.46f, -.04f) }
    };
    private static readonly Vector2[] FarOutline =
    {
        new Vector2(0, -.52f), new Vector2(-.38f, -.03f),
        new Vector2(.02f, .50f), new Vector2(.34f, .02f)
    };

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before adding district leaves.");
        var access = Object.FindAnyObjectByType<ResidentialAccess>();
        var road = Object.FindAnyObjectByType<StreetRoadNetwork>();
        var material = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/PhotoReconstruction/Stage08Autumn/Materials/FallenLeaves.mat");
        if (!access || !road || !material)
            throw new InvalidOperationException("District leaves require saved access, road and leaf material.");

        Physics.SyncTransforms();
        var scatter = new Scatter(access, road);
        scatter.OnPaving();
        scatter.AlongKerbs();
        scatter.UnderTrees();
        scatter.AlongApproaches();
        scatter.BetweenDrifts();
        scatter.AgainstWalls();
        if (scatter.Count < 12000 || scatter.Count > LeafLimit || scatter.FieldLeaves < 4000 ||
            scatter.CarpetLeaves < 4000 ||
            scatter.Approaches < access.routes.Length * .6f)
            throw new InvalidOperationException($"Unexpected leaf coverage: {scatter.Count} leaves, " +
                $"{scatter.Approaches} approaches / {access.routes.Length}, {scatter.FieldLeaves} scattered, " +
                $"{scatter.CarpetLeaves} paving drifts.");

        EnsureAssetFolder();
        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);
        var carpets = GameObject.Find("Осень · ковры листьев во дворах");
        if (carpets) Object.DestroyImmediate(carpets);
        var earlierLitter = GameObject.Find("Осень · опавшая листва");
        if (earlierLitter)
            foreach (var lod in earlierLitter.GetComponentsInChildren<LODGroup>())
                if (lod.name.StartsWith("Leaves_", StringComparison.Ordinal))
                    Object.DestroyImmediate(lod.gameObject);
        var root = new GameObject(RootName).transform;
        foreach (var (key, tile) in scatter.Tiles.OrderBy(pair => pair.Key.x).ThenBy(pair => pair.Key.y))
        {
            string name = $"Leaves_{key.x}_{key.y}";
            var chunk = new GameObject(name);
            chunk.transform.SetParent(root, false);
            chunk.transform.localPosition = tile.Origin;
            var near = MakeRenderer(chunk.transform, "Вблизи", tile.Near.Save(name + "_Near"), material);
            var levels = new List<LOD> { new LOD(.24f, new Renderer[] { near }) };
            if (tile.Far.Count > 0)
            {
                var far = MakeRenderer(chunk.transform, "Вдали", tile.Far.Save(name + "_Far"), material);
                levels.Add(new LOD(.075f, new Renderer[] { far }));
            }
            var lod = chunk.AddComponent<LODGroup>();
            lod.SetLODs(levels.ToArray());
            lod.RecalculateBounds();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"PAINTED_DISTRICT_LEAVES leaves={scatter.Count} tiles={scatter.Tiles.Count} " +
            $"approaches={scatter.Approaches}/{access.routes.Length} curb={scatter.KerbLeaves} " +
            $"canopy={scatter.CanopyLeaves} walls={scatter.WallLeaves} scattered={scatter.FieldLeaves} " +
            $"pavingDrifts={scatter.CarpetLeaves} " +
            $"nearTriangles={scatter.Count * 4} farTriangles={scatter.Tiles.Values.Sum(tile => tile.Far.Count * 2)}");
    }

    private sealed class Scatter
    {
        private readonly Random random = new Random(603452);
        private readonly ResidentialAccess access;
        private readonly MeshCollider[] roadSurfaces;
        private readonly Ground ground;
        private readonly Dictionary<Vector2Int, int> occupancy = new Dictionary<Vector2Int, int>();
        private readonly HashSet<Vector2Int> corridors = new HashSet<Vector2Int>();
        internal readonly Dictionary<Vector2Int, Tile> Tiles = new Dictionary<Vector2Int, Tile>();
        internal int Count { get; private set; }
        internal int Approaches { get; private set; }
        internal int CarpetLeaves { get; private set; }
        internal int KerbLeaves { get; private set; }
        internal int CanopyLeaves { get; private set; }
        internal int WallLeaves { get; private set; }
        internal int FieldLeaves { get; private set; }

        internal Scatter(ResidentialAccess access, StreetRoadNetwork road)
        {
            this.access = access;
            roadSurfaces = road.surfaces;
            ground = new Ground(access, road);
            foreach (var point in access.routes.SelectMany(route => route.points))
                corridors.Add(Cell(XZ(point), 12f));
            foreach (var point in road.walkingChecks)
                corridors.Add(Cell(XZ(point), 12f));
        }

        internal void OnPaving()
        {
            // Follow the paving around benches, entrance aprons and outdoor pockets where
            // the former scene already had intentional drifts. The physics surface decides
            // which parts of each outline can carry leaves.
            foreach (var (x, z, length, width, angle, attempts) in new[]
            {
                (-25f, 15f, 2.75f, 1.36f, 13f, 540),
                (-22f, 10f, 2.52f, 1.28f, -17f, 490),
                (-32f, 17.6f, 2.55f, .96f, 87f, 400),
                (-27f, 12.7f, 2.44f, .94f, 48f, 295),
                (27.3f, 36.05f, 2.80f, 1.56f, 14f, 485),
                (31.25f, 36.32f, 2.95f, 1.20f, 7f, 440),
                (32.8f, 40.2f, 3.20f, 1.36f, 11f, 420),
                (40.75f, 35.85f, 2.36f, 1.18f, -6f, 360),
                (45.75f, 41.35f, 2.72f, 1.26f, 19f, 330),
                (51.95f, 36.4f, 3.42f, 1.44f, 11f, 340),
                (56.2f, 39.82f, 2.28f, .96f, 11f, 285),
                (-110f, 157.5f, 2.77f, 1.32f, 22f, 320),
                (-44.5f, -49.5f, 2.50f, 1.22f, 28f, 320),
                (143.8f, -214.8f, 2.55f, 1.72f, 28f, 275),
                (145.9f, -222.5f, 2.62f, 1.45f, -55f, 235),
                (198f, 118.3f, 2.55f, 1.50f, 7f, 245),
                (208.2f, 118.6f, 2.70f, 1.42f, 55f, 220)
            })
            {
                float radians = angle * Mathf.Deg2Rad;
                Drift(new Vector2(x, z), new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)),
                    length, width, attempts);
            }

            var pockets = new Dictionary<Vector2Int, (Vector2 center, Vector2 axis)>();
            foreach (var route in access.routes)
            {
                for (int i = 1; i < route.points.Length; i++)
                {
                    Vector2 start = XZ(route.points[i - 1]), delta = XZ(route.points[i]) - start;
                    if (delta.sqrMagnitude < .03f) continue;
                    Vector2 center = start + delta * .5f, axis = delta.normalized;
                    Vector2 across = new Vector2(-axis.y, axis.x);
                    foreach (float side in Sides)
                    {
                        Vector2 shoulder = center + across * (side * 1.1f);
                        if (ground.TrySample(shoulder, out _))
                            pockets.TryAdd(Cell(shoulder, 10f), (shoulder, axis));
                    }
                }
            }
            foreach (var (center, axis) in pockets.Values.OrderBy(_ => random.Next()))
            {
                if (CarpetLeaves >= CarpetLimit) break;
                Drift(center, axis, Range(1.15f, 2.4f), Range(.46f, .90f), random.Next(67, 113));
            }
        }

        private void Drift(Vector2 center, Vector2 axis, float length, float width, int attempts)
        {
            Vector2 across = new Vector2(-axis.y, axis.x);
            float phase = Range(0, Mathf.PI * 2);
            for (int i = 0; i < attempts && CarpetLeaves < CarpetLimit; i++)
            {
                float angle = Range(0, Mathf.PI * 2);
                float radius = Mathf.Pow(Next(), .81f);
                float outline = .93f + .22f * Mathf.Sin(angle * 3 + phase) + .12f * Mathf.Cos(angle * 5 - phase);
                float spread = radius * outline * (i % 9 == 0 ? 1.48f : 1);
                Vector2 p = center + (axis * (Mathf.Cos(angle) * length) +
                    across * (Mathf.Sin(angle) * width)) * spread;
                if (Place(p, true, true)) CarpetLeaves++;
            }
        }

        internal void AlongKerbs()
        {
            var boundaries = Object.FindObjectsByType<MeshCollider>()
                .Where(collider => collider.sharedMesh && (roadSurfaces.Contains(collider) ||
                    collider.name.Contains("борд", StringComparison.OrdinalIgnoreCase) ||
                    collider.name.Contains("кром", StringComparison.OrdinalIgnoreCase) ||
                    HasKerb(collider.GetComponent<MeshRenderer>())))
                .OrderBy(collider => collider.name, StringComparer.Ordinal)
                .ThenBy(collider => collider.transform.position.x)
                .ThenBy(collider => collider.transform.position.z);
            var edgePockets = new Dictionary<Vector2Int, (Vector2 center, Vector2 axis)>();
            foreach (var collider in boundaries)
            {
                var mesh = collider.sharedMesh;
                if (!mesh.isReadable) continue;
                var vertices = mesh.vertices;
                var materials = collider.GetComponent<MeshRenderer>()?.sharedMaterials ?? Array.Empty<Material>();
                for (int sub = 0; sub < Mathf.Min(mesh.subMeshCount, materials.Length); sub++)
                {
                    if (!materials[sub] || !IsKerb(materials[sub].name)) continue;
                    var triangles = mesh.GetTriangles(sub);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 a = collider.transform.TransformPoint(vertices[triangles[i]]);
                        Vector3 b = collider.transform.TransformPoint(vertices[triangles[i + 1]]);
                        Vector3 c = collider.transform.TransformPoint(vertices[triangles[i + 2]]);
                        if (Mathf.Max(a.y, b.y, c.y) - Mathf.Min(a.y, b.y, c.y) < .028f ||
                            Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) > .62f) continue;
                        SaveEdge(a, b, edgePockets);
                        SaveEdge(b, c, edgePockets);
                        SaveEdge(c, a, edgePockets);
                    }
                }
            }
            // Vary the width and density along the real curb. Open crossing gaps contain no curb faces.
            foreach (var (center, axis) in edgePockets.Values.OrderBy(_ => random.Next()))
            {
                Vector2 side = new Vector2(-axis.y, axis.x);
                float density = Mathf.PerlinNoise(center.x * .14f + 142f, center.y * .14f + 74f);
                int attempts = Mathf.RoundToInt(Mathf.Lerp(9, 49, density));
                foreach (float sign in Sides)
                    for (int i = 0; i < attempts; i++)
                    {
                        Vector2 p = center + axis * Range(-.76f, .76f) +
                            side * (sign * (.09f + .82f * Mathf.Pow(Next(), 2.25f)));
                        if (Place(p, true) && ++KerbLeaves >= KerbLimit) return;
                    }
            }
        }

        private static void SaveEdge(Vector3 a, Vector3 b,
            Dictionary<Vector2Int, (Vector2 center, Vector2 axis)> pockets)
        {
            if (Mathf.Abs(a.y - b.y) > .058f) return;
            Vector2 from = XZ(a), delta = XZ(b) - from;
            if (delta.sqrMagnitude < .18f * .18f) return;
            int segments = Mathf.CeilToInt(delta.magnitude / 1.16f);
            for (int i = 0; i < segments; i++)
            {
                var center = from + delta * ((i + .5f) / segments);
                pockets.TryAdd(Cell(center, 1.18f), (center, delta.normalized));
            }
        }

        internal void UnderTrees()
        {
            var visited = new HashSet<Vector2Int>();
            var trees = Object.FindObjectsByType<LODGroup>()
                .OrderBy(group => group.transform.position.x).ThenBy(group => group.transform.position.z)
                .Where(IsBroadleaf).OrderBy(_ => random.Next());
            foreach (var tree in trees)
            {
                var center = XZ(tree.transform.position);
                var districtCell = Cell(center, 12f);
                bool nearDistrict = corridors.Any(cell =>
                    Mathf.Abs(cell.x - districtCell.x) <= 2 && Mathf.Abs(cell.y - districtCell.y) <= 2);
                if (!nearDistrict || !visited.Add(Cell(center, 3.8f))) continue;
                float radius = Mathf.Clamp(tree.size * .42f, 2.3f, 5.4f);
                float phase = Range(0, 2 * Mathf.PI);
                int attempts = Mathf.RoundToInt(radius * Range(41, 69));
                for (int i = 0; i < attempts; i++)
                {
                    float angle = Range(0, 2 * Mathf.PI);
                    float distance = radius * Mathf.Pow(Next(), .84f) *
                        (.88f + .24f * Mathf.Sin(angle * 3f + phase));
                    Vector2 p = center + new Vector2(Mathf.Cos(angle) * 1.16f, Mathf.Sin(angle)) * distance;
                    if (Place(p, distance < radius * .64f) && ++CanopyLeaves >= CanopyLimit) return;
                }
            }
        }

        internal void AlongApproaches()
        {
            foreach (var route in access.routes)
            {
                int before = Count;
                float next = .65f;
                float travelled = 0;
                for (int i = 1; i < route.points.Length; i++)
                {
                    Vector2 a = XZ(route.points[i - 1]), delta = XZ(route.points[i]) - a;
                    float length = delta.magnitude;
                    if (length < .01f) continue;
                    Vector2 axis = delta / length, across = new Vector2(-axis.y, axis.x);
                    while (next <= travelled + length)
                    {
                        Vector2 center = a + axis * (next - travelled);
                        foreach (float side in Sides)
                        {
                            float variation = Mathf.PerlinNoise(center.x * .21f + 150f + side, center.y * .21f + 98f);
                            int attempts = Mathf.RoundToInt(Mathf.Lerp(9, 24, variation));
                            for (int j = 0; j < attempts; j++)
                            {
                                float acrossPath = side * (.64f + .68f * Mathf.Pow(Next(), 2.1f));
                                Vector2 p = center + axis * Range(-.55f, .55f) + across * acrossPath;
                                Place(p, true);
                            }
                        }
                        next += Range(1.65f, 2.65f);
                    }
                    travelled += length;
                    if (Count - before > 93) break;
                }
                // Keep short entrances represented without putting a pile in the door opening.
                if (Count == before && route.points.Length > 1)
                {
                    Vector2 entrance = XZ(route.points[0]);
                    Vector2 axis = (XZ(route.points[1]) - entrance).normalized;
                    Vector2 across = new Vector2(-axis.y, axis.x);
                    foreach (float side in Sides)
                        for (int j = 0; j < 22; j++)
                            Place(entrance + axis * Range(.45f, 1.35f) + across * (side * Range(.65f, 1.15f)), true);
                }
                if (Count > before) Approaches++;
            }
        }

        internal void AgainstWalls()
        {
            var pockets = new List<(Vector2 center, Vector2 axis, Vector2 outside)>();
            var buildings = Object.FindObjectsByType<StreetBuilding>().OrderBy(building => building.address);
            foreach (var building in buildings)
            {
                var bodies = building.bodyColliders.Where(collider => collider && collider.enabled).ToArray();
                if (bodies.Length == 0) continue;
                Bounds bounds = bodies[0].bounds;
                foreach (var collider in bodies) bounds.Encapsulate(collider.bounds);
                Vector2 min = XZ(bounds.min), max = XZ(bounds.max);
                var corners = new[] { min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y) };
                for (int side = 0; side < corners.Length; side++)
                {
                    Vector2 from = corners[side], along = corners[(side + 1) % corners.Length] - from;
                    float length = along.magnitude;
                    if (length < 1) continue;
                    Vector2 axis = along / length, outside = new Vector2(axis.y, -axis.x);
                    for (float step = Range(.45f, 1.25f); step < length; step += Range(3.2f, 5.4f))
                    {
                        Vector2 candidate = from + axis * step + outside * 1.1f;
                        if (!ground.TrySample(candidate, out var surface)) continue;
                        Vector3 start = surface.point + Vector3.up * .34f;
                        var wallRay = new Ray(start, new Vector3(-outside.x, 0, -outside.y));
                        if (!Physics.Raycast(wallRay, out var wall, 4.1f, Layers, QueryTriggerInteraction.Ignore) ||
                            !bodies.Contains(wall.collider)) continue;
                        pockets.Add((XZ(wall.point) + outside * .07f, axis, outside));
                    }
                }
            }
            foreach (var (center, axis, outside) in pockets.OrderBy(_ => random.Next()))
            {
                int attempts = random.Next(15, 34);
                for (int i = 0; i < attempts; i++)
                    if (Place(center + axis * Range(-.82f, .82f) +
                              outside * (.08f + .52f * Mathf.Pow(Next(), 2f)), true) &&
                        ++WallLeaves >= WallLimit) return;
            }
        }

        internal void BetweenDrifts()
        {
            var cells = new HashSet<Vector2Int>();
            foreach (var center in corridors)
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        cells.Add(center + new Vector2Int(x, z));
            var ordered = cells.OrderBy(p => p.x).ThenBy(p => p.y).OrderBy(_ => random.Next()).ToArray();
            // Interleave small quotas across all sectors, so a dense street block cannot
            // consume the connecting scatter budget before the outer yards are sampled.
            for (int pass = 0; pass < 14; pass++)
                foreach (var cell in ordered)
                {
                    int attempts = corridors.Contains(cell) ? 8 : 4;
                    for (int i = 0; i < attempts; i++)
                    {
                        Vector2 point = new Vector2((cell.x + Next()) * 12f, (cell.y + Next()) * 12f);
                        float noise = Mathf.PerlinNoise(point.x * .32f + 312f, point.y * .32f + 54f);
                        if (Next() < noise * .82f && Place(point, false) && ++FieldLeaves >= FieldLimit) return;
                    }
                }
        }

        private bool Place(Vector2 point, bool drift, bool carpet = false)
        {
            if (Count >= LeafLimit || !ground.TrySample(point, out var hit)) return false;
            Vector2Int bin = Cell(point, .08f);
            occupancy.TryGetValue(bin, out int layers);
            if (layers >= (drift ? 3 : 1)) return false;
            float length = carpet ? Range(.12f, .227f) : Range(.105f, .201f);
            Vector3 axis = Quaternion.AngleAxis(Range(0, 360), hit.normal) *
                Vector3.ProjectOnPlane(Vector3.forward, hit.normal).normalized;
            Vector3 width = Vector3.Cross(hit.normal, axis) * (length * Range(.82f, 1.17f));
            if (!ground.Supports(hit, axis * (length * .48f)) || !ground.Supports(hit, width * .39f))
                return false;
            occupancy[bin] = layers + 1;
            Vector2Int key = Cell(point, TileSize);
            if (!Tiles.TryGetValue(key, out var tile))
            {
                tile = new Tile(new Vector3(key.x * TileSize, 0, key.y * TileSize));
                Tiles.Add(key, tile);
            }
            Vector3 center = hit.point - tile.Origin + hit.normal * (.009f + layers * .006f + Range(0, .004f));
            Color tint = Palette[random.Next(Palette.Length)] * Range(.86f, 1.04f);
            Vector2[] outline = Outlines[random.Next(Outlines.Length)];
            tile.Near.Add(center, axis * length, width, hit.normal, tint, outline, true);
            if (tile.Near.Count % 3 == 0)
                tile.Far.Add(center, axis * (length * 1.24f), width * 1.29f, hit.normal, tint, FarOutline, false);
            Count++;
            return true;
        }

        private static bool IsBroadleaf(LODGroup group)
        {
            var levels = group.GetLODs();
            return levels.Length > 0 && levels[0].renderers.Any(renderer => renderer &&
                renderer.sharedMaterials.Any(material => material && Has(material.name, "ModelLeaves")));
        }

        private float Next() => (float)random.NextDouble();
        private float Range(float a, float b) => Mathf.Lerp(a, b, Next());
    }

    private sealed class Ground
    {
        private readonly HashSet<MeshCollider> paved;
        private readonly HashSet<Collider> excluded;
        private readonly Dictionary<MeshCollider, (string[] names, int[] ends)> surfaces =
            new Dictionary<MeshCollider, (string[], int[])>();

        internal Ground(ResidentialAccess access, StreetRoadNetwork road)
        {
            paved = access.surfaces.Concat(road.surfaces).ToHashSet();
            excluded = Object.FindObjectsByType<StreetBuilding>().SelectMany(building => building.bodyColliders).ToHashSet();
            excluded.UnionWith(Object.FindObjectsByType<PlaygroundSite>().Select(site => site.ground));
        }

        internal bool TrySample(Vector2 point, out RaycastHit hit)
        {
            if (!Physics.Raycast(new Vector3(point.x, SampleHeight, point.y), Vector3.down, out hit,
                    SampleDistance, Layers, QueryTriggerInteraction.Ignore) || hit.normal.y < .76f ||
                hit.collider is not MeshCollider collider || !collider.sharedMesh || excluded.Contains(collider) ||
                collider.GetComponentInParent<StreetBuilding>()) return false;

            string material = NameAt(collider, hit.triangleIndex);
            if (Has(material, "MainRoadAsphalt") || Has(material, "ReferenceAsphalt") ||
                Has(material, "RoadPaint") || Has(material, "Playground") || Has(material, "Rubber") ||
                IsKerb(material)) return false;
            return paved.Contains(collider) || collider.name == "Grass" ||
                Has(material, "Grass") || Has(material, "Lawn") || Has(material, "Ground") ||
                Has(material, "Soil") || Has(material, "Mulch") || Has(material, "Verge") ||
                Has(material, "Pav") || Has(material, "Footpath") || Has(material, "Asphalt") ||
                Has(material, "Approach") || Has(material, "Walk") || Has(material, "Concrete");
        }

        internal bool Supports(RaycastHit center, Vector3 edge) =>
            SameHeight(center.point + edge) && SameHeight(center.point - edge);

        private static bool SameHeight(Vector3 point) =>
            Physics.Raycast(point + Vector3.up * .095f, Vector3.down, out var hit, .15f, Layers,
                QueryTriggerInteraction.Ignore) && hit.normal.y > .72f && Mathf.Abs(point.y - hit.point.y) < .036f;

        private string NameAt(MeshCollider collider, int triangle)
        {
            if (!surfaces.TryGetValue(collider, out var entry))
            {
                var renderer = collider.GetComponent<MeshRenderer>();
                var filter = collider.GetComponent<MeshFilter>();
                if (!renderer || !filter || filter.sharedMesh != collider.sharedMesh)
                    entry = (Array.Empty<string>(), Array.Empty<int>());
                else
                {
                    int total = 0;
                    entry = (renderer.sharedMaterials.Select(m => m ? m.name : "").ToArray(),
                        new int[collider.sharedMesh.subMeshCount]);
                    for (int i = 0; i < entry.ends.Length; i++)
                    {
                        total += (int)collider.sharedMesh.GetIndexCount(i) / 3;
                        entry.ends[i] = total;
                    }
                }
                surfaces.Add(collider, entry);
            }
            for (int i = 0; i < Mathf.Min(entry.ends.Length, entry.names.Length); i++)
                if (triangle >= 0 && triangle < entry.ends[i]) return entry.names[i];
            return collider.sharedMesh.name;
        }
    }

    private sealed class Tile
    {
        internal readonly Vector3 Origin;
        internal readonly LeafMesh Near = new LeafMesh();
        internal readonly LeafMesh Far = new LeafMesh();

        internal Tile(Vector3 origin) => Origin = origin;
    }

    private sealed class LeafMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();
        internal int Count { get; private set; }

        internal void Add(Vector3 center, Vector3 axis, Vector3 width, Vector3 normal,
            Color tint, Vector2[] outline, bool folded)
        {
            int first = vertices.Count;
            foreach (Vector2 point in outline)
            {
                float curl = folded ? (point.y > .3f ? .017f : point.x > .2f ? .011f : .002f) : 0;
                vertices.Add(center + width * point.x + axis * point.y + normal * curl);
                uv.Add(point);
                colors.Add(tint * (point.x < -.1f ? .83f : 1.04f));
            }
            for (int i = 1; i < outline.Length - 1; i++)
            {
                triangles.Add(first);
                triangles.Add(first + i);
                triangles.Add(first + i + 1);
            }
            Count++;
        }

        internal Mesh Save(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string path = $"{AssetRoot}/{name}.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved)
            {
                EditorUtility.CopySerialized(mesh, saved);
                EditorUtility.SetDirty(saved);
                Object.DestroyImmediate(mesh);
                return saved;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }

    private static MeshRenderer MakeRenderer(Transform parent, string name, Mesh mesh, Material material)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.rayTracingMode = RayTracingMode.Off;
        return renderer;
    }

    private static void EnsureAssetFolder()
    {
        string parent = "Assets";
        foreach (string name in AssetRoot.Split('/').Skip(1))
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
            parent = path;
        }
    }

    private static bool HasKerb(MeshRenderer renderer) =>
        renderer && renderer.sharedMaterials.Any(material => material && IsKerb(material.name));

    private static bool IsKerb(string name) =>
        Has(name, "Kerb") || Has(name, "Curb") || Has(name, "Coping") || Has(name, "AccessEdge");

    private static bool Has(string name, string term) => name.Contains(term, StringComparison.OrdinalIgnoreCase);
    private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
    private static Vector2Int Cell(Vector2 point, float size) =>
        new Vector2Int(Mathf.FloorToInt(point.x / size), Mathf.FloorToInt(point.y / size));
}
