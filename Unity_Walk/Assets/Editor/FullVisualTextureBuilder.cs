using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

// Deterministic project-authored, periodic PBR maps. Source scans are kept separately.
internal static class FullVisualTextureBuilder
{
    internal const string Root = "Assets/Art/FullVisual/Textures";
    private const int Size = 1024;
    private const string Revision = "OPD concrete and stucco surface library 2";

    [Serializable] private sealed class Manifest
    {
        public string author = "OPD project";
        public string generator = "Assets/Editor/FullVisualTextureBuilder.cs";
        public string revision = Revision;
        public string terms = "Project-authored source and textures. Same terms as the OPD project.";
        public string description = "Periodic concrete and stucco surfaces authored from mathematical aggregate, pores and trowel profiles. No copied artwork.";
        public string channels = "Albedo RGB sRGB. Normal RGB tangent-space OpenGL. Mask RGB = AO, roughness, height in linear data.";
        public int textureSize = Size;
        public MapFile[] files;
    }

    [Serializable] private sealed class MapFile
    {
        public string file, sha256;
    }

    private struct Pixel
    {
        internal Color color;
        internal float height, roughness, ao;
    }

    internal static void Prepare()
    {
        Directory.CreateDirectory(Root);
        string manifestPath = Root + "/PROVENANCE.json";
        bool fresh = File.Exists(manifestPath) && File.ReadAllText(manifestPath).Contains(Revision);
        var files = new List<MapFile>();
        foreach (string family in new[] { "CurbConcrete", "SoftStucco" })
        {
            string folder = Root + "/" + family;
            Directory.CreateDirectory(folder);
            if (!fresh || !File.Exists(folder + "/Albedo.png") || !File.Exists(folder + "/Normal.png") ||
                !File.Exists(folder + "/Mask.png"))
                Generate(family, folder);
            foreach (string map in new[] { "Albedo", "Normal", "Mask" })
            {
                string path = folder + "/" + map + ".png";
                Configure(path, map);
                using var hash = SHA256.Create();
                using var stream = File.OpenRead(path);
                files.Add(new MapFile
                {
                    file = family + "/" + map + ".png",
                    sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant()
                });
            }
        }
        File.WriteAllText(manifestPath, JsonUtility.ToJson(new Manifest { files = files.ToArray() }, true));
        AssetDatabase.ImportAsset(manifestPath);
    }

    private static void Generate(string family, string folder)
    {
        var albedo = new Color32[Size * Size];
        var mask = new Color32[albedo.Length];
        var normal = new Color32[albedo.Length];
        var heights = new float[albedo.Length];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int i = y * Size + x;
            Pixel pixel = Sample(family, (x + .5f) / Size, (y + .5f) / Size);
            albedo[i] = pixel.color;
            mask[i] = new Color(pixel.ao, pixel.roughness, pixel.height, 1);
            heights[i] = pixel.height;
        }
        const float relief = .0011f;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = heights[y * Size + (x + 1) % Size] - heights[y * Size + (x + Size - 1) % Size];
            float dy = heights[((y + 1) % Size) * Size + x] - heights[((y + Size - 1) % Size) * Size + x];
            Vector3 n = new Vector3(-dx * Size * relief, -dy * Size * relief, 2).normalized;
            normal[y * Size + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
        }
        Write(folder + "/Albedo.png", albedo);
        Write(folder + "/Normal.png", normal);
        Write(folder + "/Mask.png", mask);
        Debug.Log("FULL_VISUAL_TEXTURE family=" + family + " size=" + Size + " maps=3");
    }

    private static Pixel Sample(string family, float u, float v)
    {
        float cloud = Noise(u, v, 5, 5, 19);
        float medium = Noise(u, v, 22, 22, 107);
        float grain = Noise(u, v, 116, 116, 73);
        float roughness = .87f + (grain - .5f) * .10f;
        float height, brightness;
        Color tint;
        switch (family)
        {
            case "CurbConcrete":
                float pores = Mathf.Pow(Mathf.Clamp01((Noise(u, v, 82, 82, 43) - .68f) * 3.125f), 2);
                height = .53f + (medium - .5f) * .13f + (grain - .5f) * .07f - pores * .14f;
                brightness = .69f + (cloud - .5f) * .055f + (medium - .5f) * .045f - pores * .08f;
                tint = new Color(1, .985f, .953f);
                break;
            case "SoftStucco":
                float trowel = Noise(u, v, 19, 4, 231) * .62f + Noise(u, v, 7, 24, 311) * .38f;
                height = .50f + (trowel - .5f) * .15f + (grain - .5f) * .055f;
                brightness = .68f + (cloud - .5f) * .07f + (trowel - .5f) * .038f;
                tint = new Color(1, .991f, .967f);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown authored surface family.");
        }
        return new Pixel
        {
            color = new Color(brightness * tint.r, brightness * tint.g, brightness * tint.b, 1),
            height = Mathf.Clamp01(height),
            roughness = Mathf.Clamp01(roughness),
            ao = Mathf.Clamp01(.96f - Mathf.Max(0, .49f - height) * .36f)
        };
    }

    private static float Noise(float u, float v, int width, int height, int seed)
    {
        float x = u * width, y = v * height;
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float tx = x - ix, ty = y - iy;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        return Mathf.Lerp(Mathf.Lerp(Hash(ix % width, iy % height, seed), Hash((ix + 1) % width, iy % height, seed), tx),
            Mathf.Lerp(Hash(ix % width, (iy + 1) % height, seed), Hash((ix + 1) % width, (iy + 1) % height, seed), tx), ty);
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }

    private static void Write(string path, Color32[] colors)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        texture.SetPixels32(colors);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
    }

    private static void Configure(string path, string map)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var type = map == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType == type && importer.sRGBTexture == (map == "Albedo") &&
            importer.mipmapEnabled && importer.anisoLevel == 8 && importer.filterMode == FilterMode.Trilinear &&
            importer.textureCompression == TextureImporterCompression.CompressedHQ && importer.maxTextureSize == Size)
            return;
        importer.textureType = type;
        importer.sRGBTexture = map == "Albedo";
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.anisoLevel = 8;
        importer.filterMode = FilterMode.Trilinear;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = Size;
        importer.SaveAndReimport();
    }
}
