using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GarageLawnMask
{
    private const string DenseLawnSource = "Assets/Art/AutumnGrass/DenseLawn/Tiles/";

    [Serializable]
    public sealed class Result
    {
        public int overlappingTiles;
        public int modifiedTiles;
        public int removedInstances;
        public int remainingInstances;
        public int retainedInstances;
        public string[] modifiedAssets;
    }

    public static Result Apply(Scene scene, Vector2[] footprint, string outputFolder)
    {
        if (!AssetDatabase.IsValidFolder(outputFolder))
            throw new ArgumentException("The garage asset output folder must already exist.", nameof(outputFolder));
        return CheckOrApply(scene, footprint, outputFolder);
    }

    public static Result Inspect(Scene scene, Vector2[] footprint) => CheckOrApply(scene, footprint, null);

    private static Result CheckOrApply(Scene scene, Vector2[] footprint, string outputFolder)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new ArgumentException("A loaded scene is required.", nameof(scene));
        Vector2[] polygon = GarageSurfaceCut.CanonicalFootprint(footprint);
        Rect bounds = GarageSurfaceCut.Bounds(polygon);

        var result = new Result();
        var modified = new List<string>();
        foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
        {
            if (terrain.gameObject.scene != scene || terrain.drawHeightmap || !terrain.terrainData)
                continue;

            var source = terrain.terrainData;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            bool original = sourcePath.StartsWith(DenseLawnSource, StringComparison.Ordinal);
            if (!original && !Path.GetFileNameWithoutExtension(sourcePath).StartsWith("GarageLawn_", StringComparison.Ordinal))
                continue;

            Vector3 position = terrain.transform.position;
            Vector3 size = source.size;
            var area = new Rect(position.x, position.z, size.x, size.z);
            if (!area.Overlaps(bounds))
                continue;

            result.overlappingTiles++;
            if (terrain.GetComponent<TerrainCollider>() || terrain.transform.lossyScale != Vector3.one ||
                terrain.transform.rotation != Quaternion.identity)
                throw new InvalidOperationException($"Unexpected transform or collider on detail-only tile {terrain.name}.");

            int width = source.detailWidth, height = source.detailHeight;
            if (width <= 0 || height <= 0)
                continue;

            float stepX = area.width / width, stepZ = area.height / height;
            // Include every cell whose scatter area intersects the pavement. Native
            // terrain detail placement can jitter away from the cell centre.
            int minX = Mathf.Clamp(Mathf.FloorToInt((bounds.xMin - area.xMin) / stepX), 0, width);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((bounds.xMax - area.xMin) / stepX), 0, width);
            int minZ = Mathf.Clamp(Mathf.FloorToInt((bounds.yMin - area.yMin) / stepZ), 0, height);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt((bounds.yMax - area.yMin) / stepZ), 0, height);
            var affected = new bool[maxZ - minZ, maxX - minX];
            for (int z = minZ; z < maxZ; z++)
            for (int x = minX; x < maxX; x++)
            {
                Vector2 center = new Vector2(position.x + (x + .5f) * stepX, position.z + (z + .5f) * stepZ);
                affected[z - minZ, x - minX] = IntersectsCell(polygon, center, .5f * stepX, .5f * stepZ);
            }
            int removed = 0;
            int layerCount = source.detailPrototypes.Length;
            var replacement = new int[layerCount][,];
            for (int layer = 0; layer < layerCount; layer++)
            {
                int[,] values = source.GetDetailLayer(0, 0, width, height, layer);
                replacement[layer] = values;
                for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    int count = values[z, x];
                    if (x >= minX && x < maxX && z >= minZ && z < maxZ && affected[z - minZ, x - minX])
                    {
                        removed += count;
                        values[z, x] = 0;
                    }
                    else result.retainedInstances += count;
                }
            }

            if (outputFolder == null)
            {
                result.remainingInstances += removed;
                continue;
            }
            if (removed == 0)
                continue;

            bool alreadyLocal = sourcePath.StartsWith(outputFolder + "/", StringComparison.Ordinal);
            string targetPath = alreadyLocal ? sourcePath :
                AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/GarageLawn_{terrain.name}.asset");
            if (!alreadyLocal && !AssetDatabase.CopyAsset(sourcePath, targetPath))
                throw new InvalidOperationException($"Could not copy lawn tile {sourcePath}.");
            var target = AssetDatabase.LoadAssetAtPath<TerrainData>(targetPath);
            if (!target)
                throw new InvalidOperationException($"Could not load lawn tile {targetPath}.");
            for (int layer = 0; layer < layerCount; layer++)
            {
                target.SetDetailLayer(0, 0, layer, replacement[layer]);
                int[,] actual = target.GetDetailLayer(0, 0, width, height, layer);
                for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    if (actual[z, x] != replacement[layer][z, x])
                        throw new InvalidOperationException($"Lawn detail data did not round-trip for {terrain.name}.");
            }

            Undo.RecordObject(terrain, "Align garage court lawn edges");
            terrain.terrainData = target;
            EditorUtility.SetDirty(terrain);
            EditorUtility.SetDirty(target);
            result.modifiedTiles++;
            result.removedInstances += removed;
            modified.Add(targetPath);
        }

        if (modified.Count > 0)
            EditorSceneManager.MarkSceneDirty(scene);
        result.modifiedAssets = modified.ToArray();
        return result;
    }

    private static bool IntersectsCell(Vector2[] polygon, Vector2 center, float halfX, float halfZ)
    {
        // Rect bounds above already reject the two cell axes. Check the
        // remaining separating axes, i.e. the normals of the court edges.
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 side = polygon[(i + 1) % polygon.Length] - polygon[i];
            Vector2 normal = new Vector2(-side.y, side.x);
            float extent = Mathf.Abs(normal.x) * halfX + Mathf.Abs(normal.y) * halfZ;
            if (Vector2.Dot(normal, center - polygon[i]) + extent <= 0f)
                return false;
        }
        return true;
    }
}
