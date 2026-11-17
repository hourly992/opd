using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class StreetHdrpSetup
{
    private const string AssetFolder = "Assets/StreetHDRP";
    private const string LightingRoot = "HDRP — осеннее освещение";
    private static readonly string[] SceneNames =
    {
        "Street_LeafStudy", "BlosmComparison", "Walk", "Street"
    };

    [MenuItem("Street/HDRP/Configure saved scenes")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before configuring the saved scenes.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (!AssetDatabase.IsValidFolder(AssetFolder))
            AssetDatabase.CreateFolder("Assets", "StreetHDRP");

        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
            new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
        var raster = CreatePipeline("Autumn", false);
        var experimental = CreatePipeline("Autumn RT", true);
        GraphicsSettings.defaultRenderPipeline = raster;
        EnsureGlobalSettings();
        RegisterQualityProfiles(raster, experimental);

        var autumn = CreateAutumnProfile("Autumn lighting", false);
        var comparison = CreateAutumnProfile("Comparison lighting", true);
        var rayTracing = GetProfile("Ray-traced lighting");
        ConfigureUltraProfile(rayTracing);

        var gi = GetProfileComponent<GlobalIllumination>(rayTracing);
        gi.enable.Override(true);
        gi.tracing.Override(RayCastingMode.RayTracing);
        gi.mode.Override(RayTracingMode.Performance);
        gi.quality.Override(ScalableSettingLevelParameter.LevelCount);
        gi.rayLength = 55f;
        gi.fullResolution = false;
        gi.denoise = true;
        gi.halfResolutionDenoiser = false;
        gi.denoiserRadius = .72f;
        gi.secondDenoiserPass = true;
        gi.clampValue = 120f;
        gi.ambientProbeDimmer.Override(.55f);
        gi.textureLodBias.Override(3f);
        OverrideQualityFields(gi, "m_RayLength", "m_FullResolution", "m_Denoise",
            "m_HalfResolutionDenoiser", "m_DenoiserRadius", "m_SecondDenoiserPass", "m_ClampValue");
        EditorUtility.SetDirty(gi);

        foreach (string sceneName in SceneNames)
        {
            string path = $"Assets/Scenes/{sceneName}.unity";
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(path))
                continue;
            bool street = sceneName == "Street" || sceneName == "Street_LeafStudy";
            ConfigureScene(path, street ? autumn : comparison, rayTracing, street, sceneName == "Walk");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("STREET_HDRP_SETUP saved HDRP assets and four scene configurations");
    }

    private static HDRenderPipelineAsset CreatePipeline(string name, bool rayTracing)
    {
        string path = $"{AssetFolder}/{name}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
        if (!asset)
        {
            asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            AssetDatabase.CreateAsset(asset, path);
        }

        var settings = asset.currentPlatformRenderPipelineSettings;
        settings.supportSSR = true;
        settings.supportSSRTransparent = false;
        settings.supportSSAO = true;
        settings.supportVolumetrics = true;
        settings.supportVolumetricClouds = true;
        settings.supportSSGI = rayTracing;
        settings.supportRayTracing = rayTracing;
        settings.supportedRayTracingMode = RenderPipelineSettings.SupportedRayTracingMode.Performance;
        settings.lightProbeSystem = RenderPipelineSettings.LightProbeSystem.AdaptiveProbeVolumes;

        var shadows = settings.hdShadowInitParams;
        shadows.punctualLightShadowAtlas.shadowAtlasResolution = 2048;
        shadows.areaLightShadowAtlas.shadowAtlasResolution = 1024;
        shadows.supportScreenSpaceShadows = rayTracing;
        shadows.directionalShadowFilteringQuality = rayTracing
            ? HDShadowFilteringQuality.High : HDShadowFilteringQuality.Medium;
        shadows.maxScreenSpaceShadowSlots = rayTracing ? 1 : 0;
        settings.hdShadowInitParams = shadows;

        var resolution = settings.dynamicResolutionSettings;
        resolution.enabled = true;
        resolution.dynResType = DynamicResolutionType.Software;
        resolution.useMipBias = true;
        resolution.minPercentage = 50f;
        resolution.maxPercentage = 100f;
        resolution.DLSSUseOptimalSettings = true;
        resolution.advancedUpscalerNames = new() { AdvancedUpscalers.DLSS.ToString() };
        settings.dynamicResolutionSettings = resolution;
        asset.currentPlatformRenderPipelineSettings = settings;
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static void EnsureGlobalSettings()
    {
        // Mirrors HDRP Wizard initialization, including editor-only resources and the default volume.
        var settingsType = typeof(HDRenderPipelineAsset).Assembly.GetType(
            "UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
        var ensure = settingsType?.GetMethod("Ensure",
            BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(bool) }, null);
        if (ensure == null)
            throw new InvalidOperationException("HDRP Global Settings initializer is unavailable.");
        ensure.Invoke(null, new object[] { true });
    }

    private static void RegisterQualityProfiles(HDRenderPipelineAsset high, HDRenderPipelineAsset ultra)
    {
        var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var levels = quality.FindProperty("m_QualitySettings");
        if (levels.arraySize != 2)
        {
            string[] existing = QualitySettings.names;
            int oldRaster = Array.IndexOf(existing, "Ultra");
            int oldRayTracing = Array.IndexOf(existing, "HDRP RT experimental");
            if (oldRaster < 0 || oldRayTracing <= oldRaster)
                throw new InvalidOperationException("Original HDRP quality levels could not be identified.");
            for (int index = levels.arraySize - 1; index >= 0; index--)
            {
                if (index != oldRaster && index != oldRayTracing)
                    levels.DeleteArrayElementAtIndex(index);
            }
        }

        ConfigureQualityLevel(levels.GetArrayElementAtIndex(0), StreetHdrpPreset.HighQuality, high);
        ConfigureQualityLevel(levels.GetArrayElementAtIndex(1), StreetHdrpPreset.RayTracingQuality, ultra);
        quality.FindProperty("m_CurrentQuality").intValue = 0;
        var defaults = quality.FindProperty("m_PerPlatformDefaultQuality");
        var item = defaults.Copy();
        var end = defaults.GetEndProperty();
        for (bool next = item.Next(true); next && !SerializedProperty.EqualContents(item, end); next = item.Next(false))
        {
            if (item.propertyType == SerializedPropertyType.Integer)
                item.intValue = 0;
        }
        quality.ApplyModifiedPropertiesWithoutUndo();
        QualitySettings.SetQualityLevel(0, true);
    }

    private static void ConfigureQualityLevel(SerializedProperty profile, string name, HDRenderPipelineAsset pipeline)
    {
        profile.FindPropertyRelative("name").stringValue = name;
        profile.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipeline;
        profile.FindPropertyRelative("globalTextureMipmapLimit").intValue = 0;
        profile.FindPropertyRelative("anisotropicTextures").intValue = (int)AnisotropicFiltering.ForceEnable;
        profile.FindPropertyRelative("streamingMipmapsActive").boolValue = false;
    }

    private static VolumeProfile CreateAutumnProfile(string name, bool comparison)
    {
        var profile = GetProfile(name);
        var environment = GetProfileComponent<VisualEnvironment>(profile);
        environment.skyType.Override((int)SkyType.PhysicallyBased);
        environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

        var sky = GetProfileComponent<PhysicallyBasedSky>(profile);
        sky.type.Override(PhysicallyBasedSkyModel.EarthSimple);
        sky.groundTint.Override(new Color(.27f, .26f, .23f));
        sky.horizonTint.Override(new Color(1f, .94f, .92f));
        sky.zenithTint.Override(new Color(.82f, .94f, 1f));
        sky.exposure.Override(1.95f);

        var clouds = GetProfileComponent<VolumetricClouds>(profile);
        ConfigureClouds(clouds, false);
        clouds.enable.Override(!comparison);

        var fog = GetProfileComponent<Fog>(profile);
        fog.enabled.Override(!comparison);
        fog.enableVolumetricFog.Override(!comparison);
        fog.baseHeight.Override(2f);
        fog.maximumHeight.Override(27f);
        fog.meanFreePath.Override(385f);
        fog.depthExtent.Override(105f);
        fog.anisotropy.Override(.62f);
        fog.albedo.Override(new Color(.88f, .91f, .95f));
        fog.directionalLightsOnly.Override(true);
        fog.denoisingMode.Override(FogDenoisingMode.Reprojection);
        ConfigureFogQuality(fog, false);

        var shadows = GetProfileComponent<HDShadowSettings>(profile);
        shadows.maxShadowDistance.Override(145f);
        shadows.cascadeShadowSplitCount.Override(4);
        shadows.cascadeShadowSplit0.Override(.062f);
        shadows.cascadeShadowSplit1.Override(.17f);
        shadows.cascadeShadowSplit2.Override(.40f);
        shadows.cascadeShadowBorder0.Override(.16f);
        shadows.cascadeShadowBorder1.Override(.17f);
        shadows.cascadeShadowBorder2.Override(.19f);
        shadows.cascadeShadowBorder3.Override(.15f);

        var exposure = GetProfileComponent<Exposure>(profile);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(comparison ? 13f : 12.05f);

        var indirect = GetProfileComponent<IndirectLightingController>(profile);
        indirect.indirectDiffuseLightingMultiplier.Override(comparison ? 1.1f : 1.35f);

        var tone = GetProfileComponent<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.ACES);

        GetProfileComponent<MotionBlur>(profile).intensity.Override(0f);
        GetProfileComponent<DepthOfField>(profile).focusMode.Override(DepthOfFieldMode.Off);

        var contact = GetProfileComponent<ContactShadows>(profile);
        contact.enable.Override(!comparison);
        contact.directionalOnly.Override(true);
        contact.length.Override(.18f);
        contact.opacity.Override(.40f);
        contact.halfResolution.Override(true);
        contact.maxDistance.Override(35f);

        var bloom = GetProfileComponent<Bloom>(profile);
        bloom.intensity.Override(.095f);
        bloom.threshold.Override(1.6f);
        bloom.scatter.Override(.6f);

        var whiteBalance = GetProfileComponent<WhiteBalance>(profile);
        whiteBalance.temperature.Override(-2f);
        whiteBalance.tint.Override(1f);

        var ao = GetProfileComponent<ScreenSpaceAmbientOcclusion>(profile);
        ao.intensity.Override(.61f);
        ao.directLightingStrength.Override(.12f);
        ao.radius.Override(.55f);

        var reflections = GetProfileComponent<ScreenSpaceReflection>(profile);
        reflections.enabled.Override(!comparison);
        reflections.enabledTransparent.Override(false);
        reflections.quality.Override(0);

        var grade = GetProfileComponent<ColorAdjustments>(profile);
        grade.contrast.Override(2f);
        grade.saturation.Override(3f);

        foreach (var component in profile.components)
            EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void ConfigureClouds(VolumetricClouds clouds, bool ultra)
    {
        clouds.enable.Override(true);
        clouds.cloudControl.Override(VolumetricClouds.CloudControl.Simple);
        clouds.cloudSimpleMode.Override(ultra ? VolumetricClouds.CloudSimpleMode.Quality
            : VolumetricClouds.CloudSimpleMode.Performance);
        clouds.cloudPreset = VolumetricClouds.CloudPresets.Sparse;
        clouds.bottomAltitude.Override(1250f);
        clouds.altitudeRange.Override(1550f);
        clouds.erosionFactor.Override(.64f);
        clouds.scatteringTint.Override(new Color(.015f, .055f, .042f));
        clouds.numPrimarySteps.Override(ultra ? 78 : 38);
        clouds.numLightSteps.Override(ultra ? 10 : 5);
        clouds.temporalAccumulationFactor.Override(ultra ? .93f : .91f);
        clouds.shadows.Override(ultra);
        if (ultra)
        {
            clouds.shadowOpacity.Override(.23f);
            clouds.shadowResolution.Override(VolumetricClouds.CloudShadowResolution.Medium256);
        }
    }

    private static void ConfigureFogQuality(Fog fog, bool ultra)
    {
        fog.quality.Override(ScalableSettingLevelParameter.LevelCount);
        fog.fogControlMode = FogControl.Manual;
        fog.screenResolutionPercentage.Override(ultra ? 14f : 10f);
        fog.volumeSliceCount.Override(ultra ? 96 : 56);
        OverrideQualityFields(fog, "m_FogControlMode");
    }

    private static void ConfigureUltraProfile(VolumeProfile profile)
    {
        ConfigureClouds(GetProfileComponent<VolumetricClouds>(profile), true);
        ConfigureFogQuality(GetProfileComponent<Fog>(profile), true);
        var shadows = GetProfileComponent<HDShadowSettings>(profile);
        shadows.maxShadowDistance.Override(175f);
        foreach (var component in profile.components)
            EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(profile);
    }

    private static void OverrideQualityFields(VolumeComponent component, params string[] properties)
    {
        var serialized = new SerializedObject(component);
        foreach (string name in properties)
        {
            var state = serialized.FindProperty(name)?.FindPropertyRelative("m_OverrideState");
            if (state == null)
                throw new InvalidOperationException($"HDRP parameter {component.GetType().Name}.{name} is missing.");
            state.boolValue = true;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static VolumeProfile GetProfile(string name)
    {
        string path = $"{AssetFolder}/{name}.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        return profile;
    }

    private static T GetProfileComponent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var existing))
            return existing;
        var component = profile.Add<T>();
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    private static void ConfigureScene(string path, VolumeProfile profile, VolumeProfile rayTracing,
        bool street, bool generatedWalk)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();

        foreach (var root in roots)
        {
            foreach (var oldWeather in root.GetComponentsInChildren<StreetAutumnWeather>(true))
                oldWeather.enabled = false;
            foreach (var oldPresentation in root.GetComponentsInChildren<StreetPresentation>(true))
                oldPresentation.enabled = false;
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                StreetHdrpPreset.ConfigureCamera(camera);
        }

        var lights = roots.SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
        var sun = RenderSettings.sun;
        if (!sun)
            sun = lights.FirstOrDefault(light => light.enabled && light.type == LightType.Directional);
        if (sun)
        {
            StreetHdrpPreset.ConfigureSun(sun, street);
            RenderSettings.sun = sun;
        }

        foreach (var light in lights.Where(light => light != sun))
        {
            if (!light.TryGetComponent<HDAdditionalLightData>(out var data))
                data = light.gameObject.AddComponent<HDAdditionalLightData>();
            if (street && light.type == LightType.Spot)
            {
                light.lightUnit = LightUnit.Candela;
                light.intensity = light.range > 15f ? 950f : 650f;
                data.volumetricDimmer = .2f;
            }
        }

        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

        var lighting = roots.FirstOrDefault(root => root.name == LightingRoot) ?? new GameObject(LightingRoot);
        if (street && sun)
            ConfigureSkyFill(lighting.transform, sun.transform.eulerAngles.y);
        var volume = GetOrAdd<Volume>(lighting);
        volume.isGlobal = true;
        volume.priority = 10;
        volume.sharedProfile = profile;

        Volume experimentalVolume = null;
        if (street)
        {
            var experimental = GetOrCreateChild(lighting.transform, "RTGI — экспериментальный пресет");
            experimentalVolume = GetOrAdd<Volume>(experimental);
            experimentalVolume.isGlobal = true;
            experimentalVolume.priority = 20;
            experimentalVolume.sharedProfile = rayTracing;
            experimentalVolume.enabled = false;

            AddLocalFog(lighting.transform, "Аллея возле дома № 5", new Vector3(-25f, 3f, 20f),
                new Vector3(31f, 7f, 24f));
            AddLocalFog(lighting.transform, "Двор дома № 7", new Vector3(40f, 3f, 39f),
                new Vector3(29f, 7f, 19f));
            AddLocalFog(lighting.transform, "Опушка возле дома № 13", new Vector3(163f, 4f, 130f),
                new Vector3(41f, 9f, 36f));
        }

        GetOrAdd<StreetHdrpPreset>(lighting).SetSceneReferences(experimentalVolume, sun, generatedWalk);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException($"Cannot save HDRP scene {path}.");
    }

    private static void ConfigureSkyFill(Transform parent, float sunAzimuth)
    {
        AddSkyFill(parent, "Рассеянный свет неба", new Vector3(29f, sunAzimuth + 175f, 0f),
            new Color(.81f, .90f, 1f), 5100f);
        AddSkyFill(parent, "Свет верхней полусферы", new Vector3(67f, sunAzimuth + 70f, 0f),
            new Color(.91f, .95f, 1f), 2300f);
    }

    private static void AddSkyFill(Transform parent, string name, Vector3 direction, Color color, float lux)
    {
        var node = GetOrCreateChild(parent, name);
        node.transform.rotation = Quaternion.Euler(direction);
        var light = GetOrAdd<Light>(node);
        light.type = LightType.Directional;
        light.lightUnit = LightUnit.Lux;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        light.color = color;
        light.intensity = lux;
        light.shadows = LightShadows.None;
        var data = GetOrAdd<HDAdditionalLightData>(node);
        data.interactsWithSky = false;
        data.affectsVolumetric = false;
    }

    private static void AddLocalFog(Transform parent, string name, Vector3 position, Vector3 size)
    {
        var node = GetOrCreateChild(parent, name);
        node.transform.position = position;
        var fog = GetOrAdd<LocalVolumetricFog>(node);
        var parameters = new LocalVolumetricFogArtistParameters(new Color(.93f, .94f, .98f), 103f, 0f)
        {
            size = size,
            positiveFade = new Vector3(.33f, .43f, .33f),
            negativeFade = new Vector3(.33f, .35f, .33f),
            distanceFadeStart = 55f,
            distanceFadeEnd = 85f
        };
        fog.parameters = parameters;
    }

    private static T GetOrAdd<T>(GameObject item) where T : Component
        => item.TryGetComponent<T>(out var component) ? component : item.AddComponent<T>();

    private static GameObject GetOrCreateChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing)
            return existing.gameObject;
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }
}