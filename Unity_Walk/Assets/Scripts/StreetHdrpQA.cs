using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public sealed class StreetHdrpQA : MonoBehaviour
{
    private const int WarmupFrames = 90;
    private const int SampleFrames = 180;
    private const int OffscreenWidth = 1600;
    private const int OffscreenHeight = 900;

    [Serializable]
    private sealed class Report
    {
        public bool passed, hdrp, expectedRayTracing, rayTracingShaders, inlineRayTracing;
        public bool rayTracingFrameSettings, rayTracedGiConfigured, rayTracedSunShadowsConfigured;
        public bool volumetricFog, screenSpaceReflections, ambientOcclusion;
        public bool adaptiveProbeVolumeStateObserved, adaptiveProbeVolumeEnabled, adaptiveProbeVolumeDataLoaded;
        public bool graphicsDriverAllocationSampleAvailable;
        public long adaptiveProbeVolumeEstimatedVideoMemoryBytes = -1, graphicsDriverAllocatedBytes = -1;
        public bool dlssSelected, dlssSelectionInspectable, cameraData, playerControlsConfigured, playerGrounded;
        public string scene, engine, gpu, graphicsApi, pipeline, quality, status, measurement;
        public bool offscreen, dlssDetected, profileEnabled, frameTimingEnabled, developmentBuild;
        public string requestedAntialiasing, activeAntialiasing, colorSpace, motionPath, qualityOverride;
        public int requestedVSync, targetFrameRate;
        public ViewReport[] motionFrames;
        public int width, height, vramMb, renderers, residentMaterials, alphaClippedMaterials;
        public int errors;
        public string[] errorSamples, invalidMaterials;
        public ViewReport[] viewpoints;
    }

    [Serializable]
    private sealed class ViewReport
    {
        public string screenshot;
        public int renderedFrames, timedFrames;
        public float averageFps, medianFrameMs, p95FrameMs;
        public float suspectMagentaPercent, nearBlackPercent, nearWhitePercent;
        public bool variedImage;
        public ViewProfile profile;
    }


    [Serializable]
    private sealed class SampleStatistics
    {
        public int samples;
        public double meanMs = -1, medianMs = -1, p95Ms = -1;

        public static SampleStatistics From(List<double> values)
        {
            var result = new SampleStatistics { samples = values.Count };
            if (values.Count == 0)
                return result;

            values.Sort();
            result.meanMs = values.Average();
            int middle = values.Count / 2;
            result.medianMs = values.Count % 2 != 0 ? values[middle] : (values[middle - 1] + values[middle]) * .5;
            result.p95Ms = values[(int)Math.Ceiling(.95 * values.Count) - 1];
            return result;
        }
    }

    [Serializable]
    private sealed class ViewProfile
    {
        public int internalResolutionSamples, internalMinWidth, internalMinHeight, internalMaxWidth, internalMaxHeight;
        public bool nativeResolutionEverySample;
        public string effectiveAntialiasing, cameraMarker;
        public SampleStatistics cpuFrame, cpuMainThread, cpuRenderThread, cpuPresentWait, gpuFrame;
        public SampleStatistics offscreenSubmitWall, hdrpCameraMain, hdrpCameraRender, hdrpCameraGpu;
    }

    private sealed class FrameProfile
    {
        private readonly bool frameTimingEnabled;
        private readonly FrameTiming[] timings = new FrameTiming[8];
        private readonly List<double> cpuFrame = new(SampleFrames);
        private readonly List<double> cpuMain = new(SampleFrames);
        private readonly List<double> cpuRender = new(SampleFrames);
        private readonly List<double> presentWait = new(SampleFrames);
        private readonly List<double> gpuFrame = new(SampleFrames);
        private readonly List<double> submitWall = new(SampleFrames);
        private readonly List<double> markerMain = new(SampleFrames);
        private readonly List<double> markerRender = new(SampleFrames);
        private readonly List<double> markerGpu = new(SampleFrames);
        private ulong lastFrameTimestamp;
        private int resolutionSamples, minWidth = int.MaxValue, minHeight = int.MaxValue, maxWidth, maxHeight;

        public FrameProfile(bool frameTimingEnabled)
        {
            this.frameTimingEnabled = frameTimingEnabled;
        }

        public void Observe(HDCamera camera, ProfilingSampler marker, double submitWallMs)
        {
            if (camera != null && camera.actualWidth > 0 && camera.actualHeight > 0)
            {
                resolutionSamples++;
                minWidth = Math.Min(minWidth, camera.actualWidth);
                minHeight = Math.Min(minHeight, camera.actualHeight);
                maxWidth = Math.Max(maxWidth, camera.actualWidth);
                maxHeight = Math.Max(maxHeight, camera.actualHeight);
            }

            AddAvailable(submitWall, submitWallMs);
            if (marker != null)
            {
                AddAvailable(markerMain, marker.inlineCpuElapsedTime);
                AddAvailable(markerRender, marker.cpuElapsedTime);
                AddAvailable(markerGpu, marker.gpuElapsedTime);
            }
            if (!frameTimingEnabled)
                return;

            FrameTimingManager.CaptureFrameTimings();
            int count = (int)FrameTimingManager.GetLatestTimings((uint)timings.Length, timings);
            for (int index = count - 1; index >= 0; index--)
            {
                FrameTiming sample = timings[index];
                if (sample.frameStartTimestamp <= lastFrameTimestamp)
                    continue;
                lastFrameTimestamp = sample.frameStartTimestamp;
                AddAvailable(cpuFrame, sample.cpuFrameTime);
                AddAvailable(cpuMain, sample.cpuMainThreadFrameTime);
                AddAvailable(cpuRender, sample.cpuRenderThreadFrameTime);
                if (sample.cpuMainThreadFrameTime > 0 && sample.cpuMainThreadPresentWaitTime >= 0)
                    presentWait.Add(sample.cpuMainThreadPresentWaitTime);
                AddAvailable(gpuFrame, sample.gpuFrameTime);
            }
        }

        private static void AddAvailable(List<double> samples, double milliseconds)
        {
            if (milliseconds > 0 && !double.IsInfinity(milliseconds))
                samples.Add(milliseconds);
        }

        public ViewProfile Finish(int outputWidth, int outputHeight, string antialiasing, string markerName)
        {
            return new ViewProfile
            {
                internalResolutionSamples = resolutionSamples,
                internalMinWidth = resolutionSamples > 0 ? minWidth : 0,
                internalMinHeight = resolutionSamples > 0 ? minHeight : 0,
                internalMaxWidth = maxWidth,
                internalMaxHeight = maxHeight,
                nativeResolutionEverySample = resolutionSamples > 0 &&
                    minWidth == outputWidth && maxWidth == outputWidth && minHeight == outputHeight && maxHeight == outputHeight,
                effectiveAntialiasing = antialiasing,
                cameraMarker = markerName,
                cpuFrame = SampleStatistics.From(cpuFrame),
                cpuMainThread = SampleStatistics.From(cpuMain),
                cpuRenderThread = SampleStatistics.From(cpuRender),
                cpuPresentWait = SampleStatistics.From(presentWait),
                gpuFrame = SampleStatistics.From(gpuFrame),
                offscreenSubmitWall = SampleStatistics.From(submitWall),
                hdrpCameraMain = SampleStatistics.From(markerMain),
                hdrpCameraRender = SampleStatistics.From(markerRender),
                hdrpCameraGpu = SampleStatistics.From(markerGpu)
            };
        }
    }

    private readonly List<string> errorSamples = new();
    private int errors, renderedFrames;
    private Camera cameraToMeasure;
    private RenderPipeline.StandardRequest offscreenRequest;
    private Texture2D synchronizationPixel;
    private bool submittingOffscreen;
    private Report activeProfile;
    private HDCamera profileCamera;
    private ProfilingSampler cameraMarker;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartIfRequested()
    {
        var arguments = Environment.GetCommandLineArgs();
        if (!(arguments.Contains("-hdrpQA") || arguments.Contains("-hdrpQAOffscreen")) || arguments.Contains("-streetQA"))
            return;

        int quality = Array.IndexOf(QualitySettings.names, ReadOptionalValue(arguments, "-hdrpQAQuality"));
        if (quality >= 0)
            QualitySettings.SetQualityLevel(quality, true);

        var verifier = new GameObject("HDRP verification");
        DontDestroyOnLoad(verifier);
        verifier.AddComponent<StreetHdrpQA>();
    }

    private static string ReadOptionalValue(string[] arguments, string flag)
    {
        int index = Array.IndexOf(arguments, flag);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    private void OnEnable()
    {
        Application.logMessageReceived += RecordError;
        RenderPipelineManager.endCameraRendering += CountCameraFrame;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= RecordError;
        RenderPipelineManager.endCameraRendering -= CountCameraFrame;
        if (cameraMarker != null)
            cameraMarker.enableRecording = false;
        if (offscreenRequest?.destination)
        {
            offscreenRequest.destination.Release();
            Destroy(offscreenRequest.destination);
        }
        if (synchronizationPixel)
            Destroy(synchronizationPixel);
    }

    private void RecordError(string message, string stackTrace, LogType type)
    {
        if (type is not (LogType.Error or LogType.Assert or LogType.Exception))
            return;

        errors++;
        if (errorSamples.Count < 10)
            errorSamples.Add(message);
    }

    private void CountCameraFrame(ScriptableRenderContext context, Camera renderedCamera)
    {
        if (renderedCamera == cameraToMeasure && (offscreenRequest == null || submittingOffscreen))
            renderedFrames++;
    }

    private IEnumerator Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        int outputIndex = Array.IndexOf(args, "-qaOutput");
        string output = outputIndex >= 0 && outputIndex + 1 < args.Length
            ? Path.GetFullPath(args[outputIndex + 1])
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "QA_HDRP"));
        Directory.CreateDirectory(output);
        string reportPath = Path.Combine(output, "hdrp_report.json");
        File.WriteAllText(reportPath, "{\"status\":\"running\",\"passed\":false}");
        string qualityOverride = ReadOptionalValue(args, "-hdrpQAQuality");
        if (args.Contains("-hdrpQAQuality") && !QualitySettings.names.Contains(qualityOverride))
        {
            Finish(new Report
            {
                status = "requested quality unavailable",
                qualityOverride = qualityOverride
            }, reportPath);
            yield break;
        }

        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        int sceneIndex = Array.IndexOf(args, "-hdrpQAScene");
        if (sceneIndex >= 0)
        {
            string requested = sceneIndex + 1 < args.Length ? args[sceneIndex + 1] : "";
            if (requested.Length == 0 || !Application.CanStreamedLevelBeLoaded(requested))
            {
                Finish(new Report { status = "scene unavailable", scene = requested }, reportPath);
                yield break;
            }
            if (requested != SceneManager.GetActiveScene().name)
                yield return SceneManager.LoadSceneAsync(requested, LoadSceneMode.Single);
        }
        yield return null;

        bool offscreen = args.Contains("-hdrpQAOffscreen");
        bool profileEnabled = args.Contains("-hdrpQAProfile");
        var camera = Camera.main;
        if (!camera)
            camera = FindObjectsByType<Camera>().FirstOrDefault(candidate => offscreen || candidate.isActiveAndEnabled);
        cameraToMeasure = camera;
        bool usableCamera = camera && camera.gameObject.activeInHierarchy && (offscreen || camera.enabled);
        bool usableRequest = !offscreen || usableCamera && PrepareOffscreenRequest(camera, args, profileEnabled);
        double pipelineDeadline = Time.realtimeSinceStartupAsDouble + 20;
        while (!offscreen && RenderPipelineManager.currentPipeline is not HDRenderPipeline &&
            Time.realtimeSinceStartupAsDouble < pipelineDeadline)
            yield return null;
        var asset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
        var report = new Report
        {
            scene = SceneManager.GetActiveScene().name,
            engine = Application.unityVersion,
            gpu = SystemInfo.graphicsDeviceName,
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            vramMb = SystemInfo.graphicsMemorySize,
            width = offscreen && offscreenRequest?.destination ? offscreenRequest.destination.width : Screen.width,
            height = offscreen && offscreenRequest?.destination ? offscreenRequest.destination.height : Screen.height,
            offscreen = offscreen,
            profileEnabled = profileEnabled,
            frameTimingEnabled = profileEnabled && FrameTimingManager.IsFeatureEnabled(),
            developmentBuild = Debug.isDebugBuild,
            colorSpace = QualitySettings.activeColorSpace.ToString(),
            requestedVSync = QualitySettings.vSyncCount,
            targetFrameRate = Application.targetFrameRate,
            requestedAntialiasing = args.Contains("-streetHDRPDLAA") ? "DLAA" :
                args.Contains("-streetHDRPDLSS") ? "DLSS Quality" : "project default",
            measurement = offscreen
                ? "HDRP StandardRequest for every frame, with GPU readback fences before and after each sample set. Frame intervals include player updates; there is no backbuffer presentation or screenshot encoding in the timings."
                : "Normal camera frames in the Windows player, without screenshot encoding in the timings.",
            pipeline = asset ? asset.name : "",
            quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
            qualityOverride = qualityOverride,
            expectedRayTracing = args.Contains("-hdrpQAExpectRT"),
            rayTracingShaders = SystemInfo.supportsRayTracingShaders,
            inlineRayTracing = SystemInfo.supportsInlineRayTracing,
            hdrp = asset && RenderPipelineManager.currentPipeline is HDRenderPipeline,
            status = "no active HDRP camera"
        };
        if (!report.hdrp || !usableCamera || !usableRequest)
        {
            if (offscreen && !usableRequest)
                report.status = "offscreen HDRP render request unavailable";
            Finish(report, reportPath);
            yield break;
        }
        if (offscreen)
            camera.enabled = false;

        var walk = camera.GetComponentInParent<FirstPersonWalk>();
        var controller = camera.GetComponentInParent<CharacterController>();
        var session = camera.GetComponentInParent<StreetSession>();
        bool mainStreet = SceneManager.GetActiveScene().name == "Street";
        yield return new WaitForSecondsRealtime(1.25f);
        report.playerControlsConfigured = walk && walk.isActiveAndEnabled && controller && controller.enabled &&
            (!mainStreet || session && session.isActiveAndEnabled);
        report.playerGrounded = controller && controller.isGrounded;
        // In an uncapped offscreen loop the ordinary gravity step can be below the
        // controller's minimum movement. Probe support using the street QA step.
        if (controller && controller.enabled)
        {
            for (int settleStep = 0; settleStep < 35 && !report.playerGrounded; settleStep++)
            {
                CollisionFlags collision = controller.Move(Vector3.down * .06f);
                report.playerGrounded = controller.isGrounded || (collision & CollisionFlags.Below) != 0;
                if (!report.playerGrounded)
                    yield return null;
            }
        }

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include).Where(r =>
            r.gameObject.scene == camera.gameObject.scene).ToArray();
        var materials = new HashSet<Material>();
        var invalid = new HashSet<string>();
        foreach (var renderer in renderers)
        {
            Material[] slots = renderer.sharedMaterials;
            if (slots.Length == 0 && renderer is MeshRenderer && renderer.enabled && renderer.gameObject.activeInHierarchy)
                invalid.Add(renderer.name + " (no slots)");
            foreach (var material in slots)
            {
                if (material)
                    materials.Add(material);
                else if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    invalid.Add(renderer.name + " (missing material)");
            }
        }
        foreach (var material in materials)
        {
            if (!material.shader || !material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader")
                invalid.Add(material.name + " (" + (material.shader ? material.shader.name : "missing shader") + ")");
        }
        report.renderers = renderers.Length;
        report.residentMaterials = materials.Count;
        report.invalidMaterials = invalid.OrderBy(name => name).ToArray();
        report.alphaClippedMaterials = materials.Count(material =>
            material.IsKeywordEnabled("_ALPHATEST_ON") ||
            material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f ||
            material.GetTag("RenderType", false) == "TransparentCutout");

        var views = new List<ViewReport>();
        var spawn = new ViewReport { screenshot = offscreen ? "00_spawn_offscreen.png" : "00_spawn_hud.png" };
        if (offscreen)
        {
            // A render request prepares a distinct HDRP camera history and allows temporal effects to settle.
            for (int index = 0; index < 8; index++)
            {
                RenderOffscreen();
                yield return null;
            }
        }
        else
            yield return new WaitForEndOfFrame();
        Capture(output, spawn);
        views.Add(spawn);
        ReadFeatures(camera, report);
        if (profileEnabled)
        {
            activeProfile = report;
            profileCamera = offscreen ? GetRequestCamera(camera) : HDCamera.GetOrCreate(camera);
            var marker = typeof(HDCamera).GetProperty("profilingSampler", BindingFlags.NonPublic | BindingFlags.Instance);
            cameraMarker = profileCamera != null ? marker?.GetValue(profileCamera) as ProfilingSampler : null;
            if (cameraMarker != null)
                cameraMarker.enableRecording = true;
        }
        if (offscreen && !report.cameraData && mainStreet)
            Debug.LogError("HDRP StandardRequest camera history is unavailable.");

        if (walk)
            walk.enabled = false;
        if (session)
            session.enabled = false;
        if (controller)
            controller.enabled = false;

        var block = FindAnyObjectByType<PilotBlock>();
        var points = block && block.inspectionViews != null
            ? block.inspectionViews.Where(view => view).Take(4).ToArray()
            : Array.Empty<Transform>();
        if (points.Length == 0)
        {
            var view = new ViewReport { screenshot = "01_camera.png" };
            yield return MeasureView(output, view);
            views.Add(view);
        }
        else
        {
            string[] names = { "01_street.png", "02_house5.png", "03_courtyard.png", "04_overview.png" };
            for (int index = 0; index < points.Length; index++)
            {
                camera.transform.SetPositionAndRotation(points[index].position, points[index].rotation);
                var view = new ViewReport { screenshot = names[index] };
                yield return MeasureView(output, view);
                views.Add(view);
            }
        }

        if (profileEnabled && mainStreet)
        {
            report.motionPath = "180 fixed poses around (-31.5,1.75,20), lateral sine ±0.55 m and yaw ±9 degrees, looking toward (-4,3,-43).";
            var motion = new ViewReport { screenshot = "05_motion_end.png" };
            yield return MeasureView(output, motion, motion: true);
            views.Add(motion);
            var motionFrames = new List<ViewReport>();
            yield return CaptureMotionSamples(output, motionFrames);
            report.motionFrames = motionFrames.ToArray();
        }

        report.viewpoints = views.ToArray();
        report.status = "finished";
        report.passed = report.invalidMaterials.Length == 0 && errors == 0 &&
            (!mainStreet || report.cameraData && report.playerControlsConfigured && report.playerGrounded && report.volumetricFog) &&
            (report.expectedRayTracing ? report.rayTracingShaders && report.rayTracedGiConfigured
                : !report.rayTracedGiConfigured && !report.rayTracedSunShadowsConfigured) &&
            views.All(view => view.variedImage && view.suspectMagentaPercent < .1f) &&
            views.Skip(1).All(view => view.renderedFrames >= SampleFrames && view.averageFps > 0);
        Finish(report, reportPath);
    }

    private IEnumerator MeasureView(string output, ViewReport view, bool motion = false)
    {
        bool offscreen = offscreenRequest != null;
        if (motion)
            SetMotionPose(0);
        for (int index = 0; index < WarmupFrames; index++)
        {
            if (offscreen)
                RenderOffscreen();
            if (activeProfile?.frameTimingEnabled == true)
                FrameTimingManager.CaptureFrameTimings();
            yield return null;
        }

        var samples = new float[SampleFrames];
        var profiler = activeProfile != null ? new FrameProfile(activeProfile.frameTimingEnabled) : null;
        int before = renderedFrames;
        if (offscreen)
        {
            ReadOffscreen(synchronizationPixel);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int index = 0; index < samples.Length; index++)
            {
                if (motion)
                    SetMotionPose(index);
                long submitStart = profiler != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                RenderOffscreen();
                double submitWall = profiler != null ? ElapsedMilliseconds(submitStart) : 0;
                if (index + 1 == samples.Length)
                    ReadOffscreen(synchronizationPixel);
                else
                    yield return null;
                profiler?.Observe(profileCamera, cameraMarker, submitWall);
                long end = System.Diagnostics.Stopwatch.GetTimestamp();
                samples[index] = (float)TicksToMilliseconds(end - start);
                start = end;
            }
        }
        else
        {
            for (int index = 0; index < samples.Length; index++)
            {
                if (motion)
                    SetMotionPose(index);
                yield return null;
                profiler?.Observe(profileCamera, cameraMarker, 0);
                samples[index] = Time.unscaledDeltaTime * 1000;
            }
        }

        view.renderedFrames = renderedFrames - before;
        view.timedFrames = samples.Length;
        if (profiler != null)
            view.profile = profiler.Finish(activeProfile.width, activeProfile.height, activeProfile.activeAntialiasing, cameraMarker?.name);
        Array.Sort(samples);
        float mean = samples.Average();
        view.averageFps = mean > 0 ? 1000 / mean : 0;
        view.medianFrameMs = samples[samples.Length / 2];
        view.p95FrameMs = samples[Mathf.CeilToInt(.95f * samples.Length) - 1];
        if (!offscreen)
            yield return new WaitForEndOfFrame();
        Capture(output, view);
    }


    private static double TicksToMilliseconds(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    private static double ElapsedMilliseconds(long start) => TicksToMilliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - start);

    private void SetMotionPose(int frame)
    {
        var origin = new Vector3(-31.5f, 1.75f, 20f);
        var direction = new Vector3(-4f, 3f, -43f) - origin;
        Quaternion facing = Quaternion.LookRotation(direction);
        float travel = Mathf.Sin(2 * Mathf.PI * frame / (SampleFrames - 1));
        Vector3 position = origin + facing * Vector3.right * (.55f * travel);
        Quaternion rotation = Quaternion.AngleAxis(9f * travel, Vector3.up) * facing;
        cameraToMeasure.transform.SetPositionAndRotation(position, rotation);
    }

    private IEnumerator CaptureMotionSamples(string output, List<ViewReport> result)
    {
        bool offscreen = offscreenRequest != null;
        SetMotionPose(0);
        for (int frame = 0; frame < WarmupFrames; frame++)
        {
            if (offscreen)
                RenderOffscreen();
            yield return null;
        }

        const int referenceFrame = SampleFrames / 2;
        for (int frame = 0; frame <= referenceFrame + 1; frame++)
        {
            SetMotionPose(frame);
            if (offscreen)
                RenderOffscreen();
            if (frame >= referenceFrame - 1)
            {
                if (!offscreen)
                    yield return new WaitForEndOfFrame();
                var image = new ViewReport { screenshot = $"motion_frame_{frame:000}.png" };
                Capture(output, image);
                result.Add(image);
            }
            yield return null;
        }

        SetMotionPose(referenceFrame);
        for (int frame = 0; frame < WarmupFrames; frame++)
        {
            if (offscreen)
                RenderOffscreen();
            yield return null;
        }
        if (!offscreen)
            yield return new WaitForEndOfFrame();
        var reference = new ViewReport { screenshot = "motion_static_reference_090.png" };
        Capture(output, reference);
        result.Add(reference);
    }

    private static int ReadScreenDimension(string[] args, string flag, int defaultValue)
    {
        int index = Array.IndexOf(args, flag);
        if (index < 0)
            return defaultValue;
        if (index + 1 < args.Length && int.TryParse(args[index + 1], out int value) &&
            value >= 64 && value <= SystemInfo.maxTextureSize)
            return value;
        Debug.LogError($"Invalid profiling dimension {flag}. Use an integer from 64 to {SystemInfo.maxTextureSize}.");
        return 0;
    }

    private bool PrepareOffscreenRequest(Camera camera, string[] args, bool profileEnabled)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
            GraphicsSettings.currentRenderPipeline is not HDRenderPipelineAsset)
            return false;

        int width = profileEnabled ? ReadScreenDimension(args, "-screen-width", OffscreenWidth) : OffscreenWidth;
        int height = profileEnabled ? ReadScreenDimension(args, "-screen-height", OffscreenHeight) : OffscreenHeight;
        if (width == 0 || height == 0)
            return false;

        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "HDRP QA offscreen target",
            antiAliasing = 1,
            useDynamicScale = false
        };
        offscreenRequest = new RenderPipeline.StandardRequest { destination = target };
        if (!target.Create() || !RenderPipeline.SupportsRenderRequest(camera, offscreenRequest))
            return false;

        synchronizationPixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
        return true;
    }

    private void RenderOffscreen()
    {
        submittingOffscreen = true;
        try
        {
            RenderPipeline.SubmitRenderRequest(cameraToMeasure, offscreenRequest);
        }
        finally
        {
            submittingOffscreen = false;
        }
    }

    private void ReadOffscreen(Texture2D destination)
    {
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = offscreenRequest.destination;
            destination.ReadPixels(new Rect(0, 0, destination.width, destination.height), 0, 0, false);
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    private static HDCamera GetRequestCamera(Camera camera)
    {
        // HDRP 17.6 renders StandardRequest into a separate camera history.
        var cameraType = typeof(HDCamera);
        var channelType = cameraType.GetNestedType("HistoryChannel", BindingFlags.NonPublic);
        if (channelType == null || !Enum.IsDefined(channelType, "CustomUserHistory0"))
            return null;
        var get = cameraType.GetMethod("GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static, null,
            new[] { typeof(Camera), typeof(int), channelType }, null);
        return get?.Invoke(null, new[] { (object)camera, 0, Enum.Parse(channelType, "CustomUserHistory0") }) as HDCamera;
    }

    private void ReadFeatures(Camera camera, Report report)
    {
        var cameraData = camera.GetComponent<HDAdditionalCameraData>();
        var hdCamera = offscreenRequest == null ? HDCamera.GetOrCreate(camera) : GetRequestCamera(camera);
        report.cameraData = cameraData && hdCamera != null;
        report.dlssDetected = HDDynamicResolutionPlatformCapabilities.DLSSDetected;
        if (hdCamera == null)
            return;

        var settings = hdCamera.frameSettings;
        var stack = hdCamera.volumeStack;
        var fog = stack.GetComponent<Fog>();
        var reflections = stack.GetComponent<ScreenSpaceReflection>();
        var ao = stack.GetComponent<ScreenSpaceAmbientOcclusion>();
        var gi = stack.GetComponent<GlobalIllumination>();
        var sun = RenderSettings.sun;
        var sunData = sun ? sun.GetComponent<HDAdditionalLightData>() : null;

        report.volumetricFog = fog && fog.enabled.value && fog.enableVolumetricFog.value &&
            settings.IsEnabled(FrameSettingsField.Volumetrics);
        report.screenSpaceReflections = reflections && reflections.enabled.value &&
            reflections.tracing.value != RayCastingMode.RayTracing && settings.IsEnabled(FrameSettingsField.SSR);
        report.ambientOcclusion = ao && ao.intensity.value > 0 && settings.IsEnabled(FrameSettingsField.SSAO);
        report.rayTracingFrameSettings = settings.IsEnabled(FrameSettingsField.RayTracing);
        report.rayTracedGiConfigured = report.rayTracingFrameSettings && gi && gi.enable.value &&
            gi.tracing.value != RayCastingMode.RayMarching && settings.IsEnabled(FrameSettingsField.SSGI);
        report.rayTracedSunShadowsConfigured = report.rayTracingFrameSettings && sunData &&
            sunData.useRayTracedShadows && settings.IsEnabled(FrameSettingsField.ScreenSpaceShadows);
        var dlssMethod = typeof(HDCamera).GetMethod("IsDLSSEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        report.dlssSelectionInspectable = dlssMethod != null;
        report.dlssSelected = dlssMethod?.Invoke(hdCamera, null) is true;
        report.activeAntialiasing = cameraData ? cameraData.antialiasing.ToString() : "camera default";
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (report.dlssSelected && cameraData && GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset asset)
        {
            uint quality = cameraData.deepLearningSuperSamplingUseCustomQualitySettings
                ? cameraData.deepLearningSuperSamplingQuality
                : asset.currentPlatformRenderPipelineSettings.dynamicResolutionSettings.DLSSPerfQualitySetting;
            var mode = (UnityEngine.NVIDIA.DLSSQuality)quality;
            report.activeAntialiasing = mode == UnityEngine.NVIDIA.DLSSQuality.DLAA ? "DLAA" : "DLSS " + mode;
        }
#endif
    }

    private void Capture(string output, ViewReport view)
    {
        bool offscreen = offscreenRequest != null;
        var image = offscreen
            ? new Texture2D(offscreenRequest.destination.width, offscreenRequest.destination.height, TextureFormat.RGB24, false)
            : ScreenCapture.CaptureScreenshotAsTexture();
        if (!image)
            return;

        try
        {
            if (offscreen)
                ReadOffscreen(image);
            int magenta = 0, nearBlack = 0, nearWhite = 0, darkest = 255, lightest = 0;
            Color32[] pixels = image.GetPixels32();
            foreach (var pixel in pixels)
            {
                if (pixel.r > 185 && pixel.b > 170 && pixel.g < 120 &&
                    pixel.r - pixel.g > 85 && pixel.b - pixel.g > 70)
                    magenta++;
                int luma = (54 * pixel.r + 183 * pixel.g + 19 * pixel.b) >> 8;
                if (luma <= 12)
                    nearBlack++;
                if (luma >= 247)
                    nearWhite++;
                darkest = Mathf.Min(darkest, luma);
                lightest = Mathf.Max(lightest, luma);
            }
            view.variedImage = lightest - darkest > 50;
            view.suspectMagentaPercent = 100f * magenta / pixels.Length;
            view.nearBlackPercent = 100f * nearBlack / pixels.Length;
            view.nearWhitePercent = 100f * nearWhite / pixels.Length;
            File.WriteAllBytes(Path.Combine(output, view.screenshot), image.EncodeToPNG());
        }
        finally
        {
            Destroy(image);
        }
    }

    private void ReadProbeAndGraphicsMemory(Report report)
    {
        if (!report.hdrp || !cameraToMeasure || report.viewpoints == null)
            return;

        var hdCamera = offscreenRequest == null ? HDCamera.GetOrCreate(cameraToMeasure) : GetRequestCamera(cameraToMeasure);
        if (hdCamera != null)
        {
            report.adaptiveProbeVolumeStateObserved = true;
            report.adaptiveProbeVolumeEnabled = hdCamera.frameSettings.IsEnabled(FrameSettingsField.AdaptiveProbeVolume);
            var probes = ProbeReferenceVolume.instance;
            report.adaptiveProbeVolumeDataLoaded = probes.DataHasBeenLoaded();
            int estimatedBytes = probes.GetVideoMemoryCost();
            if (estimatedBytes >= 0)
                report.adaptiveProbeVolumeEstimatedVideoMemoryBytes = estimatedBytes;
        }

        if (Debug.isDebugBuild || Application.isEditor)
        {
            long allocatedBytes = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
            if (allocatedBytes > 0)
            {
                report.graphicsDriverAllocatedBytes = allocatedBytes;
                report.graphicsDriverAllocationSampleAvailable = true;
            }
        }
    }

    private void Finish(Report report, string path)
    {
        ReadProbeAndGraphicsMemory(report);
        report.errors = errors;
        report.errorSamples = errorSamples.ToArray();
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        Debug.Log("HDRP_QA " + JsonUtility.ToJson(report));
        Application.Quit(report.passed ? 0 : 2);
    }
}
