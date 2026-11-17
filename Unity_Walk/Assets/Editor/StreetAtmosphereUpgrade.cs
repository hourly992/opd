using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class StreetAtmosphereUpgrade
{
    [MenuItem("Street/Improve district atmosphere")]
    public static void ApplyAndBuild()
    {
        foreach (string path in new[] { "Assets/StreetHDRP/Autumn.asset", "Assets/StreetHDRP/Autumn RT.asset" })
        {
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
            var serialized = new SerializedObject(asset);
            var resolution = serialized.FindProperty("m_RenderPipelineSettings.hdShadowInitParams.maxDirectionalShadowMapResolution");
            if (resolution == null)
                throw new InvalidOperationException($"Cannot configure shadow resolution in {path}.");
            resolution.intValue = 4096;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (string name in new[] { "Autumn lighting", "Comparison lighting" })
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"Assets/StreetHDRP/{name}.asset");
            var shadows = Component<HDShadowSettings>(profile);
            shadows.maxShadowDistance.Override(320f);
            shadows.cascadeShadowSplitCount.Override(4);
            shadows.cascadeShadowSplit0.Override(.045f);
            shadows.cascadeShadowSplit1.Override(.15f);
            shadows.cascadeShadowSplit2.Override(.38f);
            var contact = Component<ContactShadows>(profile);
            contact.enable.Override(true);
            contact.length.Override(.24f);
            contact.opacity.Override(.67f);
            contact.directionalOnly.Override(false);
            contact.maxDistance.Override(45f);
            contact.halfResolution.Override(false);
            var ao = Component<ScreenSpaceAmbientOcclusion>(profile);
            ao.intensity.Override(.77f);
            ao.radius.Override(.58f);
            ao.directLightingStrength.Override(.22f);
            var bloom = Component<Bloom>(profile);
            bloom.intensity.Override(.035f);
            bloom.threshold.Override(3.5f);
            bloom.scatter.Override(.38f);
            EditorUtility.SetDirty(profile);
        }

        foreach (string path in new[] { "Assets/Scenes/Street_LeafStudy.unity", "Assets/Scenes/Street.unity" })
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var sun = RenderSettings.sun;
            if (!sun || !sun.TryGetComponent<HDAdditionalLightData>(out var data))
                throw new InvalidOperationException($"Missing HDRP sun in {path}.");
            data.angularDiameter = .56f;
            data.flareSize = .65f;
            data.normalBias = .18f;
            data.slopeBias = .35f;
            data.shadowNearPlane = .35f;
            data.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
            data.SetShadowResolution(4096);
            data.SetShadowResolutionOverride(true);
            var clock = UnityEngine.Object.FindFirstObjectByType<StreetDayNight>();
            var serialized = new SerializedObject(clock);
            serialized.FindProperty("moonLux").floatValue = 4.5f;
            serialized.FindProperty("nightAmbient").floatValue = .8f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            StreetNightLighting.Apply(scene);
            if (scene.name == "Street")
            {
                StreetRoadAtmosphere.Apply(scene);
                StreetGrassVolumeUpgrade.Apply(scene);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"STREET_ATMOSPHERE_APPLIED {path}");
        }
        AssetDatabase.SaveAssets();
        Build();
    }

    private static T Component<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile || !profile.TryGet<T>(out var component))
            throw new InvalidOperationException($"Volume is missing {typeof(T).Name}.");
        return component;
    }

    public static void Build()
    {
        string output = Path.GetFullPath("Build/StreetWindowsHDRPAtmosphere/DuninaGorkavicha.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Street.unity", "Assets/Scenes/Street_LeafStudy.unity" },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = output,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Atmosphere build failed: {report.summary.result}");
        Debug.Log($"STREET_ATMOSPHERE_BUILD {output}");
    }
}
