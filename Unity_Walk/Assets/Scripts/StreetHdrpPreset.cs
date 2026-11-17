using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DisallowMultipleComponent]
public sealed class StreetHdrpPreset : MonoBehaviour
{
    public const string HighQuality = "High";
    public const string RayTracingQuality = "Ultra";

    [SerializeField] private Volume rayTracingVolume;
    [SerializeField] private Light sun;
    [SerializeField] private bool configureGeneratedWalk;

    public void SetSceneReferences(Volume experimentalVolume, Light directionalLight, bool generatedWalk)
    {
        rayTracingVolume = experimentalVolume;
        sun = directionalLight;
        configureGeneratedWalk = generatedWalk;
    }

    private IEnumerator Start()
    {
        if (configureGeneratedWalk)
        {
            foreach (var light in FindObjectsByType<Light>())
            {
                if (light.gameObject.scene != gameObject.scene || light.type != LightType.Directional)
                    continue;
                sun = light;
                ConfigureSun(light, false);
                RenderSettings.sun = light;
                break;
            }
        }

        var args = Environment.GetCommandLineArgs();
        string[] levels = QualitySettings.names;
        int highLevel = Array.IndexOf(levels, HighQuality);
        int ultraLevel = Array.IndexOf(levels, RayTracingQuality);
        int selectedLevel = QualitySettings.GetQualityLevel();
        int rasterLevel = highLevel >= 0 ? highLevel : selectedLevel;
        bool preferHigh = Array.IndexOf(args, "-streetHDRPHigh") >= 0;
        bool requestRayTracing = !preferHigh && Array.IndexOf(args, "-noRayTracing") < 0 &&
            (Array.IndexOf(args, "-streetHDRPUltra") >= 0 || Array.IndexOf(args, "-streetHDRPRT") >= 0 ||
             Application.isEditor && selectedLevel == ultraLevel);
        bool requestDlss = Array.IndexOf(args, "-streetHDRPDLSS") >= 0;
        bool allowTaaOnly = Array.IndexOf(args, "-streetHDRPTAA") >= 0;
        bool explicitDlaa = Array.IndexOf(args, "-streetHDRPDLAA") >= 0;
        bool direct3D12 = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12;
        bool rayTracing = requestRayTracing && direct3D12 && rayTracingVolume != null &&
            SystemInfo.supportsRayTracing && ultraLevel >= 0;
        int targetLevel = rayTracing ? ultraLevel : rasterLevel;
        if (selectedLevel != targetLevel)
            QualitySettings.SetQualityLevel(targetLevel, true);
        bool requestDlaa = !requestDlss && !allowTaaOnly && (explicitDlaa || rayTracing);

        ApplyRayTracing(rayTracing);
        if (direct3D12 && (rayTracing || requestDlss || requestDlaa))
        {
            double initDeadline = Time.realtimeSinceStartupAsDouble + 12;
            while (RenderPipelineManager.currentPipeline is not HDRenderPipeline &&
                Time.realtimeSinceStartupAsDouble < initDeadline)
                yield return null;
        }
        bool enableDlss = direct3D12 && (requestDlss || requestDlaa) &&
            HDDynamicResolutionPlatformCapabilities.DLSSDetected;
        foreach (var camera in FindObjectsByType<Camera>())
        {
            if (camera.gameObject.scene != gameObject.scene)
                continue;
            var data = ConfigureCamera(camera);
            SetFrameSetting(data, FrameSettingsField.AdaptiveProbeVolume, !rayTracing);
            SetFrameSetting(data, FrameSettingsField.SSGI, rayTracing);
            SetFrameSetting(data, FrameSettingsField.RayTracing, rayTracing);
            SetFrameSetting(data, FrameSettingsField.ScreenSpaceShadows, rayTracing);
            data.allowDeepLearningSuperSampling = enableDlss;
            data.allowDynamicResolution = enableDlss;
            camera.allowDynamicResolution = enableDlss;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            data.deepLearningSuperSamplingUseCustomQualitySettings = enableDlss;
            data.deepLearningSuperSamplingUseCustomAttributes = enableDlss;
            data.deepLearningSuperSamplingUseOptimalSettings = enableDlss;
            data.deepLearningSuperSamplingSharpening = 0f;
            data.deepLearningSuperSamplingQuality = (uint)(requestDlaa
                ? UnityEngine.NVIDIA.DLSSQuality.DLAA
                : UnityEngine.NVIDIA.DLSSQuality.MaximumQuality);
#endif
        }

        for (int frame = 0; frame < 6; frame++)
            yield return null;

        if (rayTracing && !(RenderPipelineManager.currentPipeline is HDRenderPipeline { rayTracingSupported: true }))
        {
            QualitySettings.SetQualityLevel(rasterLevel, true);
            ApplyRayTracing(false);
            foreach (var camera in FindObjectsByType<HDAdditionalCameraData>())
            {
                if (camera.gameObject.scene != gameObject.scene)
                    continue;
                SetFrameSetting(camera, FrameSettingsField.AdaptiveProbeVolume, true);
                SetFrameSetting(camera, FrameSettingsField.SSGI, false);
                SetFrameSetting(camera, FrameSettingsField.RayTracing, false);
                SetFrameSetting(camera, FrameSettingsField.ScreenSpaceShadows, false);
            }
            rayTracing = false;
            yield return null;
        }

        var main = Camera.main;
        var hdrpCamera = main && RenderPipelineManager.currentPipeline is HDRenderPipeline
            ? HDCamera.GetOrCreate(main) : null;
        var dlssQuery = typeof(HDCamera).GetMethod("IsDLSSEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        var dlssActive = hdrpCamera != null && dlssQuery != null
            ? dlssQuery.Invoke(hdrpCamera, null)?.ToString()
            : "Unavailable";
        var frameSettings = hdrpCamera != null ? hdrpCamera.frameSettings : default;
        var stack = hdrpCamera?.volumeStack;
        var gi = stack?.GetComponent<GlobalIllumination>();
        var fog = stack?.GetComponent<Fog>();
        string gpuPreset = requestDlaa ? "DLAA" : requestDlss ? "DLSS Quality" : "TAA";
        Debug.Log($"STREET_HDRP scene={gameObject.scene.name} API={SystemInfo.graphicsDeviceType} " +
            $"GPU={SystemInfo.graphicsDeviceName} DXR={SystemInfo.supportsRayTracing} RTRequested={requestRayTracing} " +
            $"RTActive={rayTracing && frameSettings.IsEnabled(FrameSettingsField.RayTracing)} " +
            $"RTGI={gi != null && gi.enable.value && gi.tracing.value == RayCastingMode.RayTracing && frameSettings.IsEnabled(FrameSettingsField.SSGI)} " +
            $"RTShadows={rayTracing && sun && frameSettings.IsEnabled(FrameSettingsField.ScreenSpaceShadows)} " +
            $"SSR={frameSettings.IsEnabled(FrameSettingsField.SSR)} " +
            $"VolumetricFog={fog != null && fog.enabled.value && fog.enableVolumetricFog.value && frameSettings.IsEnabled(FrameSettingsField.Volumetrics)} " +
            $"UpscalerRequested={gpuPreset} DLSSDetected={HDDynamicResolutionPlatformCapabilities.DLSSDetected} DLSSActive={dlssActive}");
    }



    private void ApplyRayTracing(bool enabled)
    {
        if (rayTracingVolume)
            rayTracingVolume.enabled = enabled;
        if (sun && sun.TryGetComponent<HDAdditionalLightData>(out var data))
        {
            data.useScreenSpaceShadows = enabled;
            data.useRayTracedShadows = enabled;
            data.numRayTracingSamples = enabled ? 4 : 2;
            data.filterSizeTraced = enabled ? 7 : 5;
            data.SetShadowResolution(enabled ? 4096 : 2048);
            data.useContactShadow.useOverride = true;
            data.useContactShadow.@override = !enabled;
        }
    }

    public static void ConfigureSun(Light light, bool street)
    {
        var data = light.TryGetComponent<HDAdditionalLightData>(out var existing)
            ? existing : light.gameObject.AddComponent<HDAdditionalLightData>();
        light.lightUnit = LightUnit.Lux;
        light.intensity = 26000f;
        light.color = Color.white;
        light.useColorTemperature = true;
        light.colorTemperature = 5050f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 1f;
        if (street)
        {
            light.transform.rotation = Quaternion.Euler(20f, 126f, 0f);
        }
        data.interactsWithSky = true;
        data.angularDiameter = 1.15f;
        data.affectsVolumetric = true;
        data.volumetricDimmer = .92f;
        data.volumetricShadowDimmer = 1f;
        data.SetShadowResolution(2048);
        data.SetShadowResolutionOverride(true);
        data.numRayTracingSamples = 2;
        data.filterTracedShadow = true;
    }

    public static HDAdditionalCameraData ConfigureCamera(Camera camera)
    {
        var data = camera.TryGetComponent<HDAdditionalCameraData>(out var existing)
            ? existing : camera.gameObject.AddComponent<HDAdditionalCameraData>();
        data.clearColorMode = camera.clearFlags == CameraClearFlags.SolidColor
            ? HDAdditionalCameraData.ClearColorMode.Color
            : HDAdditionalCameraData.ClearColorMode.Sky;
        data.backgroundColorHDR = camera.backgroundColor;
        data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
        data.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
        data.taaBaseBlendFactor = .81f;
        data.taaMotionVectorRejection = .23f;
        data.taaAntiFlicker = .41f;
        data.taaJitterScale = .91f;
        data.taaHistorySharpening = .22f;
        data.taaSharpenStrength = .25f;
        data.dithering = true;
        data.volumeLayerMask = 1;
        data.allowDynamicResolution = false;
        data.allowDeepLearningSuperSampling = false;
        camera.allowHDR = true;
        SetFrameSetting(data, FrameSettingsField.AdaptiveProbeVolume, true);
        SetFrameSetting(data, FrameSettingsField.ContactShadows, true);
        SetFrameSetting(data, FrameSettingsField.SSR, true);
        SetFrameSetting(data, FrameSettingsField.SSAO, true);
        SetFrameSetting(data, FrameSettingsField.Volumetrics, true);
        SetFrameSetting(data, FrameSettingsField.VolumetricClouds, true);
        return data;
    }

    private static void SetFrameSetting(HDAdditionalCameraData data, FrameSettingsField field, bool enabled)
    {
        data.customRenderingSettings = true;
        data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
        data.renderingPathCustomFrameSettings.SetEnabled(field, enabled);
    }
}
