using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class StreetHdrpMaterials
{
    private static readonly string[] StreetShaders =
    {
        "Street/TexturedSurface", "Street/Surface", "Street/Asphalt020S", "Street/Foliage",
        "Street/Glass", "Street/PlaygroundSoil", "Street/WoodlandGround", "Street/PaintedBackdrop",
        "Street/GarageCanopy", "Street/FallenLeaves"
    };

    private static readonly Action<Material> ValidateLit = CreateLitValidator();

    [MenuItem("Street/HDRP/Migrate material assets")]
    public static void Migrate()
    {
        if (!(GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset))
            throw new InvalidOperationException("Assign an HDRP pipeline before migrating materials.");

        Type upgraderType = typeof(HDShaderUtils).Assembly.GetType(
            "UnityEditor.Rendering.HighDefinition.StandardsToHDLitMaterialUpgrader", true);
        var standardUpgrader = (MaterialUpgrader)Activator.CreateInstance(
            upgraderType, "Standard", "HDRP/Lit", null);

        foreach (string shaderName in StreetShaders)
        {
            Shader shader = Shader.Find(shaderName);
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException($"HDRP street shader failed to import: {shaderName}");
        }

        var migrated = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material || !material.shader) continue;
            string shaderName = material.shader.name;
            if (shaderName == "Standard")
            {
                standardUpgrader.Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
                EditorUtility.SetDirty(material);
            }
            else if (StreetShaders.Contains(shaderName))
            {
                ConfigureStreetMaterial(material, shaderName);
                EditorUtility.SetDirty(material);
            }
            else
            {
                continue;
            }

            migrated.TryGetValue(shaderName, out int count);
            migrated[shaderName] = count + 1;
        }

        MigrateComparisonModelMaterials();
        AssetDatabase.SaveAssets();
        foreach (var group in migrated)
            Debug.Log($"Street HDRP materials: {group.Key} = {group.Value}");
    }

    private static void ConfigureStreetMaterial(Material material, string shaderName)
    {
        bool foliage = shaderName == "Street/Foliage";
        bool leaves = shaderName == "Street/FallenLeaves";
        bool canopy = shaderName == "Street/GarageCanopy";
        bool soil = shaderName == "Street/PlaygroundSoil";
        bool roadMarking = shaderName == "Street/TexturedSurface" &&
            Mathf.Abs(material.GetFloat("_SurfaceClass") - 5f) < .1f;
        bool cutout = foliage || leaves || soil || roadMarking;
        bool doubleSided = foliage || leaves || canopy ||
            (material.HasProperty("_Cull") && material.GetFloat("_Cull") == (float)CullMode.Off);

        material.shaderKeywords = Array.Empty<string>();
        material.SetOverrideTag("RenderType", "");
        material.SetFloat("_SurfaceType", canopy ? 1f : 0f);
        material.SetFloat("_AlphaCutoffEnable", cutout ? 1f : 0f);
        material.SetFloat("_AlphaCutoff", foliage ? material.GetFloat("_Cutoff") : .45f);
        material.SetFloat("_DoubleSidedEnable", doubleSided ? 1f : 0f);
        material.SetFloat("_DoubleSidedNormalMode", doubleSided ? 0f : 1f);
        material.SetFloat("_EnableBlendModePreserveSpecularLighting", canopy ? 0f : 1f);
        material.SetFloat("_RayTracing", 1f);
        material.renderQueue = canopy ? (int)RenderQueue.Transparent :
            cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
        ValidateStreetMaterial(material);
    }

    private static Action<Material> CreateLitValidator()
    {
        Type litApi = typeof(HDRenderPipelineAsset).Assembly.GetType(
            "UnityEngine.Rendering.HighDefinition.LitAPI", true);
        return (Action<Material>)Delegate.CreateDelegate(typeof(Action<Material>),
            litApi.GetMethod("ValidateMaterial", BindingFlags.Static | BindingFlags.NonPublic));
    }

    internal static void ValidateStreetMaterial(Material material)
    {
        // HDRP uses these compatibility slots to transfer alpha to the built-in
        // lightmapper. The street shaders also read them as original art inputs.
        var originalTexture = material.GetTexture("_MainTex");
        Vector2 originalScale = material.GetTextureScale("_MainTex");
        Vector2 originalOffset = material.GetTextureOffset("_MainTex");
        Color originalTint = material.GetColor("_Color");
        Color originalEmission = material.GetColor("_EmissionColor");
        float originalCutoff = material.GetFloat("_Cutoff");

        try
        {
            ValidateLit(material);
        }
        finally
        {
            material.SetTexture("_MainTex", originalTexture);
            material.SetTextureScale("_MainTex", originalScale);
            material.SetTextureOffset("_MainTex", originalOffset);
            material.SetColor("_Color", originalTint);
            material.SetColor("_EmissionColor", originalEmission);
            material.SetFloat("_Cutoff", originalCutoff);
        }

        // These street shaders decode the source AO, roughness and height channels.
        material.DisableKeyword("_MASKMAP");
    }

    private static void MigrateComparisonModelMaterials()
    {
        const string modelPath = "Assets/BlosmComparison/Dunina_Blosm.fbx";
        if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
            return;

        Material[] embedded = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<Material>().Where(material => material.shader && material.shader.name == "Standard").ToArray();
        if (embedded.Length == 0)
            return;

        const string parent = "Assets/BlosmComparison";
        const string folder = parent + "/HDRP";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(parent, "HDRP");

        Shader lit = Shader.Find("HDRP/Lit");
        foreach (Material source in embedded)
        {
            var identifier = new AssetImporter.SourceAssetIdentifier(source);
            Material destination = new Material(lit) { name = source.name };
            destination.SetColor("_BaseColor", source.color);
            if (source.mainTexture)
            {
                destination.SetTexture("_BaseColorMap", source.mainTexture);
                destination.SetTextureScale("_BaseColorMap", source.mainTextureScale);
                destination.SetTextureOffset("_BaseColorMap", source.mainTextureOffset);
            }
            destination.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            destination.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            HDMaterial.ValidateMaterial(destination);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.name}.mat");
            AssetDatabase.CreateAsset(destination, assetPath);
            importer.AddRemap(identifier, destination);
        }
        importer.SaveAndReimport();
        Debug.Log($"Street HDRP comparison model material remaps: {embedded.Length}");
    }
}

public sealed class StreetHdrpMaterialGUI : ShaderGUI
{
    public override void ValidateMaterial(Material material)
    {
        StreetHdrpMaterials.ValidateStreetMaterial(material);
    }
}

