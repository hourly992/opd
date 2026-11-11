using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

internal static class DistrictLeafLighting
{
    private const string Folder = "Assets/PhotoReconstruction/Stage16Leaf/Materials";

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sun = RenderSettings.sun;
        var sky = RenderSettings.skybox;
        if (scene.path != LeafCourtyardStudy.ScenePath || !sun || !sky || !sky.HasProperty("_LeafSky"))
            throw new InvalidOperationException("Open the saved Street scene with its autumn sky and directional light.");

        sun.transform.rotation = Quaternion.Euler(32.5f, 59f, 0);
        sun.color = new Color(1, .908f, .766f);
        sun.intensity = 1.18f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .97f;
        sun.shadowNormalBias = .23f;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.695f, .765f, .865f);
        RenderSettings.ambientEquatorColor = new Color(.63f, .665f, .725f);
        RenderSettings.ambientGroundColor = new Color(.48f, .435f, .397f);
        RenderSettings.fogColor = new Color(.535f, .625f, .649f);
        RenderSettings.fogDensity = .00195f;

        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        string path = Folder + "/PaintedDaylight.mat";
        var painted = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!painted)
        {
            painted = new Material(sky) { name = "PaintedDaylight" };
            AssetDatabase.CreateAsset(painted, path);
        }
        painted.SetFloat("_LeafSky", .92f);
        painted.SetFloat("_Exposure", .88f);
        painted.SetVector("_SunDirection", -sun.transform.forward);
        RenderSettings.skybox = painted;
        EditorUtility.SetDirty(painted);

        int cameras = 0;
        foreach (var presentation in Object.FindObjectsByType<StreetPresentation>(FindObjectsInactive.Include))
        {
            if (presentation.gameObject.scene != scene) continue;
            presentation.leafGrade = .95f;
            presentation.exposure = 1.055f;
            presentation.saturation = 1.06f;
            presentation.contactShadows = .46f;
            presentation.contactRadius = .48f;
            presentation.sunShafts = .115f;
            presentation.bloom = .084f;
            presentation.sharpness = .015f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(presentation);
            EditorUtility.SetDirty(presentation);
            cameras++;
        }
        if (cameras == 0)
            throw new InvalidOperationException("The saved Street scene has no presentation camera.");
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"DISTRICT_LEAF_LIGHT sunElevation=32.5 sunIntensity=1.18 cameras={cameras} sky={path}");
    }
}
