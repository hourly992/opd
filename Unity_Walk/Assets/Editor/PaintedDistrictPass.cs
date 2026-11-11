using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class PaintedDistrictPass
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string Lighting = "Assets/Art/PaintedDistrict/Lighting";

    [MenuItem("Street/Apply painted district to the saved scene")]
    public static void Apply()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scenes before running the district pass.");

        var scene = EditorSceneManager.OpenScene(ScenePath);
        int buildings = Object.FindObjectsByType<StreetBuilding>().Length;
        if (buildings != 18 || !Object.FindAnyObjectByType<PilotBlock>())
            throw new InvalidOperationException("The saved district must include all 19 buildings.");
        string layout = FullVisualAudit.LayoutHash();
        PaintedDistrictSurfaces.Apply();
        PaintedDistrictEdges.Apply();
        Physics.SyncTransforms();
        PaintedDistrictLeaves.Apply();
        ApplyLighting();
        Physics.SyncTransforms();
        if (FullVisualAudit.LayoutHash() != layout)
            throw new InvalidOperationException("The district building or route layout changed.");
        PaintedProfileAudit.Verify();
        DistrictSurfaceAuditStage04.Check();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Could not save the painted district.");
        AssetDatabase.SaveAssets();
        Debug.Log($"PAINTED_DISTRICT_SAVED buildings={buildings + 1} layout={layout}");
    }

    public static void ApplyAndBuild()
    {
        Apply();
        StreetBuilder.BuildGeneratedScene();
    }

    private static void ApplyLighting()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sun = RenderSettings.sun;
        if (!sun || !RenderSettings.skybox)
            throw new InvalidOperationException("The saved scene needs its sun and sky.");

        Directory.CreateDirectory(Lighting);
        AssetDatabase.Refresh();
        sun.transform.rotation = Quaternion.Euler(38, 59, 0);
        sun.color = new Color(1, .951f, .854f);
        sun.intensity = 1.16f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .86f;
        sun.shadowNormalBias = .23f;
        EditorUtility.SetDirty(sun);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.76f, .80f, .84f);
        RenderSettings.ambientEquatorColor = new Color(.75f, .71f, .63f);
        RenderSettings.ambientGroundColor = new Color(.53f, .46f, .36f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = .0020f;
        RenderSettings.fogColor = new Color(.61f, .74f, .82f);

        var sky = LightingVariant(RenderSettings.skybox, Lighting + "/BlueAutumnSky.mat");
        sky.SetFloat("_PaintedDistrictSky", 1);
        sky.SetFloat("_Exposure", 1);
        sky.SetVector("_SunDirection", -sun.transform.forward);
        RenderSettings.skybox = sky;
        EditorUtility.SetDirty(sky);

        var glassVariants = new System.Collections.Generic.Dictionary<Material, Material>();
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            if (renderer.gameObject.scene != scene) continue;
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source || source.shader.name != "Street/Glass") continue;
                if (!glassVariants.TryGetValue(source, out var glass))
                {
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id))
                        throw new InvalidOperationException("Street window glass must have an asset path.");
                    glass = LightingVariant(source, $"{Lighting}/Glass_{guid}_{id}.mat");
                    glass.SetFloat("_SkyPaintedDistrict", 1);
                    glass.SetFloat("_SkyExposure", sky.GetFloat("_Exposure"));
                    glass.SetVector("_SkySunDirection", sky.GetVector("_SunDirection"));
                    EditorUtility.SetDirty(glass);
                    glassVariants.Add(source, glass);
                }
                materials[i] = glass;
                changed = true;
            }
            if (!changed) continue;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        var presentations = Object.FindObjectsByType<StreetPresentation>(FindObjectsInactive.Include)
            .Where(value => value.gameObject.scene == scene).ToArray();
        if (presentations.Length == 0)
            throw new InvalidOperationException("The district camera has no presentation component.");
        foreach (var presentation in presentations)
        {
            var camera = presentation.GetComponent<Camera>();
            camera.fieldOfView = 60;
            presentation.paintedDistrictGrade = true;
            presentation.exposure = 1.035f;
            presentation.saturation = 1.075f;
            presentation.contactShadows = .33f;
            presentation.contactRadius = .42f;
            presentation.sunShafts = .09f;
            presentation.bloom = .07f;
            presentation.sharpness = .01f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            PrefabUtility.RecordPrefabInstancePropertyModifications(presentation);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(presentation);
        }
    }

    private static Material LightingVariant(Material source, string path)
    {
        if (AssetDatabase.GetAssetPath(source).StartsWith(Lighting + "/", StringComparison.Ordinal))
            return source;
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        material = new Material(source) { name = source.name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
