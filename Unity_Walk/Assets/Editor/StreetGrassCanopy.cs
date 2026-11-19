using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetGrassCanopy
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string AssetsFolder = "Assets/Art/AutumnGrass/CanopyDensity";
    private const string ReportFolder = "QA_HDRP/grass-canopy";
    private const float DetailDistance = 29f;
    private const float BladeClearance = .16f;
    private const float HeightTolerance = .068f;
    private const uint Seed = 3122177;

    [Serializable]
    private sealed class Report
    {
        public int terrainTiles, originalTufts, addedTufts, filledCells, protectedCells, grassLodGroups;
        public float drawDistance = DetailDistance, clearance = BladeClearance;
        public int[] addedTones = new int[3];
    }

    private sealed class LawnProbe
    {
        private readonly PhysicsScene physics;
        private readonly Dictionary<MeshCollider, bool[]> lawnSlots = new();

        internal LawnProbe(PhysicsScene physics) => this.physics = physics;

        internal bool Height(float x, float z, out float height)
        {
            height = 0f;
            if (!physics.Raycast(new Vector3(x, 4f, z), Vector3.down, out var hit, 4.5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < .72f || hit.collider is not MeshCollider collider ||
                !collider.sharedMesh || hit.triangleIndex < 0)
                return false;

            height = hit.point.y;
            if (collider.name == "Grass" && collider.bounds.size.x > 1000)
                return true;
            if (!lawnSlots.TryGetValue(collider, out bool[] flags))
            {
                var mesh = collider.sharedMesh;
                flags = new bool[mesh.subMeshCount];
                if (collider.TryGetComponent<MeshRenderer>(out var renderer) &&
                    collider.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh == mesh)
                {
                    var materials = renderer.sharedMaterials;
                    for (int slot = 0; slot < flags.Length && slot < materials.Length; slot++)
                    {
                        var material = materials[slot];
                        flags[slot] = material && (material.name == "Lawn ground" ||
                            material.name == "Raised lawn ground");
                    }
                }
                lawnSlots.Add(collider, flags);
            }
            var index = hit.triangleIndex;
            for (int i = 0; i < flags.Length; i++)
            {
                int triangles = (int)collider.sharedMesh.GetIndexCount(i) / 3;
                if (index < triangles) return flags[i];
                index -= triangles;
            }
            return false;
        }
    }

    [MenuItem("Street/Grass/Thicken saved district lawns")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var tiles = UnityEngine.Object.FindObjectsByType<Terrain>()
            .Where(tile => tile.gameObject.scene == scene && !tile.drawHeightmap &&
                !tile.GetComponent<TerrainCollider>() && tile.terrainData &&
                tile.terrainData.detailPrototypes.Length != 0 &&
                tile.name.StartsWith("Lawn_", StringComparison.Ordinal)).ToArray();
        if (tiles.Length == 0 || tiles.Any(tile => AssetDatabase.GetAssetPath(tile.terrainData).StartsWith(
            AssetsFolder + "/", StringComparison.Ordinal)))
            throw new InvalidOperationException("Load the saved lawn-grades scene before applying the district grass.");

        if (!AssetDatabase.IsValidFolder(AssetsFolder))
            AssetDatabase.CreateFolder("Assets/Art/AutumnGrass", "CanopyDensity");

        var report = new Report();
        var probe = new LawnProbe(scene.GetPhysicsScene());
        Physics.SyncTransforms();
        var garageBounds = UnityEngine.Object.FindObjectsByType<UndergroundParkingEntrance>()
            .Where(entrance => entrance.floorAndWalls)
            .Select(entrance => entrance.floorAndWalls.bounds).ToArray();
        foreach (var tile in tiles.OrderBy(tile => tile.name, StringComparer.Ordinal))
            ThickenTile(tile, probe, garageBounds, report);

        foreach (var group in UnityEngine.Object.FindObjectsByType<LODGroup>())
        {
            var levels = group.GetLODs();
            if (group.gameObject.scene != scene || levels.Length == 0 ||
                !levels.SelectMany(level => level.renderers).Any(IsLawnRenderer) ||
                !group.name.StartsWith("Трава · ячейка ", StringComparison.Ordinal) &&
                !group.name.StartsWith("Поднятый газон ", StringComparison.Ordinal))
                continue;
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = false;
            Vector3 referencePoint = group.localReferencePoint;
            float size = group.size;
            for (int i = 0; i < levels.Length; i++)
                levels[i].fadeTransitionWidth = .25f;
            group.SetLODs(levels);
            group.localReferencePoint = referencePoint;
            group.size = size;
            report.grassLodGroups++;
        }

        if (report.addedTufts == 0 || report.addedTufts > report.originalTufts * .45f || report.grassLodGroups == 0)
            throw new InvalidOperationException("The district grass mask or density is unexpected.");

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the district grass.");
        Directory.CreateDirectory(ReportFolder);
        File.WriteAllText(ReportFolder + "/grass-edit.json", JsonUtility.ToJson(report, true));
        Debug.Log("GRASS_CANOPY_SAVED " + JsonUtility.ToJson(report));
    }

    private static bool IsLawnRenderer(Renderer renderer)
    {
        if (!renderer) return false;
        var materials = renderer.sharedMaterials;
        return materials.Any(material => material &&
            material.HasProperty("_Grass") && material.GetFloat("_Grass") >= 1f);
    }

    private static void ThickenTile(Terrain terrain, LawnProbe probe, Bounds[] garages, Report report)
    {
        var original = terrain.terrainData;
        var prototypes = original.detailPrototypes;
        if (prototypes.Length != 3 || prototypes.Any(prototype => !prototype.useInstancing || !prototype.prototype))
            throw new InvalidOperationException($"Unexpected detail prototypes on {terrain.name}.");
        int width = original.detailWidth, height = original.detailHeight;
        var layers = new int[prototypes.Length][,];
        var occupancy = new int[height, width];
        for (int i = 0; i < layers.Length; i++)
        {
            layers[i] = original.GetDetailLayer(0, 0, width, height, i);
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                int count = layers[i][z,x];
                occupancy[z,x] += count;
                report.originalTufts += count;
            }
        }

        Vector3 origin = terrain.transform.position;
        float dx = original.size.x / width, dz = original.size.z / height;
        var plantedArea = new int[height+1,width+1];
        for (int z = 0; z < height; z++)
        {
            int row = 0;
            for (int x = 0; x < width; x++)
            {
                if (occupancy[z,x] != 0) row++;
                plantedArea[z+1,x+1] = plantedArea[z,x+1] + row;
            }
        }
        int radius = Mathf.CeilToInt(3.6f / dx);
        for (int z = 1; z < height - 1; z++)
        for (int x = 1; x < width - 1; x++)
        {
            bool empty = occupancy[z,x] == 0;
            if (!empty && (occupancy[z,x] != 1 ||
                occupancy[z-1,x] == 0 || occupancy[z+1,x] == 0 ||
                occupancy[z,x-1] == 0 || occupancy[z,x+1] == 0))
                continue;
            float existingCover = 1f;
            if (empty)
            {
                int left=Mathf.Max(0,x-radius),right=Mathf.Min(width,x+radius+1);
                int bottom=Mathf.Max(0,z-radius),top=Mathf.Min(height,z+radius+1);
                int count=plantedArea[top,right]-plantedArea[top,left]-plantedArea[bottom,right]+plantedArea[bottom,left];
                existingCover=count/(float)((right-left)*(top-bottom));
                if (existingCover < .07f) continue;
            }

            float worldX = origin.x + (x + .5f) * dx, worldZ = origin.z + (z + .5f) * dz;
            float patch = Mathf.PerlinNoise(worldX * .54f + 29f, worldZ * .54f + 61f);
            float widePatch = Mathf.PerlinNoise(worldX * .19f + 19f, worldZ * .19f + 38f);
            float density = Mathf.SmoothStep(0f, .51f, Mathf.InverseLerp(.34f, .60f, patch * .6f + widePatch * .4f));
            if (empty)
                density *= Mathf.SmoothStep(.18f,.86f,Mathf.InverseLerp(.07f,.44f,existingCover));
            uint key = unchecked((uint)Mathf.RoundToInt(worldX * 8f) * 73856093u ^
                (uint)Mathf.RoundToInt(worldZ * 8f) * 19349663u ^ Seed);
            if (Random01(key) >= density) continue;
            if (empty && NearGarage(garages,worldX,worldZ)) continue;

            float detailY = origin.y + original.GetInterpolatedHeight((x + .5f) / width, (z + .5f) / height);
            if (!SafeSite(probe, worldX, worldZ, detailY))
            {
                report.protectedCells++;
                continue;
            }

            float colour = Mathf.Lerp(Random01(key ^ 195491u),
                Mathf.PerlinNoise(worldX * .31f + 79f, worldZ * .31f + 11f), .65f);
            int tone = colour < .44f ? 0 : colour < .68f ? 1 : 2;
            layers[tone][z,x]++;
            report.addedTones[tone]++;
            report.addedTufts++;
            if (empty) report.filledCells++;
        }

        string path = $"{AssetsFolder}/{terrain.name}.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (!data)
        {
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), path))
                throw new IOException($"Could not copy terrain detail data for {terrain.name}.");
            data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        }
        if (!data) throw new IOException($"Could not load revised terrain data for {terrain.name}.");
        for (int i = 0; i < prototypes.Length; i++)
        {
            prototypes[i].minHeight = 1.05f + .05f * i;
            prototypes[i].maxHeight = 1.65f + .06f * i;
        }
        data.detailPrototypes = prototypes;
        for (int i = 0; i < layers.Length; i++)
            data.SetDetailLayer(0, 0, i, layers[i]);
        terrain.terrainData = data;
        terrain.detailObjectDistance = DetailDistance;
        EditorUtility.SetDirty(data);
        report.terrainTiles++;
    }

    private static bool NearGarage(Bounds[] garages, float x, float z)
    {
        foreach (var garage in garages)
            if (x >= garage.min.x - .8f && x <= garage.max.x + .8f &&
                z >= garage.min.z - .8f && z <= garage.max.z + .8f) return true;
        return false;
    }

    private static bool SafeSite(LawnProbe probe, float x, float z, float detailY)
    {
        if (!probe.Height(x,z,out float level) || Mathf.Abs(level - detailY) > HeightTolerance)
            return false;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dz == 0) continue;
            if (!probe.Height(x + dx * BladeClearance,z + dz * BladeClearance,out float y) ||
                Mathf.Abs(y - level) > HeightTolerance) return false;
        }
        return true;
    }

    private static float Random01(uint input)
    {
        unchecked
        {
            input ^= input >> 16;
            input *= 0x7feb352du;
            input ^= input >> 15;
            input *= 0x846ca68bu;
            input ^= input >> 16;
            return (input & 0x00ffffffu) * (1f / 16777216f);
        }
    }

    [MenuItem("Street/Grass/Build district grass Windows game")]
    public static void Build()
    {
        const string path = "Build/StreetWindowsHDRPGrassCanopy/DuninaGorkavicha.exe";
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath, "Assets/Scenes/Street_LeafStudy.unity" },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = Path.GetFullPath(path)
        });
        if (result.summary.result != BuildResult.Succeeded) throw new IOException("Windows grass build failed.");
        Debug.Log("GRASS_CANOPY_BUILD " + path);
    }

    public static void ApplyAndBuild()
    {
        Apply();
        Build();
    }
}
