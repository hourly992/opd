using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class StreetAutumnGrassSurface
{
    private const string GroundGuid = "7e7ae6aebc64cfe4490ecf484669a6e8";
    private const string CourtGuid = "d0b09ee0afd727e44bba47649cd0971c";

    internal static void Apply(Scene scene, string assetFolder)
    {
        string textures = assetFolder + "/Turf/";
        var color = ImportMap(textures + "Grass005_Color.jpg", TextureImporterType.Default, true, TextureImporterFormat.BC7);
        var normal = ImportMap(textures + "Grass005_NormalGL.jpg", TextureImporterType.NormalMap, false, TextureImporterFormat.BC5);
        var mask = ImportMap(textures + "Grass005_Mask.png", TextureImporterType.Default, false, TextureImporterFormat.BC7);
        var grass = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(GroundGuid));
        var court = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(CourtGuid));
        if (!grass || !court)
            throw new InvalidOperationException("Expected Street lawn materials could not be located.");

        var groundFinish = Finish(grass, assetFolder + "/Lawn ground.mat", color, normal, mask, .47f, .38f);
        var courtFinish = Finish(court, assetFolder + "/Raised lawn ground.mat", color, normal, mask, .42f, .24f);
        int groundCount = 0, courtCount = 0;
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            if (renderer.gameObject.scene != scene)
                continue;
            var slots = renderer.sharedMaterials;
            bool dirty = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == grass)
                {
                    slots[i] = groundFinish;
                    groundCount++;
                    dirty = true;
                }
                else if (slots[i] == court)
                {
                    slots[i] = courtFinish;
                    courtCount++;
                    dirty = true;
                }
            }
            if (!dirty)
                continue;
            renderer.sharedMaterials = slots;
            EditorUtility.SetDirty(renderer);
        }

        if (groundCount == 0 || courtCount == 0)
            throw new InvalidOperationException("The new turf texture was not assigned to both lawn materials.");
        Debug.Log($"STREET_AUTUMN_TURF maps=Grass005 2048x2048 groundSlots={groundCount} courtSlots={courtCount}");
    }

    private static Material Finish(Material source, string path, Texture2D color, Texture2D normal,
        Texture2D mask, float bump, float dry)
    {
        var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!result)
        {
            result = new Material(source) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(result, path);
        }
        result.SetTexture("_MainTex", color);
        result.SetTexture("_BumpMap", normal);
        result.SetTexture("_MaskMap", mask);
        result.SetVector("_TileMeters", new Vector4(1.36f, 1.36f, 0f, 0f));
        result.SetColor("_Color", new Color(.94f, .91f, .73f));
        result.SetFloat("_Saturation", .63f);
        result.SetFloat("_BumpStrength", bump);
        result.SetFloat("_OcclusionStrength", .46f);
        result.SetFloat("_AlbedoContrast", 1.02f);
        result.SetFloat("_MeadowDrying", dry);
        result.SetFloat("_Parallax", .0003f);
        EditorUtility.SetDirty(result);
        return result;
    }

    private static Texture2D ImportMap(string path, TextureImporterType type, bool color, TextureImporterFormat format)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            throw new InvalidOperationException($"The Grass005 source map is missing: {path}.");
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        if (width != 2048 || height != 2048)
            throw new InvalidOperationException($"The Grass005 source map must be 2048 by 2048: {path}.");
        importer.textureType = type;
        importer.sRGBTexture = color;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = true;
        importer.mipmapFilter = color ? TextureImporterMipFilter.KaiserFilter : TextureImporterMipFilter.BoxFilter;
        importer.filterMode = FilterMode.Trilinear;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.anisoLevel = 16;
        importer.maxTextureSize = 2048;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Standalone",
            overridden = true,
            maxTextureSize = 2048,
            format = format,
            compressionQuality = 100
        });
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (!texture || texture.width != width || texture.height != height || texture.anisoLevel != 16)
            throw new InvalidOperationException($"Unexpected imported grass texture quality: {path}.");
        return texture;
    }
}
