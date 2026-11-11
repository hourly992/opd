using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class StreetAutumnFinish
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string MaterialRoot = "Assets/PhotoReconstruction/Stage08Autumn/Materials";

    public static void ApplyAndBuild()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save open scenes before applying the autumn style.");

        const string backup = "../.local-backups/before_autumn_style_20261106";
        Directory.CreateDirectory(backup);
        if (!File.Exists(backup + "/Street.unity"))
            File.Copy(ScenePath, backup + "/Street.unity");

        var scene = EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(MaterialRoot);
        AssetDatabase.Refresh();
        ApplyLighting(scene);
        ApplyMaterials(scene);
        StreetAutumnLitter.Apply();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("The autumn Street scene could not be saved.");
        AssetDatabase.SaveAssets();
        StreetBuilder.BuildGeneratedScene();
    }

    private static void ApplyLighting(Scene scene)
    {
        var sun = RenderSettings.sun;
        var oldSky = RenderSettings.skybox;
        var skyShader = Shader.Find("Skybox/StreetAutumn");
        if (!sun || !oldSky || !skyShader)
            throw new InvalidOperationException("The saved sunlight and autumn sky shader are required.");

        sun.transform.rotation = Quaternion.Euler(34, 58, 0);
        sun.color = new Color(1, .91f, .77f);
        sun.intensity = 1.13f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .97f;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.76f, .77f, .78f);
        RenderSettings.ambientEquatorColor = new Color(.67f, .65f, .63f);
        RenderSettings.ambientGroundColor = new Color(.49f, .44f, .39f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = .00235f;
        RenderSettings.fogColor = new Color(.69f, .64f, .55f);

        const string skyPath = MaterialRoot + "/AutumnSky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (!sky)
        {
            sky = new Material(skyShader);
            sky.SetTexture("_MainTex", oldSky.GetTexture("_MainTex"));
            sky.SetFloat("_Rotation", oldSky.GetFloat("_Rotation"));
            AssetDatabase.CreateAsset(sky, skyPath);
        }
        sky.SetFloat("_Exposure", .84f);
        sky.SetVector("_SunDirection", -sun.transform.forward);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;

        foreach (var presentation in UnityEngine.Object.FindObjectsByType<StreetPresentation>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (presentation.gameObject.scene != scene)
                continue;
            presentation.contactShadows = .31f;
            presentation.contactRadius = .46f;
            presentation.sunShafts = .095f;
            presentation.exposure = 1.08f;
            presentation.saturation = 1.02f;
            presentation.bloom = .13f;
            presentation.sharpness = .035f;
            EditorUtility.SetDirty(presentation);
        }
    }

    private static void ApplyMaterials(Scene scene)
    {
        var replacements = new Dictionary<Material, Material>();
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (renderer.gameObject.scene != scene)
                continue;
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source || !source.shader || !IsStyled(source))
                    continue;
                string path = AssetDatabase.GetAssetPath(source);
                if (path.StartsWith(MaterialRoot + "/", StringComparison.Ordinal))
                    continue;

                if (!replacements.TryGetValue(source, out var replacement))
                {
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    string target = $"{MaterialRoot}/{source.name}_{guid}.mat";
                    replacement = AssetDatabase.LoadAssetAtPath<Material>(target);
                    if (replacement)
                        EditorUtility.CopySerialized(source, replacement);
                    else
                    {
                        replacement = new Material(source);
                        AssetDatabase.CreateAsset(replacement, target);
                    }
                    replacement.name = source.name;
                    Style(replacement);
                    EditorUtility.SetDirty(replacement);
                    replacements.Add(source, replacement);
                }
                materials[i] = replacement;
                changed = true;
            }
            if (changed)
            {
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }
        Debug.Log($"STREET_AUTUMN_STYLE materialVariants={replacements.Count}");
    }

    private static bool IsStyled(Material material)
    {
        if (material.shader.name == "Street/Foliage")
            return material.name == "ModelLeaves" || material.name == "BirchLeaves" ||
                   material.name == "HedgeLeaves" || material.name == "LawnBlades" ||
                   material.name == "Leaf0" || material.name == "Leaf1";
        return material.shader.name == "Street/TexturedSurface" || material.shader.name == "Street/Glass";
    }

    private static void Style(Material material)
    {
        if (material.shader.name == "Street/Glass")
        {
            material.SetFloat("_ReflectionStrength", material.GetFloat("_ReflectionStrength") * .53f);
            material.SetFloat("_InteriorDetail", .12f);
            return;
        }
        if (material.shader.name == "Street/Foliage")
        {
            if (material.name == "LawnBlades")
            {
                material.SetColor("_Color", new Color(.32f, .29f, .14f));
                return;
            }
            bool hedge = material.name == "HedgeLeaves";
            material.SetFloat("_AutumnAmount", hedge ? .48f : .97f);
            material.SetColor("_AutumnGold", hedge ? new Color(.47f,.49f,.15f) : new Color(.88f,.60f,.10f));
            material.SetColor("_AutumnCopper", hedge ? new Color(.45f,.36f,.13f) : new Color(.77f,.29f,.062f));
            material.SetFloat("_Translucency", .19f);
            return;
        }

        float surface = material.GetFloat("_SurfaceClass");
        if (surface > 4.5f && surface < 5.5f)
            return;
        material.SetFloat("_BumpStrength", material.GetFloat("_BumpStrength") * .65f);
        material.SetFloat("_Parallax", material.GetFloat("_Parallax") * .42f);
        material.SetFloat("_Weathering", material.GetFloat("_Weathering") * .61f);
        material.SetFloat("_AlbedoContrast", material.GetFloat("_AlbedoContrast") * .72f);
        material.SetFloat("_RoughnessBias", Mathf.Max(.11f, material.GetFloat("_RoughnessBias")));

        if (material.GetFloat("_MeadowDrying") > 0)
        {
            bool court = material.name == "CourtMaintainedGrass";
            material.SetColor("_Color", court ? new Color(.62f,.64f,.40f) : new Color(.61f,.58f,.36f));
            material.SetVector("_MeadowSoilRange", new Vector4(.052f,.103f,.43f,.70f));
        }
        else if (surface > 1.5f && surface < 2.5f)
        {
            Color tint = material.GetColor("_Color");
            material.SetColor("_Color", new Color(tint.r * 1.07f, tint.g * 1.01f, tint.b * .88f, tint.a));
        }
        else if (material.name == "CourtGreyRectangularPavers")
        {
            material.SetColor("_Color", new Color(.68f,.65f,.56f));
        }
    }
}
