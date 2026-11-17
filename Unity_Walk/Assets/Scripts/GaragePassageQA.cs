using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Read-only runtime validation and matched captures of a saved HDRP scene.
public sealed class GaragePassageQA : MonoBehaviour
{
    [Serializable]
    private sealed class Layout
    {
        public Shot[] views;
        public Lane[] lanes;
        public Barrier[] barriers;
    }

    [Serializable]
    private sealed class Shot
    {
        public string name;
        public Vector3 eye, lookAt;
    }

    [Serializable]
    private sealed class Lane
    {
        public string name;
        public Vector3[] ground;
        public float tolerance = .08f;
        public float width = 2.1f;
        public float headroom = 2.15f;
    }

    [Serializable]
    private sealed class Barrier
    {
        public string name, colliderNameContains;
        public Vector3 from, to;
    }

    [Serializable]
    private sealed class LaneResult
    {
        public string name, worstGround, blocker;
        public int samples, missingGround, excessiveGroundError, clearanceObstructions;
        public Vector3 worstPosition;
        public float maxHeightError, maximumExcessStep, measuredRise, walkEndError;
        public bool playerTraversal, passed;
    }

    [Serializable]
    private sealed class BarrierResult
    {
        public string name, collider;
        public Vector3 contact;
        public bool passed;
    }

    [Serializable]
    private sealed class ShotResult
    {
        public string name;
        public Vector3 eye, lookAt;
        public int timedFrames;
        public float offscreenAverageFps;
    }

    [Serializable]
    private sealed class Report
    {
        public string status = "running";
        public string scene, gpu, graphicsApi, hdrpQuality;
        public bool passed, captureOnly, wastePadHidden;
        public int width = 1600, height = 900;
        public float hour = 13f;
        public List<LaneResult> lanes = new();
        public List<BarrierResult> barriers = new();
        public List<ShotResult> screenshots = new();
        public List<string> errors = new();
    }

