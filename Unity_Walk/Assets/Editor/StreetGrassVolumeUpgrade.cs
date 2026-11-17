using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class StreetGrassVolumeUpgrade
{
    private const string Folder = "Assets/Art/AutumnGrass/DenseLawn";
    private const int StrandCount = 7;

    [Serializable]
    public sealed class Result
    {
        public int meshes, detailTiles, oldStrandsPerTuft = 3, newStrandsPerTuft = StrandCount;
        public int oldTrianglesPerTuft = 6, newTrianglesPerTuft = 23;
        public float minBladeHeightMetres, maxBladeHeightMetres, windStrengthMetres = .011f;
        public bool densityMapsPreserved, prototypeInstancesPreserved;
    }

    // Changes the three reusable detail meshes; the terrain masks and native root placements stay intact.
    public static Result Apply(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new ArgumentException("A loaded city scene is required.");

        string[] tiles = AssetDatabase.FindAssets("t:TerrainData", new[] { Folder + "/Tiles" })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (tiles.Length != 43)
            throw new InvalidOperationException($"Expected 43 saved lawn masks, found {tiles.Length}.");
        string[] maskHashes = tiles.Select(FileHash).ToArray();

        var result = new Result { minBladeHeightMetres = float.PositiveInfinity };
        Color[] palette =
        {
            new(.145f, .199f, .061f),
            new(.185f, .215f, .074f),
            new(.245f, .235f, .087f)
        };
        for (int variant = 0; variant < palette.Length; variant++)
        {
            string path = $"{Folder}/Lawn tuft {variant + 1}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing || existing.subMeshCount != 1)
                throw new InvalidOperationException($"The saved lawn prototype is missing at {path}.");
            var replacement = MakeTuft(existing.name, palette[variant], variant, result);
            EditorUtility.CopySerialized(replacement, existing);
            UnityEngine.Object.DestroyImmediate(replacement);
            EditorUtility.SetDirty(existing);
            result.meshes++;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/Lawn HDRP Instanced.mat");
        if (!material || !material.enableInstancing || material.shader.name != "Street/Foliage")
            throw new InvalidOperationException("The existing instanced HDRP grass material is missing.");
        material.SetFloat("_WindStrength", result.windStrengthMetres);
        material.SetFloat("_MeadowKeep", 1f);
        material.enableInstancing = true;
        material.SetShaderPassEnabled("MotionVectors", true);
        EditorUtility.SetDirty(material);

        result.detailTiles = UnityEngine.Object.FindObjectsByType<Terrain>()
            .Count(terrain => terrain.gameObject.scene == scene && terrain.terrainData &&
                AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith(Folder + "/Tiles/", StringComparison.Ordinal));
        // Native masks and prototype distributions are untouched by this operation.
        AssetDatabase.SaveAssets();
        result.densityMapsPreserved = tiles.Select(FileHash).SequenceEqual(maskHashes);
        result.prototypeInstancesPreserved = result.densityMapsPreserved;
        if (!result.densityMapsPreserved)
            throw new InvalidOperationException("The grass volume edit unexpectedly changed a native terrain mask.");
        Directory.CreateDirectory("QA_HDRP");
        File.WriteAllText("QA_HDRP/grass-volume-upgrade.json", JsonUtility.ToJson(result, true));
        Debug.Log($"STREET_GRASS_VOLUME meshes={result.meshes} nativeTiles={result.detailTiles} " +
            $"bladesPerTuft={StrandCount} trianglesPerTuft=23 densityMapsPreserved={result.densityMapsPreserved} " +
            $"heightRange={result.minBladeHeightMetres:F3}..{result.maxBladeHeightMetres:F3}m");
        return result;
    }

    private static string FileHash(string path)
    {
        using var file = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
    }

    private static Mesh MakeTuft(string name, Color palette, int variant, Result result)
    {
        var positions = new List<Vector3>(36);
        var normals = new List<Vector3>(36);
        var tangents = new List<Vector4>(36);
        var colors = new List<Color>(36);
        var uv = new List<Vector4>(36);
        var triangles = new List<int>(69);
        var random = new System.Random(919 + variant * 173);
        for (int strand = 0; strand < StrandCount; strand++)
        {
            float angle = variant * .43f + strand * 2.39996f + RandomRange(random, -.35f, .35f);
            Vector3 facing = new(-Mathf.Sin(angle), 0, Mathf.Cos(angle));
            Vector3 side = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 upNormal = (facing * .78f + Vector3.up * 1.08f).normalized;
            float height = RandomRange(random, .061f, .106f) * (variant == 2 ? .97f : 1f);
            float width = RandomRange(random, .0055f, .0085f);
            float lean = height * RandomRange(random, .22f, .49f);
            float rootRadius = RandomRange(random, .008f, .032f);
            Vector3 root = facing * rootRadius - Vector3.up * .006f;
            Vector3 middle = root + Vector3.up * (height * .57f) + facing * (lean * .31f);
            Vector3 tip = root + Vector3.up * height + facing * lean;
            Vector3 spread = side * (width * .64f);
            Color tone = palette * RandomRange(random, .88f, 1.11f);

            int start = positions.Count;
            Add(root - side * width, upNormal, tone * .70f, new Vector4(0, 0, 0, 0));
            Add(root + side * width, upNormal, tone * .77f, new Vector4(1, 0, 0, 0));
            Add(middle - spread, (upNormal - side * .17f).normalized, tone * .92f, new Vector4(0, .57f, 0, 0));
            Add(middle + spread, (upNormal + side * .17f).normalized, tone * 1.02f, new Vector4(1, .57f, 0, 0));
            Add(tip, upNormal, tone * 1.12f, new Vector4(.5f, 1, 0, 0));
            if (strand == 0)
            {
                Add(middle + facing * width * .53f, (upNormal + facing * .13f).normalized,
                    tone * 1.07f, new Vector4(.5f, .57f, 0, 0));
                Face(start, start + 1, start + 5, upNormal);
                Face(start, start + 5, start + 2, upNormal);
                Face(start + 1, start + 3, start + 5, upNormal);
                Face(start + 2, start + 5, start + 4, upNormal);
                Face(start + 5, start + 3, start + 4, upNormal);
            }
            else
            {
                Face(start, start + 1, start + 3, upNormal);
                Face(start, start + 3, start + 2, upNormal);
                Face(start + 2, start + 3, start + 4, upNormal);
            }
            result.minBladeHeightMetres = Mathf.Min(result.minBladeHeightMetres, height);
            result.maxBladeHeightMetres = Mathf.Max(result.maxBladeHeightMetres, height);

            void Add(Vector3 position, Vector3 normal, Color tint, Vector4 coordinates)
            {
                positions.Add(position);
                normals.Add(normal);
                tangents.Add(new Vector4(side.x, side.y, side.z, -1));
                colors.Add(new Color(tint.r, tint.g, tint.b, 1));
                uv.Add(coordinates);
            }
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetTangents(tangents);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        var padded = mesh.bounds;
        padded.Expand(.03f);
        mesh.bounds = padded;
        if (mesh.vertexCount != 36 || mesh.triangles.Length != 69)
            throw new InvalidOperationException("The compact seven-blade mesh has unexpected topology.");
        return mesh;

        void Face(int a, int b, int c, Vector3 expected)
        {
            bool reverse = Vector3.Dot(Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]), expected) < 0;
            triangles.Add(a);
            triangles.Add(reverse ? c : b);
            triangles.Add(reverse ? b : c);
        }
    }

    private static float RandomRange(System.Random random, float min, float max) =>
        Mathf.Lerp(min, max, (float)random.NextDouble());
}
