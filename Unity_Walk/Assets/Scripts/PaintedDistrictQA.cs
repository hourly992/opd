using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

internal sealed class PaintedDistrictQA : MonoBehaviour
{
    private const int Width = 1600;
    private const int Height = 900;
    private const int Samples = 42;
    private const float ComparisonFov = 62;
    private static readonly string[] Names = { "01_house5", "02_court7", "03_street", "04_south", "05_east", "06_west_court", "07_east_court", "08_north_court", "09_south_court" };
    private static readonly Vector3[] Positions = {
        Vector3.zero, new Vector3(26, 1.7f, 37), new Vector3(-31.5f, 1.75f, 20),
        new Vector3(139, 2.05f, -215), new Vector3(195, 2.1f, 112),
        new Vector3(-116, 9, 12), new Vector3(231, 10, 36), new Vector3(-140, 8, 163), new Vector3(139, 11, -215)
    };
    private static readonly Vector3[] Targets = {
        Vector3.zero, new Vector3(56, 1.6f, 40), new Vector3(-4, 3, -43),
        new Vector3(170, 1.2f, -230), new Vector3(225, 1.4f, 140),
        new Vector3(-70, 0, 12), new Vector3(193, 0, 34), new Vector3(-90, 0, 139), new Vector3(72, 0, -158)
    };
    private string output;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-paintedDistrictQA");
        if (index < 0) return;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-"))
        {
            Debug.LogError("Supply an output directory after -paintedDistrictQA.");
            Application.Quit(2);
            return;
        }
        var check = new GameObject(nameof(PaintedDistrictQA)).AddComponent<PaintedDistrictQA>();
        check.output = Path.GetFullPath(args[index + 1]);
    }

    private IEnumerator Start()
    {
        var camera = Camera.main;
        if (!camera)
        {
            Debug.LogError("The district camera is missing.");
            Application.Quit(2);
            yield break;
        }

        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        foreach (var controller in FindObjectsByType<FirstPersonWalk>())
            controller.enabled = false;
        camera.enabled = false;
        var presentation = camera.GetComponent<StreetPresentation>();
        Vector3 spawn = camera.transform.position;
        Quaternion heading = camera.transform.rotation;
        float nativeFov = camera.fieldOfView;
        Directory.CreateDirectory(output);
        // Realtime reflection probes can finish their first cubemaps before the timed pass.
        for (int frame = 0; frame < 48; frame++) yield return null;
        var buffer = new RenderTexture(Width, Height, 24) { antiAliasing = 4 };
        var capture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
        var originalActive = RenderTexture.active;
        var originalTarget = camera.targetTexture;
        var stats = new string[Names.Length + 1];
        stats[0] = "view,nativeVerticalFov,dxr,medianMs,p95Ms";
        var timer = new Stopwatch();
        try
        {
            camera.targetTexture = buffer;
            for (int view = 0; view < Names.Length; view++)
            {
                camera.transform.SetPositionAndRotation(view == 0 ? spawn : Positions[view],
                    view == 0 ? heading : Quaternion.LookRotation(Targets[view] - Positions[view]));
                camera.fieldOfView = nativeFov;
                var times = new double[Samples];
                for (int frame = -12; frame < Samples; frame++)
                {
                    timer.Restart();
                    camera.Render();
                    RenderTexture.active = buffer;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    timer.Stop();
                    if (frame >= 0) times[frame] = timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(times);
                stats[view + 1] = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:F2},{4:F2}",
                    Names[view], nativeFov, presentation && presentation.RayTracingActive,
                    (times[Samples / 2 - 1] + times[Samples / 2]) * .5, times[Mathf.CeilToInt(Samples * .95f) - 1]);

                camera.fieldOfView = ComparisonFov;
                camera.Render();
                RenderTexture.active = buffer;
                capture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                File.WriteAllBytes(Path.Combine(output, Names[view] + ".jpg"), capture.EncodeToJPG(89));
                RenderTexture.active = originalActive;
                yield return null;
            }
            File.WriteAllLines(Path.Combine(output, "performance.csv"), stats);
            Debug.Log("PAINTED_DISTRICT_QA_FINISHED " + SystemInfo.graphicsDeviceName);
        }
        finally
        {
            camera.targetTexture = originalTarget;
            RenderTexture.active = originalActive;
            Destroy(pixel);
            Destroy(capture);
            Destroy(buffer);
        }
        Application.Quit();
    }
}
