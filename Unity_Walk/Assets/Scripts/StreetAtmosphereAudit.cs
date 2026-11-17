using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public sealed class StreetAtmosphereAudit : MonoBehaviour
{
    [Serializable]
    private sealed class Audit
    {
        public string status, scene, quality, gpu;
        public int grassTiles, roadMarkingRenderers, decalProjectors;
        public float grassDrawDistance;
        public Snapshot[] snapshots = Array.Empty<Snapshot>();
    }

    [Serializable]
    private sealed class Snapshot
    {
        public float elapsedSeconds, hour, sunElevation, sunLux, sunDirectDimmer, sunNormalBias, sunSlopeBias;
        public float sunAngularDiameter, sunConeDegrees, shadowDistance, contactLength, contactOpacity, ambientDiffuse;
        public float exposure, bloom, aoIntensity, aoRadius, windowNightBlend;
        public int activeLamps, shadowLamps, registeredLamps, sunShadowResolution, cascades;
        public bool sunCastsShadows, frameShadowMaps, frameContactShadows, frameAmbientOcclusion;
        public bool sunRayTracingShadows, usesOffscreenHistory;
        public Vector3 cameraPosition, cameraEulerAngles;
        public Lamp[] lamps = Array.Empty<Lamp>();
    }

    [Serializable]
    private sealed class Lamp
    {
        public string name;
        public Vector3 position;
        public float intensity, range, angle;
        public bool shadows;
    }

    private readonly List<Snapshot> samples = new();
    private Audit report;
    private string reportPath;
    private Camera view;
    private StreetDayNight clock;
    private StreetLampNetwork[] networks;
    private MethodInfo getRequestCamera;
    private object[] historyArguments;
    private bool offscreen;
    private float nextCapture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnableRequestedAudit()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-atmosphereAudit") >= 0)
            new GameObject("Street atmosphere audit").AddComponent<StreetAtmosphereAudit>();
    }

    private void Start()
    {
        var args = Environment.GetCommandLineArgs();
        int outputIndex = Array.IndexOf(args, "-qaOutput");
        string output = outputIndex >= 0 && outputIndex + 1 < args.Length
            ? args[outputIndex + 1] : Path.Combine(Application.dataPath, "..", "QA_Atmosphere");
        Directory.CreateDirectory(output);
        reportPath = Path.Combine(output, "atmosphere_audit.json");
        view = FindObjectsByType<Camera>(FindObjectsInactive.Include).FirstOrDefault(camera => camera.CompareTag("MainCamera"));
        clock = FindFirstObjectByType<StreetDayNight>();
        networks = FindObjectsByType<StreetLampNetwork>();
        offscreen = Array.IndexOf(args, "-hdrpQAOffscreen") >= 0 || Array.IndexOf(args, "-qaCaptureOnly") >= 0;
        var history = typeof(HDCamera).GetNestedType("HistoryChannel", BindingFlags.NonPublic);
        if (history != null && Enum.IsDefined(history, "CustomUserHistory0"))
        {
            getRequestCamera = typeof(HDCamera).GetMethod("GetOrCreate", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Camera), typeof(int), history }, null);
            historyArguments = new[] { (object)view, 0, Enum.Parse(history, "CustomUserHistory0") };
        }
        var terrains = FindObjectsByType<Terrain>().Where(terrain => !terrain.drawHeightmap && terrain.drawTreesAndFoliage).ToArray();
        var renderers = FindObjectsByType<MeshRenderer>();
        report = new Audit
        {
            status = "running", scene = SceneManager.GetActiveScene().name,
            quality = QualitySettings.names[QualitySettings.GetQualityLevel()], gpu = SystemInfo.graphicsDeviceName,
            grassTiles = terrains.Length,
            grassDrawDistance = terrains.Length > 0 ? terrains.Max(terrain => terrain.detailObjectDistance) : 0,
            roadMarkingRenderers = renderers.Count(renderer => renderer.name.Contains("Разметка") ||
                renderer.name.StartsWith("Road marking", StringComparison.Ordinal) ||
                renderer.name.StartsWith("RoadMarking", StringComparison.Ordinal)),
            decalProjectors = FindObjectsByType<DecalProjector>().Length
        };
        Save();
        RenderPipelineManager.endCameraRendering += ReadRenderedFrame;
    }

    private void ReadRenderedFrame(ScriptableRenderContext context, Camera camera)
    {
        if (!view || camera != view || Time.realtimeSinceStartup < nextCapture || samples.Count >= 64)
            return;
        nextCapture = Time.realtimeSinceStartup + .55f;
        var hd = offscreen && getRequestCamera != null
            ? getRequestCamera.Invoke(null, historyArguments) as HDCamera : HDCamera.GetOrCreate(view);
        if (hd?.volumeStack == null)
            return;

        var data = clock && clock.Sun ? clock.Sun.GetComponent<HDAdditionalLightData>() : null;
        var sun = clock ? clock.Sun : RenderSettings.sun;
        var stack = hd.volumeStack;
        var shadows = stack.GetComponent<HDShadowSettings>();
        var contact = stack.GetComponent<ContactShadows>();
        var ao = stack.GetComponent<ScreenSpaceAmbientOcclusion>();
        var ambient = stack.GetComponent<IndirectLightingController>();
        var exposure = stack.GetComponent<Exposure>();
        var bloom = stack.GetComponent<Bloom>();
        var selected = new List<Lamp>();
        int registered = 0;
        foreach (var network in networks)
        {
            if (network.lamps == null)
                continue;
            registered += network.lamps.Length;
            foreach (var light in network.lamps)
            {
                if (!light || !light.enabled || !light.gameObject.activeInHierarchy || light.intensity <= .001f)
                    continue;
                selected.Add(new Lamp
                {
                    name = light.name, position = light.transform.position, intensity = light.intensity,
                    range = light.range, angle = light.spotAngle, shadows = light.shadows != LightShadows.None
                });
            }
        }
        samples.Add(new Snapshot
        {
            elapsedSeconds = Time.realtimeSinceStartup, hour = clock ? clock.CurrentHour : -1,
            sunElevation = clock ? clock.SunElevation : 0f, sunLux = sun ? sun.intensity : 0f,
            sunDirectDimmer = data ? data.lightDimmer : 0f,
            sunNormalBias = data ? data.normalBias : 0f, sunSlopeBias = data ? data.slopeBias : 0f,
            sunAngularDiameter = data ? data.angularDiameter : 0f, sunConeDegrees = data ? data.sunLightConeAngle : 0f,
            sunShadowResolution = data ? data.shadowResolution.@override : 0,
            sunCastsShadows = sun && sun.shadows != LightShadows.None,
            sunRayTracingShadows = data && data.useRayTracedShadows,
            shadowDistance = shadows ? shadows.maxShadowDistance.value : 0f,
            cascades = shadows ? shadows.cascadeShadowSplitCount.value : 0,
            contactLength = contact ? contact.length.value : 0f, contactOpacity = contact ? contact.opacity.value : 0f,
            frameShadowMaps = hd.frameSettings.IsEnabled(FrameSettingsField.ShadowMaps),
            frameContactShadows = hd.frameSettings.IsEnabled(FrameSettingsField.ContactShadows),
            frameAmbientOcclusion = hd.frameSettings.IsEnabled(FrameSettingsField.SSAO),
            aoIntensity = ao ? ao.intensity.value : 0f, aoRadius = ao ? ao.radius.value : 0f,
            ambientDiffuse = ambient ? ambient.indirectDiffuseLightingMultiplier.value : 0f,
            exposure = exposure ? exposure.fixedExposure.value : 0f, bloom = bloom ? bloom.intensity.value : 0f,
            windowNightBlend = Shader.GetGlobalFloat("_StreetNightBlend"),
            activeLamps = selected.Count, shadowLamps = selected.Count(light => light.shadows),
            registeredLamps = registered, lamps = selected.ToArray(),
            cameraPosition = view.transform.position, cameraEulerAngles = view.transform.eulerAngles,
            usesOffscreenHistory = offscreen
        });
        report.quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
        Save();
    }

    private void Save()
    {
        report.snapshots = samples.ToArray();
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    }

    private void OnApplicationQuit()
    {
        if (report == null)
            return;
        report.status = samples.Count > 0 ? "finished" : "no camera samples";
        Save();
    }

    private void OnDestroy()
    {
        RenderPipelineManager.endCameraRendering -= ReadRenderedFrame;
    }
}
