using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

internal static class DistrictAutumnStyle
{
    internal const string Root = "Assets/PhotoReconstruction/Stage13Style";

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before applying the autumn finish.");
        Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.Refresh();
        var sun = RenderSettings.sun;
        var sky = RenderSettings.skybox;
        if (!sun || !sky || !sky.HasProperty("_SoftDaylight"))
            throw new InvalidOperationException("The saved directional sun and autumn sky are required.");

        sun.transform.rotation = Quaternion.Euler(37f, 60f, 0);
        sun.color = new Color(1, .936f, .823f);
        sun.intensity = 1.04f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .95f;
        sun.shadowNormalBias = .25f;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.80f, .83f, .87f);
        RenderSettings.ambientEquatorColor = new Color(.77f, .74f, .70f);
        RenderSettings.ambientGroundColor = new Color(.61f, .57f, .51f);
        RenderSettings.fogColor = new Color(.63f, .70f, .72f);

        var softSky = CopyMaterial(sky, "SoftAutumnDaylight");
        softSky.SetFloat("_SoftDaylight", .82f);
        softSky.SetFloat("_Exposure", .89f);
        softSky.SetVector("_SunDirection", -sun.transform.forward);
        EditorUtility.SetDirty(softSky);
        RenderSettings.skybox = softSky;

        foreach (var presentation in UnityEngine.Object.FindObjectsByType<StreetPresentation>(FindObjectsInactive.Include))
        {
            if (presentation.gameObject.scene != scene) continue;
            presentation.contactShadows = .36f;
            presentation.contactRadius = .40f;
            presentation.sunShafts = .14f;
            presentation.exposure = 1.075f;
            presentation.saturation = 1.005f;
            presentation.bloom = .125f;
            presentation.sharpness = .022f;
            presentation.autumnGrade = .88f;
            EditorUtility.SetDirty(presentation);
        }
        StreetAutumnLitter.ApplyMargins(Root);
        int foliage = FinishFoliage();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"STAGE13_AUTUMN sunElevation=37 contactRadius=.40 foliageMaterials={foliage}");
    }

    private static int FinishFoliage()
    {
        var palette = new Dictionary<Material, Material>();
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source || source.shader.name != "Street/Foliage" ||
                    source.name is not ("ModelLeaves" or "BirchLeaves" or "HedgeLeaves" or "LawnBlades") ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId) ||
                    AssetDatabase.GetAssetPath(source).StartsWith(Root + "/", StringComparison.Ordinal))
                    continue;
                if (!palette.TryGetValue(source, out var style))
                {
                    style = CopyMaterial(source, $"{source.name}_{guid}_{localId}");
                    if (source.name == "LawnBlades")
                        style.SetColor("_Color", new Color(.385f, .359f, .224f));
                    else
                    {
                        bool hedge = source.name == "HedgeLeaves";
                        style.SetFloat("_AutumnAmount", hedge ? .42f : .96f);
                        style.SetColor("_AutumnGold", hedge ? new Color(.45f, .49f, .24f) : new Color(.84f, .58f, .14f));
                        style.SetColor("_AutumnCopper", hedge ? new Color(.44f, .35f, .17f) : new Color(.69f, .30f, .105f));
                        style.SetFloat("_Translucency", .22f);
                    }
                    style.enableInstancing = source.enableInstancing;
                    EditorUtility.SetDirty(style);
                    palette.Add(source, style);
                }
                materials[i] = style;
                changed = true;
            }
            if (changed)
            {
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
            }
        }
        return palette.Count;
    }

    internal static Material CopyMaterial(Material source, string assetName)
    {
        string path = $"{Root}/Materials/{assetName}.mat";
        var saved = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (saved) return saved;
        saved = new Material(source) { name = source.name };
        AssetDatabase.CreateAsset(saved, path);
        return saved;
    }
}
