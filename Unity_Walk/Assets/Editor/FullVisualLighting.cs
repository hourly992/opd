using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

internal static class FullVisualLighting
{
    internal const string Root = "Assets/PhotoReconstruction/FullVisual";
    private const string Materials = Root + "/Materials";

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sun = RenderSettings.sun;
        var sourceSky = RenderSettings.skybox;
        if (scene.path != "Assets/Scenes/Street.unity" || !sun || !sourceSky || !sourceSky.HasProperty("_LeafSky"))
            throw new InvalidOperationException("Open the main Street scene with its saved sun and autumn sky.");

        Directory.CreateDirectory(Materials);
        AssetDatabase.Refresh();
        sun.transform.rotation = Quaternion.Euler(32.5f, 59f, 0);
        sun.color = new Color(1, .908f, .766f);
        sun.intensity = 1.18f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .97f;
        sun.shadowNormalBias = .23f;
        EditorUtility.SetDirty(sun);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.695f, .765f, .865f);
        RenderSettings.ambientEquatorColor = new Color(.63f, .665f, .725f);
        RenderSettings.ambientGroundColor = new Color(.48f, .435f, .397f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(.535f, .625f, .649f);
        RenderSettings.fogDensity = .00195f;

        string skyPath = Materials + "/DistrictPaintedSky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (!sky)
        {
            sky = new Material(sourceSky) { name = "DistrictPaintedSky" };
            AssetDatabase.CreateAsset(sky, skyPath);
        }
        sky.SetFloat("_LeafSky", .92f);
        sky.SetFloat("_SoftDaylight", .82f);
        sky.SetFloat("_Exposure", .88f);
        sky.SetVector("_SunDirection", -sun.transform.forward);
        RenderSettings.skybox = sky;
        EditorUtility.SetDirty(sky);

        int cameras = 0, probes = 0;
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
        foreach (var probe in Object.FindObjectsByType<ReflectionProbe>())
        {
            if (probe.gameObject.scene != scene) continue;
            bool skyOnly = probe.cullingMask == 0;
            probe.resolution = skyOnly ? 128 : 256;
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.hdr = true;
            probe.nearClipPlane = .3f;
            probe.farClipPlane = skyOnly ? 500 : 180;
            probe.shadowDistance = skyOnly ? 0 : 65;
            PrefabUtility.RecordPrefabInstancePropertyModifications(probe);
            EditorUtility.SetDirty(probe);
            probes++;
        }
        int glass = MatchWindowSky(sky);
        if (cameras == 0 || probes != 5)
            throw new InvalidOperationException("The saved district camera or reflection probe network is incomplete.");
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"FULL_VISUAL_LIGHTING cameras={cameras} reflectionProbes={probes} skyMatchedGlass={glass}");
    }

    private static int MatchWindowSky(Material sky)
    {
        var variants = new Dictionary<Material, Material>();
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (renderer.gameObject.scene != scene) continue;
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source || source.shader.name != "Street/Glass") continue;
                if (!variants.TryGetValue(source, out var glass))
                {
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long file))
                        throw new InvalidOperationException("The window material has no saved asset.");
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    string path = sourcePath.StartsWith(Materials + "/Glass_", StringComparison.Ordinal)
                        ? sourcePath : $"{Materials}/Glass_{guid}_{file}.mat";
                    glass = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!glass)
                    {
                        glass = new Material(source) { name = source.name };
                        AssetDatabase.CreateAsset(glass, path);
                    }
                    glass.SetFloat("_AutumnSky", 1);
                    glass.SetFloat("_SkySoftDaylight", sky.GetFloat("_SoftDaylight"));
                    glass.SetFloat("_SkyLeaf", sky.GetFloat("_LeafSky"));
                    glass.SetFloat("_SkyExposure", sky.GetFloat("_Exposure"));
                    glass.SetFloat("_SkyRotation", sky.GetFloat("_Rotation"));
                    glass.SetVector("_SkySunDirection", sky.GetVector("_SunDirection"));
                    glass.SetTexture("_SkyTex", sky.GetTexture("_MainTex"));
                    EditorUtility.SetDirty(glass);
                    variants.Add(source, glass);
                }
                materials[i] = glass;
                changed = true;
            }
            if (!changed) continue;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
        }
        return variants.Count;
    }
}
