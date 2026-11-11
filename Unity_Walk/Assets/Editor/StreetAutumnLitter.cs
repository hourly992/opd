using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

internal static class StreetAutumnLitter
{
    private const string AssetRoot = "Assets/PhotoReconstruction/Stage08Autumn";
    private const string MeshRoot = "Assets/PhotoReconstruction/Stage09Courtyard/Leaves";
    private const string GroupName = "Осень · опавшая листва";
    private const string BenchGroupName = "Двор №7 · белые скамьи по фотографии";
    private static readonly Color[] Palette =
    {
        new Color(.95f, .59f, .13f), new Color(.94f, .43f, .085f),
        new Color(.69f, .23f, .055f), new Color(.93f, .73f, .25f),
        new Color(.61f, .37f, .13f), new Color(.74f, .50f, .14f)
    };
    private static readonly Vector2[] Birch =
    {
        new Vector2(0,-.55f), new Vector2(-.25f,-.30f), new Vector2(-.33f,-.06f),
        new Vector2(-.27f,.17f), new Vector2(-.16f,.34f), new Vector2(0,.54f),
        new Vector2(.13f,.30f), new Vector2(.27f,.13f), new Vector2(.31f,-.12f),
        new Vector2(.20f,-.35f)
    };
    private static readonly Vector2[] Maple =
    {
        new Vector2(0,-.54f), new Vector2(-.32f,-.39f), new Vector2(-.51f,-.12f),
        new Vector2(-.29f,-.14f), new Vector2(-.50f,.31f), new Vector2(-.18f,.14f),
        new Vector2(0,.57f), new Vector2(.19f,.13f), new Vector2(.49f,.31f),
        new Vector2(.30f,-.14f), new Vector2(.50f,-.11f), new Vector2(.31f,-.39f)
    };

