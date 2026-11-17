using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class StreetDayNightSetup
{
    private const string RootName = "Время суток · HDRP";
    private static readonly string[] CityScenes =
    {
        "Assets/Scenes/Street.unity", "Assets/Scenes/Street_LeafStudy.unity"
    };
    private static readonly string[] OtherScenes =
    {
        "Assets/Scenes/Walk.unity", "Assets/Scenes/BlosmComparison.unity"
    };

    [MenuItem("Street/Day and night/Configure city scenes")]
    public static void Install()
    {
        if (EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Save the open scene before installing time of day.");

        var stars = StreetStarfieldAssets.GetOrCreate(false);
        var milkyWay = StreetStarfieldAssets.GetOrCreate(true);
        foreach (string path in OtherScenes.Concat(CityScenes.Reverse()))
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            StreetBinVisibility.SetHidden(scene);
            if (!CityScenes.Contains(path))
            {
                if (scene.isDirty)
                    EditorSceneManager.SaveScene(scene);
                continue;
            }

            var sun = RenderSettings.sun;
            if (!sun || sun.type != LightType.Directional || !sun.TryGetComponent<HDAdditionalLightData>(out var data))
                throw new InvalidOperationException($"HDRP sun is missing in {path}.");
            var lights = UnityEngine.Object.FindObjectsByType<Light>();
            var sky = UnityEngine.Object.FindObjectsByType<Volume>()
                .Single(volume => volume.gameObject.scene == scene && volume.isGlobal &&
                    volume.sharedProfile && volume.sharedProfile.TryGet<PhysicallyBasedSky>(out _));
            var fill = lights.Where(light => light.gameObject.scene == scene && light.type == LightType.Directional &&
                light != sun && (light.name == "Рассеянный свет неба" || light.name == "Свет верхней полусферы")).ToArray();
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            data.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
            var root = scene.GetRootGameObjects().SingleOrDefault(candidate => candidate.name == RootName);
            if (!root)
                root = new GameObject(RootName);
            var cycleVolume = root.GetComponent<Volume>() ?? root.AddComponent<Volume>();
            cycleVolume.isGlobal = true;
            cycleVolume.priority = 100f;
            cycleVolume.weight = 1f;
            var moon = root.GetComponentInChildren<Light>(true);
            if (!moon)
            {
                var moonObject = new GameObject("Лунный свет");
                moonObject.transform.SetParent(root.transform, false);
                moon = moonObject.AddComponent<Light>();
            }
            moon.type = LightType.Directional;
            moon.lightUnit = LightUnit.Lux;
            moon.lightmapBakeType = LightmapBakeType.Realtime;
            moon.color = new Color(.39f, .56f, 1f);
            moon.shadows = LightShadows.None;
            moon.intensity = 0f;
            moon.enabled = false;
            var moonData = moon.GetComponent<HDAdditionalLightData>() ?? moon.gameObject.AddComponent<HDAdditionalLightData>();
            moonData.interactsWithSky = false;
            moonData.affectsVolumetric = true;
            moonData.volumetricDimmer = .22f;
            var clock = root.GetComponent<StreetDayNight>() ?? root.AddComponent<StreetDayNight>();
            clock.BindScene(sun, moon, fill, sky, cycleVolume, stars, milkyWay);
            var clockSettings = new SerializedObject(clock);
            clockSettings.FindProperty("moonLux").floatValue = 16f;
            clockSettings.FindProperty("nightExposure").floatValue = 4.8f;
            clockSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(clock);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException($"Could not save {path}.");
            Debug.Log($"STREET_DAY_NIGHT_INSTALLED {path} fill={fill.Length}");
        }
        AssetDatabase.SaveAssets();
    }

    public static void BuildWindows()
    {
        string path = Path.GetFullPath("Build/StreetWindowsHDRPDayNight/DuninaGorkavicha.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = CityScenes,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = path,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Day/night Windows build failed: {report.summary.result}");
        Debug.Log($"STREET_DAY_NIGHT_BUILD {path}");
    }

    public static void RefineAndBuildWindows()
    {
        StreetStarfieldAssets.Rebake();
        Install();
        BuildWindows();
    }

    public static void RefineNightAndBuild()
    {
        foreach (string path in CityScenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var clock = UnityEngine.Object.FindFirstObjectByType<StreetDayNight>();
            var serialized = new SerializedObject(clock);
            serialized.FindProperty("moonLux").floatValue = 16f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
        }
        BuildWindows();
    }
}
