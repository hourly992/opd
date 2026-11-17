using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public sealed class StreetDayNightQA : MonoBehaviour
{
    [Serializable]
    private sealed class Report
    {
        public string status, scene, gpu, graphicsApi, quality;
        public bool passed, hdrp, grounded, playerControlsConfigured, clockAdvances;
        public bool continuousSun, daylightShadowsEnabled, starsFade, nativeStarCubemap, dayNightExposureDistinct;
        public int width, height, containers, litterBins, visibleBinRenderers, enabledBinColliders;
        public float walkingDistance, simulatedClockAdvance, actualSeconds;
        public float maxSunRotationStepDegrees, maxSunIntensityStepFraction;
        public float maximumSunBearingError, maximumStarVisibilityStep;
        public Phase[] phases;
        public string[] errors;
    }

    [Serializable]
    private sealed class Phase
    {
        public string screenshot;
        public float hour, sunElevation, sunAzimuth, renderedSunElevation, sunLux, sunShadowStrength;
        public float stars, hdrpStarMultiplier;
        public bool sunEnabled, sunCastsShadows, hdrpShadowMaps, nativeStarCubemap;
        public string visualSky;
        public float meanLuma, medianLuma, p95Luma, darkPercent, magentaPercent, variedPixelPercent;
        public int isolatedSkyHighlights;
    }

    private readonly List<string> errors = new();
    private Report report;
    private string outputDirectory;
    private RenderTexture target;
    private Texture2D image;
    private Camera camera;
    private FirstPersonWalk walk;
    private StreetSession session;
    private CharacterController player;
    private StreetDayNight cycle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartWhenRequested()
    {
        if (Environment.GetCommandLineArgs().Contains("-dayNightQA"))
            new GameObject("Street day and night diagnostic").AddComponent<StreetDayNightQA>();
    }

    private IEnumerator Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        outputDirectory = Path.GetFullPath(ReadOption(args, "-qaOutput") ??
            Path.Combine(Application.dataPath, "..", "QA_DayNight"));
        Directory.CreateDirectory(outputDirectory);
        report = new Report
        {
            status = "running", scene = gameObject.scene.name, gpu = SystemInfo.graphicsDeviceName,
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            width = ReadDimension(args, "-screen-width", 1600), height = ReadDimension(args, "-screen-height", 900)
        };
        WriteReport();
        Application.logMessageReceived += CollectError;
        StartCoroutine(Watchdog());
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        yield return null;
        camera = Camera.main;
        cycle = FindAnyObjectByType<StreetDayNight>();
        if (!camera || !cycle || !cycle.Sun || !camera.TryGetComponent<HDAdditionalCameraData>(out var cameraData))
        {
            errors.Add("The StreetDayNight component, directional sunlight, or HDRP player camera was unavailable.");
            Finish(false);
            yield break;
        }

        report.hdrp = GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;
        report.quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
        walk = camera.GetComponentInParent<FirstPersonWalk>();
        session = camera.GetComponentInParent<StreetSession>();
        player = camera.GetComponentInParent<CharacterController>();
        report.playerControlsConfigured = walk && walk.isActiveAndEnabled && session && session.isActiveAndEnabled &&
            player && player.enabled;
        ReadBins();
        camera.enabled = false;
        if (walk)
            walk.enabled = false;
        if (session)
            session.enabled = false;

        if (player && player.enabled)
        {
            for (int index = 0; index < 28 && !report.grounded; index++)
            {
                CollisionFlags hit = player.Move(Vector3.down * .07f);
                report.grounded = player.isGrounded || (hit & CollisionFlags.Below) != 0;
                yield return null;
            }
            Vector3 start = player.transform.position;
            for (int index = 0; index < 18; index++)
            {
                player.Move(player.transform.forward * .08f + Vector3.down * .02f);
                yield return null;
            }
            Vector3 displacement = player.transform.position - start;
            report.walkingDistance = Vector3.ProjectOnPlane(displacement, Vector3.up).magnitude;
            player.enabled = false;
        }

        cycle.Paused = false;
        float timeBefore = cycle.CurrentHour;
        float startTime = Time.realtimeSinceStartup;
        yield return new WaitForSecondsRealtime(1.5f);
        report.actualSeconds = Time.realtimeSinceStartup - startTime;
        report.simulatedClockAdvance = Mathf.Repeat(cycle.CurrentHour - timeBefore, 24f);
        report.clockAdvances = report.simulatedClockAdvance > .001f && report.simulatedClockAdvance < .25f;
        cycle.Paused = true;
        MeasureContinuity();

        var block = FindAnyObjectByType<PilotBlock>();
        Transform view = block && block.inspectionViews != null ? block.inspectionViews.FirstOrDefault(point => point) : null;
        Vector3 streetPosition = view ? view.position : new Vector3(-44, 7.5f, 22);
        Quaternion streetRotation = view ? view.rotation : Quaternion.LookRotation(new Vector3(-9, 3, -18) - streetPosition);
        float dawn = cycle.SunriseHour + .45f;
        float dusk = cycle.SunsetHour - .45f;
        float day = (cycle.SunriseHour + cycle.SunsetHour) * .5f;
        float night = Mathf.Repeat((cycle.SunsetHour + cycle.SunriseHour + 24f) * .5f, 24f);

        target = new RenderTexture(report.width, report.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "Day and night QA",
            antiAliasing = 1,
            useDynamicScale = false
        };
        var request = new RenderPipeline.StandardRequest { destination = target };
        if (!target.Create() || !RenderPipeline.SupportsRenderRequest(camera, request))
        {
            errors.Add("HDRP StandardRequest could not create the capture target.");
            Finish(false);
            yield break;
        }
        image = new Texture2D(report.width, report.height, TextureFormat.RGB24, false);

        var phases = new List<Phase>();
        foreach (var shot in new[]
        {
            (name: "01_dawn.png", hour: dawn, rotation: streetRotation),
            (name: "02_day.png", hour: day, rotation: streetRotation),
            (name: "03_dusk.png", hour: dusk, rotation: streetRotation),
            (name: "04_night.png", hour: night, rotation: streetRotation),
            (name: "05_night_sky.png", hour: night, rotation: Quaternion.Euler(-51f, streetRotation.eulerAngles.y - 24f, 0f))
        })
        {
            cycle.SetHour(shot.hour);
            camera.transform.SetPositionAndRotation(streetPosition, shot.rotation);
            for (int sample = 0; sample < 22; sample++)
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                yield return null;
            }
            Phase phase = Capture(shot.name);
            phases.Add(phase);
            report.phases = phases.ToArray();
            WriteReport();
        }

        Phase morning = phases[0], midday = phases[1], evening = phases[2], dark = phases[3], sky = phases[4];
        report.daylightShadowsEnabled = new[] { morning, midday, evening }.All(phase =>
            phase.sunEnabled && phase.sunCastsShadows && phase.hdrpShadowMaps && phase.sunShadowStrength > 0f && phase.sunLux > 0f) &&
            Mathf.Abs(Mathf.DeltaAngle(morning.sunAzimuth, evening.sunAzimuth)) > 20f;
        report.starsFade = dark.stars > .35f && midday.stars < .05f &&
            morning.stars < dark.stars && evening.stars < dark.stars && sky.hdrpStarMultiplier > midday.hdrpStarMultiplier;
        report.nativeStarCubemap = sky.nativeStarCubemap;
        report.dayNightExposureDistinct = midday.meanLuma > dark.meanLuma * 1.25f &&
            phases.All(phase => phase.magentaPercent < .15f && phase.variedPixelPercent > 2f);

        Finish(report.hdrp && report.playerControlsConfigured && report.grounded && report.walkingDistance > .75f &&
            report.clockAdvances && report.continuousSun && report.daylightShadowsEnabled && report.starsFade &&
            report.nativeStarCubemap && report.dayNightExposureDistinct && report.containers == 3 && report.litterBins == 3 &&
            report.visibleBinRenderers == 0 && report.enabledBinColliders == 0);
    }

    private void MeasureContinuity()
    {
        const int steps = 288;
        float maximumLux = 0f, earlierLux = 0f, maxLuxStep = 0f, earlierStars = 0f;
        Quaternion earlierRotation = Quaternion.identity;
        for (int index = 0; index <= steps; index++)
        {
            cycle.SetHour(index * (24f / steps));
            var sunlight = cycle.Sun;
            maximumLux = Mathf.Max(maximumLux, sunlight.intensity);
            if (index > 0)
            {
                report.maxSunRotationStepDegrees = Mathf.Max(report.maxSunRotationStepDegrees,
                    Quaternion.Angle(sunlight.transform.rotation, earlierRotation));
                maxLuxStep = Mathf.Max(maxLuxStep, Mathf.Abs(sunlight.intensity - earlierLux));
                report.maximumStarVisibilityStep = Mathf.Max(report.maximumStarVisibilityStep,
                    Mathf.Abs(cycle.StarVisibility - earlierStars));
            }
            float actualElevation = Mathf.Asin(Mathf.Clamp(-sunlight.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            report.maximumSunBearingError = Mathf.Max(report.maximumSunBearingError,
                Mathf.Abs(actualElevation - cycle.SunElevation));
            earlierRotation = sunlight.transform.rotation;
            earlierLux = sunlight.intensity;
            earlierStars = cycle.StarVisibility;
        }

        report.maxSunIntensityStepFraction = maximumLux > 0f ? maxLuxStep / maximumLux : 1f;
        report.continuousSun = report.maxSunRotationStepDegrees < 5f &&
            report.maxSunIntensityStepFraction < .25f && report.maximumStarVisibilityStep < .35f &&
            report.maximumSunBearingError < .5f;
    }

    private Phase Capture(string filename)
    {
        var sun = cycle.Sun;
        var phase = new Phase
        {
            screenshot = filename, hour = cycle.CurrentHour, sunElevation = cycle.SunElevation,
            renderedSunElevation = Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg,
            sunAzimuth = Mathf.Atan2(-sun.transform.forward.x, -sun.transform.forward.z) * Mathf.Rad2Deg,
            sunLux = sun.intensity, sunShadowStrength = sun.shadowStrength, sunEnabled = sun.enabled,
            sunCastsShadows = sun.shadows != LightShadows.None,
            stars = cycle.StarVisibility
        };

        var cameraType = typeof(HDCamera);
        var channelType = cameraType.GetNestedType("HistoryChannel", BindingFlags.NonPublic);
        if (channelType != null && Enum.IsDefined(channelType, "CustomUserHistory0"))
        {
            var get = cameraType.GetMethod("GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(Camera), typeof(int), channelType }, null);
            var hdrpCamera = get?.Invoke(null, new[] { (object)camera, 0, Enum.Parse(channelType, "CustomUserHistory0") }) as HDCamera;
            if (hdrpCamera != null)
            {
                phase.hdrpShadowMaps = hdrpCamera.frameSettings.IsEnabled(FrameSettingsField.ShadowMaps);
                var stack = hdrpCamera.volumeStack;
                var sky = stack.GetComponent<PhysicallyBasedSky>();
                phase.visualSky = stack.GetComponent<VisualEnvironment>()?.skyType.value.ToString();
                phase.nativeStarCubemap = sky && sky.spaceEmissionTexture.value != null;
                phase.hdrpStarMultiplier = sky ? sky.spaceEmissionMultiplier.value : 0f;
            }
        }

        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            image.Apply(false);
        }
        finally { RenderTexture.active = previous; }

        MeasureImage(image.GetPixels32(), image.width, phase, filename == "05_night_sky.png");
        File.WriteAllBytes(Path.Combine(outputDirectory, filename), image.EncodeToPNG());
        return phase;
    }

    private static void MeasureImage(Color32[] pixels, int width, Phase phase, bool skyOnly)
    {
        var histogram = new int[256];
        var luma = new byte[pixels.Length];
        int dark = 0, magenta = 0;
        long total = 0;
        for (int index = 0; index < pixels.Length; index++)
        {
            Color32 pixel = pixels[index];
            int value = (54 * pixel.r + 183 * pixel.g + 19 * pixel.b) >> 8;
            luma[index] = (byte)value;
            histogram[value]++;
            total += value;
            if (value <= 12)
                dark++;
            if (pixel.r > 185 && pixel.b > 170 && pixel.g < 120 && pixel.r - pixel.g > 85 && pixel.b - pixel.g > 70)
                magenta++;
        }
        phase.meanLuma = (float)total / pixels.Length;
        phase.medianLuma = Percentile(histogram, pixels.Length / 2);
        phase.p95Luma = Percentile(histogram, (int)(pixels.Length * .95f));
        phase.darkPercent = 100f * dark / pixels.Length;
        phase.magentaPercent = 100f * magenta / pixels.Length;
        phase.variedPixelPercent = 100f * (pixels.Length - histogram.Max()) / pixels.Length;

        if (!skyOnly)
            return;
        int height = pixels.Length / width;
        for (int y = 2; y < height - 2; y++)
        {
            for (int x = 2; x < width - 2; x++)
            {
                int index = y * width + x;
                int localMean = (luma[index - 2] + luma[index + 2] +
                    luma[index - 2 * width] + luma[index + 2 * width]) / 4;
                if (luma[index] >= 25 && luma[index] >= localMean + 10 &&
                    luma[index] > luma[index - 1] && luma[index] >= luma[index + 1] &&
                    luma[index] > luma[index - width] && luma[index] >= luma[index + width])
                    phase.isolatedSkyHighlights++;
            }
        }
    }

    private static int Percentile(int[] histogram, int index)
    {
        int cumulative = 0;
        for (int bin = 0; bin < histogram.Length; bin++)
        {
            cumulative += histogram[bin];
            if (cumulative > index)
                return bin;
        }
        return 255;
    }

    private void ReadBins()
    {
        foreach (var transform in FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (transform.gameObject.scene != gameObject.scene)
                continue;
            switch (transform.name)
            {
                case "Container1100":
                    report.containers++;
                    break;
                case "LitterBin":
                    report.litterBins++;
                    break;
                default:
                    continue;
            }
            report.visibleBinRenderers += transform.GetComponentsInChildren<Renderer>(true)
                .Count(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff);
            report.enabledBinColliders += transform.GetComponentsInChildren<Collider>(true)
                .Count(collider => collider.enabled && collider.gameObject.activeInHierarchy);
        }
    }

    private void CollectError(string message, string trace, LogType type)
    {
        if (errors.Count < 20 && (type == LogType.Error || type == LogType.Assert || type == LogType.Exception))
            errors.Add(message);
    }

    private void WriteReport()
    {
        report.errors = errors.ToArray();
        File.WriteAllText(Path.Combine(outputDirectory, "day_night_report.json"), JsonUtility.ToJson(report, true));
    }

    private void Finish(bool passed)
    {
        report.passed = passed && errors.Count == 0;
        report.status = "finished";
        WriteReport();
        Debug.Log($"STREET_DAY_NIGHT_QA passed={report.passed} photos={report.phases?.Length ?? 0}");
        Application.Quit(report.passed ? 0 : 2);
    }

    private IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(150f);
        if (report.status == "running")
        {
            errors.Add("The day/night diagnostic timed out.");
            Finish(false);
        }
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= CollectError;
        if (target)
        {
            target.Release();
            Destroy(target);
        }
        if (image)
            Destroy(image);
    }

    private static string ReadOption(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ReadDimension(string[] args, string name, int fallback) =>
        int.TryParse(ReadOption(args, name), out int value) ? Mathf.Clamp(value, 480, 3840) : fallback;
}