    internal static void ApplyMargins(string assetRoot)
    {
        const string detailGroup = "Осень · детали у краёв дорожек";
        if (GameObject.Find(detailGroup)) return;
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var leafMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Materials/FallenLeaves.mat");
        var grass = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot +
            "/Materials/LawnBlades_960fa316de2bd104daca807eb076de76.mat");
        var shrub = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StreetGenerated/Pilot/Prefabs/ShrubV1.prefab");
        var concrete = UnityEngine.Object.FindObjectsByType<MeshRenderer>().SelectMany(r => r.sharedMaterials)
            .FirstOrDefault(material => material && material.name == "EntranceConcrete");
        if (!access || !leafMaterial || !grass || !shrub || !concrete)
            throw new InvalidOperationException("The saved street, leaf, grass, shrub and mineral materials are required.");
        var mineral = DistrictAutumnStyle.CopyMaterial(concrete, "DryGravel");
        mineral.SetColor("_Color", new Color(.47f, .43f, .36f));
        mineral.SetFloat("_Smoothness", .12f);
        EditorUtility.SetDirty(mineral);
        var group = new GameObject(detailGroup).transform;
        var leaves = new Dictionary<Vector2Int, LeafBatch>();
        var placedShrubs = new List<Vector3>();
        var allPaths = access.routes.SelectMany(route => route.points).Select(XZ).ToArray();
        var hardSurfaces = new HashSet<MeshCollider>(access.surfaces);
        var street = UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
        if (street) hardSurfaces.UnionWith(street.surfaces);
        var random = new Random(131109);
        int tuftCount = 0, stones = 0, sites = 0;
        Physics.SyncTransforms();
        Directory.CreateDirectory(assetRoot + "/Meshes");
        AssetDatabase.Refresh();
        var endpoints = new[]
        {
            new Vector2(-103.582f, 156.452f), new Vector2(-46.623f, -47.697f),
            new Vector2(-18.790f, -104.057f), new Vector2(16.102f, -174.723f),
            new Vector2(125.454f, 5.869f), new Vector2(166.8544f, 98.192f),
            new Vector2(178.355f, 32.318f), new Vector2(72.810f, -107.209f)
        };
        foreach (var endpoint in endpoints)
        {
            var route = access.routes.Single(item => item.points.Length > 2 &&
                (XZ(item.points[0]) - endpoint).sqrMagnitude < .01f);
            var accents = new EnvironmentMesh();
            int accentsCount = 0;
            float station = 0, next = 2.35f;
            for (int i = 1; i < route.points.Length && next <= 19f; i++)
            {
                Vector2 first = XZ(route.points[i - 1]), last = XZ(route.points[i]);
                Vector2 offset = last - first;
                float length = offset.magnitude;
                if (length < .001f) continue;
                Vector2 tangent = offset / length;
                var side = new Vector2(-tangent.y, tangent.x);
                for (; next <= station + length && next <= 19f; next += Mathf.Lerp(1.7f, 2.65f, Next(random)))
                {
                    Vector2 anchor = first + tangent * (next - station);
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        for (int attempt = 0; attempt < 29; attempt++)
                        {
                            float spread = Mathf.Lerp(.61f, 1.40f, Mathf.Pow(Next(random), .8f));
                            var candidate = anchor + side * (spread * sign) + tangent * Mathf.Lerp(-.61f, .61f, Next(random));
                            if (TryGround(candidate, out var hit) &&
                                (IsVerge(hit) || hardSurfaces.Contains((MeshCollider)hit.collider)))
                                AddAt(random, leaves, candidate, false);
                        }
                        for (int attempt = 0; attempt < 6; attempt++)
                        {
                            var candidate = anchor + side * (Mathf.Lerp(.95f, 1.65f, Next(random)) * sign) +
                                tangent * Mathf.Lerp(-.48f, .48f, Next(random));
                            if (!TryGround(candidate, out var hit) || !IsVerge(hit)) continue;
                            Vector3 point = hit.point;
                            if (attempt < 4)
                            {
                                float height = Mathf.Lerp(.09f, .165f, Next(random));
                                Color tint = Color.Lerp(new Color(.62f, .56f, .37f), new Color(.84f, .75f, .46f), Next(random));
                                accents.GrassTuft(grass, point, height, Next(random) * Mathf.PI, tint);
                                tuftCount++;
                            }
                            else
                            {
                                float size = Mathf.Lerp(.025f, .061f, Next(random));
                                accents.RoundedBox(mineral, point + Vector3.up * (size * .10f),
                                    new Vector3(size, size * .53f, size * .8f), size * .13f, .78f);
                                stones++;
                            }
                            accentsCount++;
                        }
                        var root = anchor + side * (2.04f * sign);
                        if (placedShrubs.Count >= 10 || Next(random) > .23f ||
                            allPaths.Any(path => (path - root).sqrMagnitude < 1.35f * 1.35f) ||
                            placedShrubs.Any(point => (XZ(point) - root).sqrMagnitude < 2.6f * 2.6f) ||
                            !TryGround(root, out var soil) || !IsVerge(soil) ||
                            Physics.CheckSphere(soil.point + Vector3.up * .42f, .33f, ~0, QueryTriggerInteraction.Ignore))
                            continue;
                        var plant = (GameObject)PrefabUtility.InstantiatePrefab(shrub, group);
                        plant.transform.SetPositionAndRotation(soil.point, Quaternion.Euler(0, Next(random) * 360, 0));
                        plant.transform.localScale *= Mathf.Lerp(.48f, .70f, Next(random));
                        foreach (var collider in plant.GetComponentsInChildren<Collider>())
                        {
                            collider.enabled = false;
                            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                        }
                        placedShrubs.Add(soil.point);
                    }
                }
                station += length;
            }
            if (accentsCount > 0)
            {
                var detail = accents.Create($"AutumnVerge_{sites}", $"Мелкие детали кромки {sites + 1}", assetRoot);
                detail.transform.SetParent(group, true);
                SetDetailLod(detail.GetComponent<MeshRenderer>(), .022f);
            }
            sites++;
        }
        int leafCount = leaves.Values.Sum(batch => batch.count);
        if (leafCount < 350 || tuftCount < 20 || placedShrubs.Count < 3)
            throw new InvalidOperationException("The saved path margins have insufficient clear surfaces for autumn details.");
        foreach (var entry in leaves)
        {
            var batch = entry.Value;
            string name = $"AutumnPathLeaves_{entry.Key.x}_{entry.Key.y}";
            var renderer = CreateRenderer(group, name, SaveMesh($"{assetRoot}/Meshes/{name}.asset", name, batch), leafMaterial);
            renderer.transform.localPosition = batch.origin;
            SetDetailLod(renderer, .048f);
        }
        Debug.Log($"STAGE13_DETAILS leaves={leafCount} tufts={tuftCount} gravel={stones} shrubs={placedShrubs.Count} approaches={sites}");
    }

    private static bool IsVerge(RaycastHit hit) => hit.collider.name == "Grass";

    private static void SetDetailLod(MeshRenderer renderer, float threshold)
    {
        var lod = renderer.gameObject.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(threshold, new Renderer[] { renderer }) });
        lod.RecalculateBounds();
    }

    internal static void Apply()
    {
        Directory.CreateDirectory(MeshRoot);
        AssetDatabase.Refresh();
        string materialPath = AssetRoot + "/Materials/FallenLeaves.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            var shader = Shader.Find("Street/FallenLeaves");
            if (!shader)
                throw new InvalidOperationException("Street/FallenLeaves shader is required.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        var random = new Random(76918);
        var cells = new Dictionary<Vector2Int, LeafBatch>();
        var trees = new HashSet<Vector2Int>();
        Physics.SyncTransforms();

        foreach (var lod in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None)
                     .OrderBy(group => group.transform.position.z).ThenBy(group => group.transform.position.x))
        {
            Vector3 anchor = lod.transform.position;
            // The near-player street corridor and the inner courtyard are the only areas
            // detailed with individual leaves. Woodland already has its own ground material.
            bool street = Mathf.Abs(anchor.x + 34 + .55f * anchor.z) < 27 && Mathf.Abs(anchor.z) < 151;
            bool court = anchor.x >= -2 && anchor.x <= 78 && anchor.z >= 14 && anchor.z <= 60;
            if (!street && !court)
                continue;
            var levels = lod.GetLODs();
            if (levels.Length == 0 || !levels[0].renderers.Any(renderer => renderer &&
                    renderer.sharedMaterials.Any(leaf => leaf && leaf.name == "ModelLeaves")))
                continue;
            var cell = new Vector2Int(Mathf.RoundToInt(anchor.x / 5), Mathf.RoundToInt(anchor.z / 5));
            if (trees.Add(cell))
                Scatter(random, cells, new Vector2(anchor.x, anchor.z), new Vector2(4.7f, 3.4f), 105);
        }

        var courtRandom = new Random(86170);
        int edgeCount = ScatterCourtEdges(courtRandom, cells);
        var benches = GameObject.Find(BenchGroupName);
        if (benches)
        {
            foreach (var filter in benches.GetComponentsInChildren<MeshFilter>())
            {
                Transform bench = filter.transform;
                foreach (float end in new[] { -.93f, .93f })
                {
                    var center = XZ(bench.TransformPoint(new Vector3(end, 0, .04f)));
                    Scatter(courtRandom, cells, center, new Vector2(.78f, .50f), 95, true);
                }
            }
        }

        int leafCount = cells.Values.Sum(batch => batch.count);
        if (leafCount < 500 || leafCount > 30000)
            throw new InvalidOperationException($"Unexpected number of autumn leaves: {leafCount}.");
        var previous = GameObject.Find(GroupName);
        if (previous)
            UnityEngine.Object.DestroyImmediate(previous);
        var group = new GameObject(GroupName);
        var activeAssets = new HashSet<string>();
        foreach (var entry in cells)
        {
            var batch = entry.Value;
            string name = $"Leaves_{entry.Key.x}_{entry.Key.y}";
            string path = $"{MeshRoot}/{name}.asset";
            activeAssets.Add(path);
            var renderer = CreateRenderer(group.transform, name, SaveMesh(path, name, batch), material);
            renderer.transform.localPosition = batch.origin;
            var lod = renderer.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.095f, new Renderer[] { renderer }) });
            lod.RecalculateBounds();
        }
        int driftingCount = AddWindblownLeaves(group.transform, material, courtRandom);
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(MeshRoot + "/Leaves_", StringComparison.Ordinal) && !activeAssets.Contains(path))
                AssetDatabase.DeleteAsset(path);
        }
        Debug.Log($"STREET_AUTUMN_LEAVES leaves={leafCount} batches={cells.Count} broadleafTrees={trees.Count} courtEdges={edgeCount} drifting={driftingCount}");
    }

    private static void Scatter(Random random, Dictionary<Vector2Int, LeafBatch> cells,
        Vector2 center, Vector2 radius, int attempts, bool courtyardOnly = false)
    {
        for (int i = 0; i < attempts; i++)
        {
            float angle = Next(random) * 2 * Mathf.PI;
            float distance = Mathf.Pow(Next(random), .72f);
            var position = center + Vector2.Scale(radius, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))) * distance;
            AddAt(random, cells, position, courtyardOnly);
        }
    }

    private static int ScatterCourtEdges(Random random, Dictionary<Vector2Int, LeafBatch> cells)
    {
        var islands = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
            .Where(f => f.sharedMesh && f.sharedMesh.name.StartsWith("IslandBody05_", StringComparison.Ordinal))
            .OrderBy(f => f.sharedMesh.name, StringComparer.Ordinal);
        int edgeCount = 0;
        foreach (var island in islands)
        {
            var mesh = island.sharedMesh;
            var vertices = mesh.vertices.Select(island.transform.TransformPoint).ToArray();
            var center = XZ(island.transform.TransformPoint(mesh.bounds.center));
            var edges = new HashSet<(Vector2, Vector2)>();
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                var normal = Vector3.Cross(b - a, c - a).normalized;
                float wallHeight = Mathf.Max(a.y, b.y, c.y) - Mathf.Min(a.y, b.y, c.y);
                if (Mathf.Abs(normal.y) > .1f || wallHeight < .14f)
                    continue;
                // An outside wall triangle projects to two distinct horizontal points.
                // The minimum wall height excludes the short inner rim beside the soil.
                var first = XZ(a);
                var last = (XZ(b) - first).sqrMagnitude > (XZ(c) - first).sqrMagnitude ? XZ(b) : XZ(c);
                if (first.x > last.x || (first.x == last.x && first.y > last.y))
                    (first, last) = (last, first);
                if ((last - first).sqrMagnitude < .025f || !edges.Add((first, last)))
                    continue;

                var tangent = last - first;
                var away = new Vector2(-tangent.y, tangent.x).normalized;
                if (Vector2.Dot(away, (first + last) * .5f - center) < 0)
                    away = -away;
                int count = Mathf.CeilToInt(tangent.magnitude * 23f);
                for (int sample = 0; sample < count; sample++)
                {
                    var edge = Vector2.Lerp(first, last, Next(random));
                    float patch = Mathf.PerlinNoise(edge.x * 1.36f, edge.y * 1.36f);
                    if (Next(random) < Mathf.Lerp(.08f, .78f, patch))
                        AddAt(random, cells, edge + away * Mathf.Lerp(.09f, .76f, Mathf.Pow(Next(random), 2)), true);
                }
                edgeCount++;
            }
        }
        return edgeCount;
    }

    private static void AddAt(Random random, Dictionary<Vector2Int, LeafBatch> cells, Vector2 position, bool courtyardOnly)
    {
        if (!TryGround(position, out var hit) || (courtyardOnly && !CourtPaving(hit)))
            return;
        // Keep the cross passages open in the center, letting leaves collect at both sides.
        if ((position.x >= 39.35f && position.x <= 40.85f && position.y >= 28.4f && position.y <= 47.1f) ||
            (position.y >= 37.65f && position.y <= 38.95f && position.x >= 24 && position.x <= 60.5f))
            return;
        var key = new Vector2Int(Mathf.FloorToInt(position.x / 12), Mathf.FloorToInt(position.y / 12));
        if (!cells.TryGetValue(key, out var batch))
        {
            batch = new LeafBatch { origin = new Vector3(key.x * 12, 0, key.y * 12) };
            cells.Add(key, batch);
        }
        var outline = Next(random) < .78f ? Birch : Maple;
        float size = Mathf.Lerp(.073f, .17f, Next(random));
        float angle = Next(random) * 360;
        Vector3 axis = Quaternion.AngleAxis(angle, hit.normal) * Vector3.ProjectOnPlane(Vector3.forward, hit.normal).normalized;
        Vector3 width = Vector3.Cross(hit.normal, axis);
        Vector3 center = hit.point - batch.origin + hit.normal * (.009f + Next(random) * .006f);
        var colour = Palette[random.Next(Palette.Length)] * Mathf.Lerp(.77f, 1.06f, Next(random));
        AddLeaf(batch, outline, size, axis, width, hit.normal, center, colour);
    }

    private static bool TryGround(Vector2 position, out RaycastHit hit)
    {
        var ray = new Ray(new Vector3(position.x, 1.25f, position.y), Vector3.down);
        return Physics.Raycast(ray, out hit, 1.95f, ~0, QueryTriggerInteraction.Ignore) &&
            hit.normal.y >= .82f && hit.point.y <= .35f && hit.collider is MeshCollider;
    }

    private static bool CourtPaving(RaycastHit hit)
    {
        var mesh = ((MeshCollider)hit.collider).sharedMesh;
        return mesh && (mesh.name == "Stage04CourtPaving" || mesh.name == "CourtDoorContinuousPaving05" ||
            hit.collider.name == "Серое прямоугольное мощение · внутренний двор");
    }

    private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);

    private static Mesh SaveMesh(string path, string name, LeafBatch batch)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool newMesh = !mesh;
        if (newMesh)
            mesh = new Mesh { name = name };
        mesh.Clear();
        mesh.indexFormat = batch.vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(batch.vertices);
        mesh.SetColors(batch.colours);
        mesh.SetUVs(0, batch.uv);
        mesh.SetTriangles(batch.triangles, 0);
        mesh.RecalculateNormals();
        if (newMesh)
            AssetDatabase.CreateAsset(mesh, path);
        else
            EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static MeshRenderer CreateRenderer(Transform group, string name, Mesh mesh, Material material)
    {
        var leaf = new GameObject(name);
        leaf.transform.SetParent(group, false);
        leaf.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = leaf.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return renderer;
    }

    private static int AddWindblownLeaves(Transform group, Material material, Random random)
    {
        var paths = new List<StreetWindblownLeaves.Path>();
        Mesh mesh = null;
        for (int attempt = 0; attempt < 90 && paths.Count < 8; attempt++)
        {
            var from = new Vector2(Mathf.Lerp(27.5f, 46f, Next(random)), Mathf.Lerp(36.15f, 41.35f, Next(random)));
            var to = from + new Vector2(Mathf.Lerp(.63f, 1.35f, Next(random)), Mathf.Lerp(.20f, .60f, Next(random)));
            if (!TryGround(from, out var first) || !CourtPaving(first) ||
                !TryGround(to, out var last) || !CourtPaving(last))
                continue;
            bool clear = true;
            for (int step = 1; step < 5; step++)
            {
                float t = step / 5f;
                if (!TryGround(Vector2.Lerp(from, to, t), out var middle) || !CourtPaving(middle) ||
                    Mathf.Abs(middle.point.y - Mathf.Lerp(first.point.y, last.point.y, t)) > .018f)
                    clear = false;
            }
            if (!clear)
                continue;
            if (!mesh)
            {
                var batch = new LeafBatch();
                AddLeaf(batch, Birch, .13f, Vector3.forward, Vector3.right, Vector3.up, Vector3.zero, Palette[1]);
                mesh = SaveMesh(MeshRoot + "/WindblownLeaf.asset", "WindblownLeaf", batch);
            }
            var renderer = CreateRenderer(group, $"Лист на ветру {paths.Count + 1}", mesh, material);
            renderer.enabled = false;
            paths.Add(new StreetWindblownLeaves.Path
            {
                renderer = renderer, from = first.point, to = last.point,
                period = Mathf.Lerp(6f, 11f, Next(random)), phase = Mathf.Lerp(0f, 10f, Next(random))
            });
        }
        if (paths.Count > 0)
            group.gameObject.AddComponent<StreetWindblownLeaves>().paths = paths.ToArray();
        return paths.Count;
    }

    private static void AddLeaf(LeafBatch batch, Vector2[] outline, float size, Vector3 axis,
        Vector3 width, Vector3 up, Vector3 center, Color colour)
    {
        int start = batch.vertices.Count;
        batch.vertices.Add(center + up * .009f);
        batch.colours.Add(colour * 1.13f);
        batch.uv.Add(Vector2.zero);
        for (int edge = 0; edge < outline.Length; edge++)
        {
            Vector2 p = outline[edge];
            float curl = Mathf.Abs(p.y) * size * .10f;
            batch.vertices.Add(center + (width * p.x + axis * p.y) * size + up * curl);
            batch.colours.Add(colour * (p.x < 0 ? .80f : .96f));
            batch.uv.Add(p);
            batch.triangles.Add(start);
            batch.triangles.Add(start + 1 + edge);
            batch.triangles.Add(start + 1 + (edge + 1) % outline.Length);
        }
        batch.count++;
    }

    private static float Next(Random random) => (float)random.NextDouble();

    private sealed class LeafBatch
    {
        internal readonly List<Vector3> vertices = new List<Vector3>();
        internal readonly List<Color> colours = new List<Color>();
        internal readonly List<Vector2> uv = new List<Vector2>();
        internal readonly List<int> triangles = new List<int>();
        internal Vector3 origin;
        internal int count;
    }
}