    private Report report;
    private string reportPath;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartIfRequested()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-garageQA") >= 0)
            new GameObject("Garage passage QA").AddComponent<GaragePassageQA>();
    }

    private IEnumerator Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = Path.GetFullPath(Argument(args, "-qaOutput") ??
            Path.Combine(Application.dataPath, "..", "QA_GaragePassage"));
        Directory.CreateDirectory(output);
        reportPath = Path.Combine(output, "garage_passage_qa.json");
        report = new Report
        {
            scene = gameObject.scene.name,
            gpu = SystemInfo.graphicsDeviceName,
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            hdrpQuality = QualitySettings.names[QualitySettings.GetQualityLevel()],
            captureOnly = Array.IndexOf(args, "-garageQACaptureOnly") >= 0
        };
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        Application.logMessageReceived += CaptureError;
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        yield return null;

        Layout layout;
        try
        {
            string path = Argument(args, "-garageQALayout");
            layout = path == null ? new Layout { views = DefaultViews() } :
                JsonUtility.FromJson<Layout>(File.ReadAllText(path));
            if (layout == null)
                throw new InvalidDataException("The garage QA layout could not be read.");
        }
        catch (Exception ex)
        {
            Finish(false, ex.Message);
            yield break;
        }
        if (!report.captureOnly && (layout.lanes == null || layout.lanes.Length < 3 ||
            layout.barriers == null || layout.barriers.Length < 4))
        {
            Finish(false, "Expected at least three ground lanes and four roof/support probes.");
            yield break;
        }

        Camera camera = Camera.main;
        var cycle = FindAnyObjectByType<StreetDayNight>();
        var controller = camera ? camera.GetComponentInParent<CharacterController>() : null;
        if (!camera || !controller || !cycle || GraphicsSettings.currentRenderPipeline is not HDRenderPipelineAsset)
        {
            Finish(false, "The HDRP player camera, controller or day/night cycle was unavailable.");
            yield break;
        }
        var walk = camera.GetComponentInParent<FirstPersonWalk>();
        var session = camera.GetComponentInParent<StreetSession>();
        if (walk) walk.enabled = false;
        if (session) session.enabled = false;
        controller.enabled = false;
        camera.enabled = false;
        cycle.Paused = true;
        cycle.SetHour(report.hour);
        Physics.SyncTransforms();
        report.wastePadHidden = true;
        foreach (var filter in FindObjectsByType<MeshFilter>())
            if (filter.sharedMesh && (filter.sharedMesh.name == "ContainerScreen" ||
                    filter.sharedMesh.name == "Relocated concrete waste slab"))
                report.wastePadHidden = false;

        if (!report.captureOnly)
        {
            foreach (var lane in layout.lanes)
                report.lanes.Add(InspectLane(lane, controller));
            foreach (var barrier in layout.barriers)
                report.barriers.Add(InspectBarrier(barrier));
        }

        var target = new RenderTexture(report.width, report.height, 24, RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB) { useDynamicScale = false, name = "Garage passage QA target" };
        var request = new RenderPipeline.StandardRequest { destination = target };
        if (!target.Create() || !RenderPipeline.SupportsRenderRequest(camera, request))
        {
            target.Release();
            Destroy(target);
            Finish(false, "The HDRP offscreen render request could not be created.");
            yield break;
        }
        var image = new Texture2D(report.width, report.height, TextureFormat.RGB24, false);
        yield return new WaitForSecondsRealtime(1.25f);
        var views = layout.views is { Length: > 0 } ? layout.views : DefaultViews();
        foreach (var shot in views)
        {
            camera.transform.SetPositionAndRotation(shot.eye, Quaternion.LookRotation(shot.lookAt - shot.eye));
            for (int frame = 0; frame < 24; ++frame)
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                yield return null;
            }
            var timer = Stopwatch.StartNew();
            const int measuredFrames = 60;
            for (int frame = 0; frame < measuredFrames; ++frame)
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                yield return null;
            }
            ReadImage(image, target);
            float averageRate = measuredFrames / Mathf.Max(.001f, (float)timer.Elapsed.TotalSeconds);
            string name = Path.GetFileNameWithoutExtension(shot.name) + ".png";
            File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG());
            report.screenshots.Add(new ShotResult
            {
                name = name, eye = shot.eye, lookAt = shot.lookAt,
                timedFrames = measuredFrames, offscreenAverageFps = averageRate
            });
        }
        Destroy(image);
        target.Release();
        Destroy(target);
        Finish((report.captureOnly || report.wastePadHidden) &&
            report.lanes.TrueForAll(lane => lane.passed) && report.barriers.TrueForAll(probe => probe.passed));
    }

    private static string Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static Shot[] DefaultViews() => new[]
    {
        new Shot { name = "01_opposite_front", eye = new(-117, 2.1f, 71.1f), lookAt = new(-97, .2f, 71.1f) },
        new Shot { name = "02_opposite_flank", eye = new(-115, 3.4f, 59.6f), lookAt = new(-100, .1f, 70.2f) },
        new Shot { name = "03_opposite_top", eye = new(-107.5f, 10.5f, 60.8f), lookAt = new(-99, 0f, 72.5f) },
        new Shot { name = "04_north_reference", eye = new(19.5f, 2.7f, 130f), lookAt = new(25.6f, .2f, 110.6f) },
        new Shot { name = "05_opposite_portals", eye = new(-106, 1.8f, 71.1f), lookAt = new(-93, .1f, 71.1f) },
        new Shot { name = "06_north_top", eye = new(16.5f, 10.5f, 122.0f), lookAt = new(25.8f, 0f, 112.5f) },
        new Shot { name = "07_courtyard_front", eye = new(25.5f, 2f, -48f), lookAt = new(25.5f, 1.3f, -33f) },
        new Shot { name = "08_courtyard_flank", eye = new(32.5f, 2.5f, -45.4f), lookAt = new(25.4f, 1.2f, -37.1f) },
        new Shot { name = "09_courtyard_top", eye = new(18.5f, 11f, -49f), lookAt = new(25.5f, .5f, -28f) }
    };

    private static LaneResult InspectLane(Lane lane, CharacterController source)
    {
        var report = new LaneResult { name = lane.name };
        if (lane.ground == null || lane.ground.Length < 2 || lane.width <= 0 || lane.headroom <= 0)
            return report;
        var path = new List<Vector3>();
        for (int p = 0; p + 1 < lane.ground.Length; ++p)
        {
            float length = Vector3.ProjectOnPlane(lane.ground[p + 1] - lane.ground[p], Vector3.up).magnitude;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / .15f));
            for (int i = 0; i < count; ++i)
                path.Add(Vector3.Lerp(lane.ground[p], lane.ground[p + 1], (float)i / count));
        }
        path.Add(lane.ground[^1]);
        const float probeHeight = .65f;
        const int mask = Physics.DefaultRaycastLayers;
        var floors = new List<Vector3>(path.Count);
        for (int sample = 0; sample < path.Count; ++sample)
        {
            var point = path[sample];
            var forward = path[Mathf.Min(path.Count - 1, sample + 1)] - path[Mathf.Max(0, sample - 1)];
            forward.y = 0;
            if (forward.sqrMagnitude < .00001f)
                continue;
            var normal = Vector3.Cross(Vector3.up, forward.normalized);
            // Three traces demonstrate continuity across a representative car-width strip.
            for (int side = -1; side <= 1; ++side)
            {
                ++report.samples;
                var at = point + normal * (side * lane.width * .48f);
                if (!Physics.Raycast(at + Vector3.up * probeHeight, Vector3.down, out var hit,
                    probeHeight * 2, mask, QueryTriggerInteraction.Ignore))
                {
                    ++report.missingGround;
                    continue;
                }
                if (side == 0)
                    floors.Add(hit.point);
                float error = Mathf.Abs(hit.point.y - point.y);
                if (error > report.maxHeightError)
                {
                    report.maxHeightError = error;
                    report.worstPosition = hit.point;
                    report.worstGround = hit.collider.name;
                }
                if (error > lane.tolerance || hit.normal.y < .85f)
                    ++report.excessiveGroundError;
            }
            // A car-width and height envelope checked slice by slice, independent of the player radius.
            const float clearanceAboveFloor = .16f;
            float halfHeight = (lane.headroom - clearanceAboveFloor) * .5f;
            if (halfHeight <= 0) return report;
            Vector3 body = point + Vector3.up * (clearanceAboveFloor + halfHeight);
            var box = new Vector3(lane.width * .5f, halfHeight, .08f);
            var collisions = Physics.OverlapBox(body, box, Quaternion.LookRotation(forward),
                mask, QueryTriggerInteraction.Ignore);
            if (collisions.Length > 0)
            {
                ++report.clearanceObstructions;
                report.blocker ??= collisions[0].name;
            }
        }
        if (floors.Count == path.Count)
        {
            report.measuredRise = floors[^1].y - floors[0].y;
            for (int i = 1; i < path.Count; ++i)
            {
                float step = Mathf.Abs((floors[i].y - floors[i - 1].y) - (path[i].y - path[i - 1].y));
                report.maximumExcessStep = Mathf.Max(report.maximumExcessStep, step);
            }
            report.playerTraversal = WalkLane(floors, source, out report.walkEndError);
        }
        report.passed = report.samples > 0 && report.missingGround == 0 &&
            report.excessiveGroundError == 0 && report.maximumExcessStep <= lane.tolerance &&
            report.clearanceObstructions == 0 && report.playerTraversal;
        return report;
    }

    private static bool WalkLane(List<Vector3> floors, CharacterController source, out float endError)
    {
        var node = new GameObject("Garage QA walk capsule") { layer = source.gameObject.layer };
        float feet = source.height * .5f - source.center.y;
        node.transform.position = floors[0] + Vector3.up * (feet + .035f);
        var capsule = node.AddComponent<CharacterController>();
        capsule.height = source.height;
        capsule.radius = source.radius;
        capsule.center = source.center;
        capsule.stepOffset = source.stepOffset;
        capsule.slopeLimit = source.slopeLimit;
        capsule.skinWidth = source.skinWidth;
        capsule.minMoveDistance = 0f;
        Physics.SyncTransforms();
        capsule.Move(Vector3.down * .075f);
        for (int i = 1; i < floors.Count; ++i)
        {
            Vector3 step = floors[i] - floors[i - 1];
            step.y = -.09f;
            capsule.Move(step);
        }
        var offset = node.transform.position - (floors[^1] + Vector3.up * feet);
        endError = Vector3.ProjectOnPlane(offset, Vector3.up).magnitude;
        bool passed = capsule.isGrounded && endError <= .32f && Mathf.Abs(offset.y) < .2f;
        capsule.enabled = false;
        Destroy(node);
        return passed;
    }

    private static BarrierResult InspectBarrier(Barrier barrier)
    {
        var result = new BarrierResult { name = barrier.name };
        var delta = barrier.to - barrier.from;
        if (delta.sqrMagnitude < .00001f || !Physics.Raycast(barrier.from, delta.normalized, out var hit,
            delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return result;
        result.collider = hit.collider.name;
        result.contact = hit.point;
        result.passed = !string.IsNullOrWhiteSpace(barrier.colliderNameContains) &&
            hit.collider.name.Contains(barrier.colliderNameContains, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static void ReadImage(Texture2D image, RenderTexture target)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        try
        {
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            image.Apply(false);
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    private void CaptureError(string condition, string stackTrace, LogType type)
    {
        if (type is LogType.Error or LogType.Exception or LogType.Assert)
            report.errors.Add(condition);
    }

    private void Finish(bool passed, string message = null)
    {
        if (message != null) report.errors.Add(message);
        Application.logMessageReceived -= CaptureError;
        report.status = "finished";
        report.passed = passed && report.errors.Count == 0;
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        UnityEngine.Debug.Log("GARAGE_PASSAGE_QA " + reportPath + " passed=" + report.passed);
        Application.Quit(report.passed ? 0 : 2);
    }
}
