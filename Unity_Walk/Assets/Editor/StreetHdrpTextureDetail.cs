using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class StreetHdrpTextureDetail
{
    private const string StreetScene = "Assets/Scenes/Street.unity";
    private const string AsphaltPrefix = "Assets/ThirdParty/ambientCG/Asphalt020S/";

    private static readonly HashSet<string> NativeFoliage = new(StringComparer.Ordinal)
    {
        "Assets/ThirdParty/PolyHaven/tree_small_02/Leaf.png",
        "Assets/ThirdParty/PolyHaven/tree_small_02/LeafNormal.png",
        "Assets/ThirdParty/PolyHaven/fir_tree_01/Sprig.png",
        "Assets/ThirdParty/PolyHaven/fir_tree_01/SprigNormal.png"
    };

    private static readonly HashSet<string> NativeAsphalt = new(StringComparer.Ordinal)
    {
        AsphaltPrefix + "AO_Roughness.png",
        AsphaltPrefix + "Asphalt020S_2K-JPG_NormalGL.jpg"
    };

    private static readonly string[] SurfaceSlots =
    {
        "_MainTex", "_BumpMap", "_MaskMap", "_GrassTex", "_DryGroundTex"
    };

    [Flags]
    private enum Adjustments
    {
        None = 0,
        Anisotropy = 1,
        ColourMipFilter = 2,
        SourceDimensions = 4,
        KeepNonPowerOfTwo = 8
    }

    [MenuItem("Street/HDRP/Audit texture detail")]
    public static void Audit()
    {
        foreach (var pair in FindStreetTextures())
            LogTexture(pair.Key, pair.Value);
    }

    [MenuItem("Street/HDRP/Apply texture detail")]
    public static void Apply()
    {
        SortedDictionary<string, Adjustments> paths = FindStreetTextures();
        int edited = 0;
        using (new AssetDatabase.AssetEditingScope())
        {
            foreach (var pair in paths)
            {
                if (AssetImporter.GetAtPath(pair.Key) is not TextureImporter importer)
                    continue;

                int editedBefore = edited;
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                if ((pair.Value & Adjustments.SourceDimensions) != 0)
                {
                    TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
                    TextureImporterPlatformSettings effective = standalone.overridden
                        ? standalone
                        : importer.GetDefaultPlatformTextureSettings();
                    int sourceCap = Mathf.NextPowerOfTwo(Mathf.Max(width, height));
                    if (effective.maxTextureSize < sourceCap)
                    {
                        if (!standalone.overridden)
                            effective.CopyTo(standalone);
                        standalone.name = "Standalone";
                        standalone.overridden = true;
                        standalone.maxTextureSize = sourceCap;
                        importer.SetPlatformTextureSettings(standalone);
                        edited++;
                    }
                }

                if ((pair.Value & Adjustments.KeepNonPowerOfTwo) != 0 &&
                    importer.npotScale != TextureImporterNPOTScale.None)
                {
                    importer.npotScale = TextureImporterNPOTScale.None;
                    edited++;
                }

                // Keep the authored alpha-coverage mips, mask channels and normal mips.
                if ((pair.Value & Adjustments.ColourMipFilter) != 0 &&
                    importer.mipmapEnabled && importer.sRGBTexture &&
                    importer.textureType == TextureImporterType.Default &&
                    (importer.alphaSource == TextureImporterAlphaSource.None || !importer.DoesSourceTextureHaveAlpha()) &&
                    Mathf.Max(width, height) >= 1024 &&
                    importer.mipmapFilter != TextureImporterMipFilter.KaiserFilter)
                {
                    importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
                    edited++;
                }

                if ((pair.Value & Adjustments.Anisotropy) != 0 && importer.mipmapEnabled &&
                    importer.anisoLevel < 16)
                {
                    importer.anisoLevel = 16;
                    edited++;
                }

                if (edited != editedBefore)
                {
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                }
            }
        }
        Debug.Log($"STREET_TEXTURE_DETAIL updated {edited} importer settings in {paths.Count} street texture candidates");
        foreach (var pair in paths)
            LogTexture(pair.Key, pair.Value);
    }

    private static SortedDictionary<string, Adjustments> FindStreetTextures()
    {
        // Only assets the existing street scene already uses for supported material inputs.
        var textures = new SortedDictionary<string, Adjustments>(StringComparer.Ordinal);
        string[] materialPaths = AssetDatabase.GetDependencies(StreetScene, true)
            .Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (string materialPath in materialPaths)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material || !material.shader)
                continue;
            string shader = material.shader.name;
            bool opaqueSurface = shader is "Street/TexturedSurface" or "Street/Asphalt020S"
                or "Street/PlaygroundSoil" or "Street/WoodlandGround";
            bool foliage = shader == "Street/Foliage";
            if (!opaqueSurface && !foliage)
                continue;

            foreach (string slot in SurfaceSlots)
            {
                if (!material.HasProperty(slot))
                    continue;
                Texture texture = material.GetTexture(slot);
                if (!texture)
                    continue;
                string path = AssetDatabase.GetAssetPath(texture);
                if (AssetImporter.GetAtPath(path) is not TextureImporter)
                    continue;

                Adjustments desired = Adjustments.None;
                if (opaqueSurface)
                    desired |= Adjustments.Anisotropy;
                if (opaqueSurface && slot == "_MainTex")
                    desired |= Adjustments.ColourMipFilter;
                if (NativeAsphalt.Contains(path))
                    desired |= Adjustments.SourceDimensions;
                if (foliage && NativeFoliage.Contains(path))
                    desired |= Adjustments.KeepNonPowerOfTwo;

                if (desired != Adjustments.None)
                {
                    textures.TryGetValue(path, out Adjustments existing);
                    textures[path] = existing | desired;
                }
            }
        }

        return textures;
    }

    private static void LogTexture(string path, Adjustments desired)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
        int cap = (standalone.overridden ? standalone : importer.GetDefaultPlatformTextureSettings()).maxTextureSize;
        string actual = texture
            ? $"{texture.width}x{texture.height} {texture.graphicsFormat} mips={texture.mipmapCount}"
            : "no Texture2D";
        Debug.Log($"STREET_TEXTURE_DETAIL {path} | source={width}x{height} | platformCap={cap} | " +
            $"{actual} aniso={importer.anisoLevel} filter={importer.mipmapFilter} npot={importer.npotScale} | {desired}");
    }
}

