using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class StreetDenseLawn
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string Folder = "Assets/Art/AutumnGrass/DenseLawn";
    private const string RootName = "Трава · плотный городской газон";
    private const string BaseMaterial = "Assets/Art/AutumnGrass/Urban autumn blades.mat";
    private const string CourtMaterial = "Assets/Art/AutumnGrass/Raised lawn ground.mat";
    private const string IslandMeshes = "Assets/PhotoReconstruction/Stage05Civil/Meshes";
    private const int TileSize = 64;
    private const int DetailResolution = 512;
    private const int HeightResolution = 257;
    private const int PatchResolution = 32;
    private const int BladesPerTuft = 3;
    private const float DetailDistance = 24f;
    private const float Step = (float)TileSize / DetailResolution;
    private const float MaxHeightError = .028f;
    private const float BaseDepth = .007f;
    private const float MainRoadRadius = 18f;
    private const float CourtyardMargin = 16f;

    [Serializable]
    private sealed class BakeReport
    {
        public int tiles, tufts, blades, raisedTufts, instanceChecks, samplesOnOtherSurfaces;
        public float gridMetres, drawDistance, worstHeightErrorMetres;
        public int[] paletteTufts = new int[3];
    }

    private readonly struct Zone
    {
        internal readonly Rect Bounds;
        private readonly Vector2 start, delta;
        private readonly float radius;

        internal Zone(Rect bounds)
        {
            Bounds = bounds;
            start = delta = default;
            radius = 0f;
        }

        internal Zone(Vector2 a, Vector2 b, float radius)
        {
            this.radius = radius;
            start = a;
            delta = b - a;
            Bounds = Rect.MinMaxRect(
                Mathf.Min(a.x, b.x) - radius, Mathf.Min(a.y, b.y) - radius,
                Mathf.Max(a.x, b.x) + radius, Mathf.Max(a.y, b.y) + radius);
        }

        internal bool Contains(Vector2 p)
        {
            if (!Bounds.Contains(p))
                return false;
            if (radius == 0f)
                return true;
            float fraction = Mathf.Clamp01(Vector2.Dot(p - start, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
            return (p - start - delta * fraction).sqrMagnitude <= radius * radius;
        }
    }

    private sealed class RaisedLawn
    {
        internal MeshCollider Collider;
        internal int FirstTriangle, EndTriangle;

        internal bool IsGrass(RaycastHit hit) =>
            hit.triangleIndex >= FirstTriangle && hit.triangleIndex < EndTriangle;
    }

    private sealed class Surfaces
    {
        internal readonly MeshCollider Ground;
        internal readonly RaisedLawn[] Raised;
        internal readonly float BaseY, Height;
        private readonly PhysicsScene physics;
        private readonly float rayY, rayDistance;

        internal Surfaces(Scene scene)
        {
            physics = scene.GetPhysicsScene();
            var colliders = UnityEngine.Object.FindObjectsByType<MeshCollider>()
                .Where(collider => collider.gameObject.scene == scene && collider.enabled && collider.sharedMesh).ToArray();
            Ground = colliders.Single(collider => collider.name == "Grass" && collider.bounds.size.x > 1000);
            var turf = AssetDatabase.LoadAssetAtPath<Material>(CourtMaterial);
            if (!turf)
                throw new InvalidOperationException("The saved raised lawn material is missing.");
            Raised = new RaisedLawn[3];
            for (int island = 0; island < Raised.Length; island++)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{IslandMeshes}/IslandRelief05_{island}.asset");
                var collider = colliders.Single(candidate => candidate.sharedMesh == mesh);
                int slot = Array.IndexOf(collider.GetComponent<MeshRenderer>().sharedMaterials, turf);
                if (slot < 0 || slot >= mesh.subMeshCount || mesh.GetIndexCount(slot) < 3)
                    throw new InvalidOperationException($"Raised lawn {island + 1} has no matching grass surface.");
                int first = 0;
                for (int i = 0; i < slot; i++)
                    first += (int)mesh.GetIndexCount(i) / 3;
                Raised[island] = new RaisedLawn
                {
                    Collider = collider, FirstTriangle = first, EndTriangle = first + (int)mesh.GetIndexCount(slot) / 3
                };
            }

            BaseY = Mathf.Min(Ground.bounds.min.y, Raised.Min(lawn => lawn.Collider.bounds.min.y)) - .15f;
            float roof = Mathf.Max(Ground.bounds.max.y, Raised.Max(lawn => lawn.Collider.bounds.max.y));
            Height = Mathf.Max(1f, roof - BaseY + .15f);
            rayY = Mathf.Max(60f, roof + 20f);
            rayDistance = rayY - BaseY + 1f;
        }

        internal bool VisibleGrass(float x, float z, out RaycastHit hit, out bool raised)
        {
            raised = false;
            if (!physics.Raycast(new Vector3(x, rayY, z), Vector3.down, out hit, rayDistance, ~0,
                    QueryTriggerInteraction.Ignore) || hit.normal.y < .75f)
                return false;
            if (hit.collider == Ground)
                return true;
            foreach (var lawn in Raised)
            {
                if (hit.collider != lawn.Collider || !lawn.IsGrass(hit))
                    continue;
                raised = true;
                return true;
            }
            return false;
        }

        internal float HeightAt(float x, float z)
        {
            var ray = new Ray(new Vector3(x, rayY, z), Vector3.down);
            foreach (var lawn in Raised)
            {
                var bounds = lawn.Collider.bounds;
                if (x >= bounds.min.x && x <= bounds.max.x && z >= bounds.min.z && z <= bounds.max.z &&
                    lawn.Collider.Raycast(ray, out var raised, rayDistance) && lawn.IsGrass(raised))
                    return raised.point.y;
            }
            return Ground.Raycast(ray, out var hit, rayDistance) ? hit.point.y : BaseY;
        }
    }

    [MenuItem("Street/Grass/Bake dense urban lawns")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (scene.isDirty)
            throw new InvalidOperationException("Save the open Street scene before baking the dense grass.");

        Physics.SyncTransforms();
        var surfaces = new Surfaces(scene);
        var tileZones = FindTiles(surfaces);
        EnsureFolder("Assets/Art/AutumnGrass", "DenseLawn");
        EnsureFolder(Folder, "Tiles");
        var prototypes = BuildPrototypes();
        var report = new BakeReport { gridMetres = Step, drawDistance = DetailDistance };
        var previous = scene.GetRootGameObjects().SingleOrDefault(go => go.name == RootName);
        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);

        try
        {
            foreach (var tile in tileZones.OrderBy(entry => entry.Key.x).ThenBy(entry => entry.Key.y))
                BakeTile(tile.Key, tile.Value, surfaces, prototypes, root.transform, report);

            if (report.tufts == 0 || report.raisedTufts == 0 || report.instanceChecks == 0)
                throw new InvalidOperationException("The dense grass failed to populate the expected lawns.");
            if (report.samplesOnOtherSurfaces > 0)
                throw new InvalidOperationException($"Grass placement check found {report.samplesOnOtherSurfaces} instances on another surface.");

            report.blades = report.tufts * BladesPerTuft;
            if (previous)
                UnityEngine.Object.DestroyImmediate(previous);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Could not save dense grass in Street.");
            Directory.CreateDirectory("QA_HDRP");
            File.WriteAllText("QA_HDRP/lawn-density-placement.json", JsonUtility.ToJson(report, true));
            Debug.Log($"STREET_DENSE_LAWN tiles={report.tiles} tufts={report.tufts} blades={report.blades} " +
                $"raisedTufts={report.raisedTufts} verifiedTransforms={report.instanceChecks} distance={DetailDistance:F0}m " +
                $"maxHeightError={report.worstHeightErrorMetres:F3}m");
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(root);
            throw;
        }
    }

    private static Dictionary<Vector2Int, List<Zone>> FindTiles(Surfaces surfaces)
    {
        var data = JsonUtility.FromJson<StreetBuilder.Data>(File.ReadAllText("Assets/StreetSource/street.json"));
        var zones = new List<Zone> { new(new Rect(-48f, -72f, 96f, 120f)) };
        var road = data.roads.Single(entry => entry.main).points;
        for (int i = 1; i < road.Length; i++)
            zones.Add(new Zone(new Vector2(road[i - 1].x, road[i - 1].z),
                new Vector2(road[i].x, road[i].z), MainRoadRadius));

        foreach (var building in data.buildings.Where(building => building.street?.Contains("Дунина") == true))
        {
            var polygon = building.points;
            zones.Add(new Zone(Rect.MinMaxRect(
                polygon.Min(p => p.x) - CourtyardMargin, polygon.Min(p => p.z) - CourtyardMargin,
                polygon.Max(p => p.x) + CourtyardMargin, polygon.Max(p => p.z) + CourtyardMargin)));
        }
        foreach (var raised in surfaces.Raised)
        {
            var bounds = raised.Collider.bounds;
            zones.Add(new Zone(Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z)));
        }

        var tiles = new Dictionary<Vector2Int, List<Zone>>();
        foreach (var zone in zones)
        {
            // Include neighboring zones for the border samples used by each tile's mask.
            var bounds = zone.Bounds;
            for (int z = Mathf.FloorToInt((bounds.yMin - Step) / TileSize);
                z <= Mathf.FloorToInt((bounds.yMax + Step) / TileSize); z++)
            for (int x = Mathf.FloorToInt((bounds.xMin - Step) / TileSize);
                x <= Mathf.FloorToInt((bounds.xMax + Step) / TileSize); x++)
            {
                var key = new Vector2Int(x, z);
                if (!tiles.TryGetValue(key, out var list))
                    tiles.Add(key, list = new List<Zone>());
                list.Add(zone);
            }
        }
        return tiles;
    }

    private static void BakeTile(Vector2Int key, List<Zone> zones, Surfaces surfaces, DetailPrototype[] prototypes,
        Transform parent, BakeReport report)
    {
        var origin = new Vector3(key.x * TileSize, surfaces.BaseY, key.y * TileSize);
        var heights = new float[HeightResolution, HeightResolution];
        const float heightStep = (float)TileSize / (HeightResolution - 1);
        for (int z = 0; z < HeightResolution; z++)
        for (int x = 0; x < HeightResolution; x++)
            heights[z, x] = Mathf.Clamp01((surfaces.HeightAt(origin.x + x * heightStep, origin.z + z * heightStep)
                                          - origin.y) / surfaces.Height);

        // Classify the highest original collider. Eroding the mask by a full cell leaves room for
        // detail jitter, the sideways lean of the blades, and heightmap interpolation at curbs.
        var mask = new byte[DetailResolution + 2, DetailResolution + 2];
        for (int z = -1; z <= DetailResolution; z++)
        for (int x = -1; x <= DetailResolution; x++)
        {
            float localX = (x + .5f) * Step, localZ = (z + .5f) * Step;
            var world = new Vector2(origin.x + localX, origin.z + localZ);
            bool covered = false;
            foreach (var zone in zones)
            {
                if (!zone.Contains(world))
                    continue;
                covered = true;
                break;
            }
            if (!covered || !surfaces.VisibleGrass(world.x, world.y, out var hit, out bool raised))
                continue;
            float encodedY = origin.y + surfaces.Height * InterpolateHeight(heights, localX, localZ);
            if (Mathf.Abs(encodedY - hit.point.y) <= MaxHeightError)
                mask[z + 1, x + 1] = raised ? (byte)2 : (byte)1;
        }

        var layers = new int[prototypes.Length][,];
        for (int index = 0; index < layers.Length; index++)
            layers[index] = new int[DetailResolution, DetailResolution];
        int count = 0;
        var random = new System.Random(unchecked(key.x * 73856093 ^ key.y * 19349663 ^ 5839));
        for (int z = 1; z <= DetailResolution; z++)
        for (int x = 1; x <= DetailResolution; x++)
        {
            byte kind = mask[z, x];
            if (kind == 0 || !HasClearance(mask, x, z, kind))
                continue;
            float continuity = Mathf.PerlinNoise((origin.x + x * Step) * .71f + 52.3f,
                (origin.z + z * Step) * .71f + 65.9f);
            float coverage = kind == 2 ? .98f : Mathf.Lerp(.79f, .97f, continuity);
            if (random.NextDouble() > coverage)
                continue;
            double tone = random.NextDouble();
            int layer = tone < .46 ? 0 : tone < .82 ? 1 : 2;
            layers[layer][z - 1, x - 1] = 1;
            report.paletteTufts[layer]++;
            if (kind == 2)
                report.raisedTufts++;
            count++;
        }
        if (count == 0)
            return;

        string name = $"Lawn_{key.x}_{key.y}";
        string path = $"{Folder}/Tiles/{name}.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (!data)
        {
            data = new TerrainData { name = name };
            AssetDatabase.CreateAsset(data, path);
        }
        data.heightmapResolution = HeightResolution;
        data.size = new Vector3(TileSize, surfaces.Height, TileSize);
        data.SetHeights(0, 0, heights);
        data.SetDetailResolution(DetailResolution, PatchResolution);
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        data.detailPrototypes = prototypes;
        for (int i = 0; i < prototypes.Length; i++)
            data.SetDetailLayer(0, 0, i, layers[i]);
        EditorUtility.SetDirty(data);

        CheckNativeTransforms(data, origin, surfaces, report);
        var tile = new GameObject(name);
        tile.transform.SetParent(parent, false);
        tile.transform.position = origin;
        // This Terrain carries instanced details only. Its sampling heightmap has no visible
        // surface, shadows, ray-tracing geometry, or collider.
        var terrain = tile.AddComponent<Terrain>();
        terrain.terrainData = data;
        terrain.drawHeightmap = false;
        terrain.drawTreesAndFoliage = true;
        terrain.allowAutoConnect = false;
        terrain.enableHeightmapRayTracing = false;
        terrain.shadowCastingMode = ShadowCastingMode.Off;
        terrain.ignoreQualitySettings = true;
        terrain.detailObjectDistance = DetailDistance;
        terrain.detailObjectDensity = 1f;
        terrain.keepUnusedRenderingResources = false;
        report.tiles++;
        report.tufts += count;
    }

    private static float InterpolateHeight(float[,] heights, float x, float z)
    {
        const float scale = (HeightResolution - 1f) / TileSize;
        float gx = Mathf.Clamp(x * scale, 0f, HeightResolution - 1f);
        float gz = Mathf.Clamp(z * scale, 0f, HeightResolution - 1f);
        int ix = Mathf.Min(Mathf.FloorToInt(gx), HeightResolution - 2);
        int iz = Mathf.Min(Mathf.FloorToInt(gz), HeightResolution - 2);
        float low = Mathf.Lerp(heights[iz, ix], heights[iz, ix + 1], gx - ix);
        float high = Mathf.Lerp(heights[iz + 1, ix], heights[iz + 1, ix + 1], gx - ix);
        return Mathf.Lerp(low, high, gz - iz);
    }

    private static bool HasClearance(byte[,] mask, int x, int z, byte kind)
    {
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            if (mask[z + dz, x + dx] != kind)
                return false;
        return true;
    }

    private static void CheckNativeTransforms(TerrainData data, Vector3 origin, Surfaces surfaces, BakeReport report)
    {
        for (int z = 0; z < data.detailPatchCount; z += 2)
        for (int x = 0; x < data.detailPatchCount; x += 2)
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
        {
            var visible = data.ComputeDetailInstanceTransforms(x, z, layer, 1f, out _);
            for (int i = 0; i < visible.Length; i += 13)
            {
                var instance = visible[i];
                report.instanceChecks++;
                if (!surfaces.VisibleGrass(origin.x + instance.posX, origin.z + instance.posZ, out var hit, out _))
                {
                    report.samplesOnOtherSurfaces++;
                    continue;
                }
                report.worstHeightErrorMetres = Mathf.Max(report.worstHeightErrorMetres,
                    Mathf.Abs(origin.y + instance.posY - hit.point.y));
            }
        }
    }

    private static DetailPrototype[] BuildPrototypes()
    {
        string materialPath = $"{Folder}/Lawn HDRP Instanced.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterial);
            if (!source)
                throw new InvalidOperationException("The HDRP grass shader material is missing.");
            material = new Material(source) { name = "Lawn HDRP Instanced", enableInstancing = true };
            material.SetFloat("_RayTracing", 0f);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetFloat("_Grass", 3f);
        EditorUtility.SetDirty(material);

        // Linear albedo, from cool greens through olive grass to dry yellow tips.
        Color[] palette = { new(.163f, .229f, .073f), new(.207f, .251f, .090f), new(.317f, .266f, .117f) };
        var prototypes = new DetailPrototype[palette.Length];
        for (int index = 0; index < prototypes.Length; index++)
        {
            string name = $"Lawn tuft {index + 1}";
            string meshPath = $"{Folder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh)
            {
                mesh = MakeTuft(name, palette[index], index);
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            string prefabPath = $"{Folder}/{name}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab)
            {
                var source = new GameObject(name);
                source.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = source.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
                UnityEngine.Object.DestroyImmediate(source);
            }

            prototypes[index] = new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                useDensityScaling = false,
                minHeight = .80f,
                maxHeight = 1.16f,
                minWidth = .81f,
                maxWidth = 1.15f,
                noiseSeed = 2341 + index * 191,
                noiseSpread = .26f,
                positionJitter = .48f,
                alignToGround = .18f,
                healthyColor = Color.white,
                dryColor = Color.white
            };
        }
        return prototypes;
    }

    private static Mesh MakeTuft(string name, Color baseColor, int variant)
    {
        var positions = new List<Vector3>(BladesPerTuft * 4);
        var normals = new List<Vector3>(positions.Capacity);
        var colors = new List<Color>(positions.Capacity);
        var tangents = new List<Vector4>(positions.Capacity);
        var uv = new List<Vector4>(positions.Capacity);
        var triangles = new List<int>(BladesPerTuft * 6);
        var random = new System.Random(953 + variant * 161);
        for (int blade = 0; blade < BladesPerTuft; blade++)
        {
            float angle = variant * .56f + blade * 2.39996f + (float)random.NextDouble() * .5f;
            Vector3 facing = new(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 side = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 normal = (Vector3.up * 1.6f + facing * .62f).normalized;
            Vector3 root = facing * (.007f + (float)random.NextDouble() * .018f) - Vector3.up * BaseDepth;
            float height = .047f + (float)random.NextDouble() * .039f;
            float lean = height * (.26f + (float)random.NextDouble() * .43f);
            float width = .0044f + (float)random.NextDouble() * .0034f;
            Vector3 mid = root + Vector3.up * (height * .55f) + facing * (lean * .34f);
            int start = positions.Count;
            positions.AddRange(new[]
            {
                root - side * width, root + side * width, mid - side * width * .65f,
                root + Vector3.up * height + facing * lean
            });
            uv.AddRange(new Vector4[] { new(0,0,0,0), new(1,0,0,0), new(0,.55f,0,0), new(.5f,1,0,0) });
            colors.AddRange(new[] { baseColor * .78f, baseColor * .84f, baseColor, baseColor * 1.15f });
            for (int i = 0; i < 4; i++)
            {
                normals.Add(normal);
                tangents.Add(new Vector4(side.x, 0f, side.z, -1f));
            }
            triangles.AddRange(new[] { start, start + 1, start + 2, start + 2, start + 1, start + 3 });
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetTangents(tangents);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        var bounds = mesh.bounds;
        bounds.Expand(.02f);
        mesh.bounds = bounds;
        return mesh;
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }

    public static void ApplyAndBuildWindows()
    {
        Apply();
        string path = Path.GetFullPath("Build/StreetWindowsHDRPLawnDense/DuninaGorkavicha.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = path,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Dense lawn Windows build failed: {report.summary.result}");
        Debug.Log($"STREET_DENSE_LAWN_BUILD {path}");
    }
}
