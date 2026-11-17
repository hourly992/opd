using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class StreetViewComparison : MonoBehaviour
{
    private static readonly string[] Names =
    {
        "01_street", "02_house5", "03_courtyard", "04_overview", "05_foliage"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartIfRequested()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-comparisonQA") >= 0)
            new GameObject("Street comparison capture").AddComponent<StreetViewComparison>();
    }

    private IEnumerator Start()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-qaOutput");
        if (index < 0 || index + 1 >= args.Length)
        {
            Application.Quit(2);
            yield break;
        }

        string output = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(output);
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        yield return new WaitForSecondsRealtime(1.5f);

        var camera = Camera.main;
        var block = FindAnyObjectByType<PilotBlock>();
        if (!camera || !block || block.inspectionViews.Length != 4 ||
            Screen.width != 1600 || Screen.height != 900)
        {
            Debug.LogError($"Cannot capture matched Street views at {Screen.width}x{Screen.height}.");
            Application.Quit(2);
            yield break;
        }

        if (camera.GetComponentInParent<FirstPersonWalk>() is { } walk)
            walk.enabled = false;
        if (camera.GetComponentInParent<StreetSession>() is { } session)
            session.enabled = false;
        if (camera.GetComponentInParent<CharacterController>() is { } controller)
            controller.enabled = false;

        camera.enabled = false;
        var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create();
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        bool hdrp = GraphicsSettings.currentRenderPipeline != null;
        var request = new RenderPipeline.StandardRequest { destination = target };

        for (int view = 0; view < Names.Length; view++)
        {
            if (view < block.inspectionViews.Length)
            {
                var pose = block.inspectionViews[view];
                camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
            else
            {
                var eye = new Vector3(-27.5f, 1.7f, 24f);
                var lookAt = new Vector3(-24f, 1.2f, 21f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(lookAt - eye));
            }

            for (int frame = 0; frame < 24; frame++)
            {
                yield return null;
                if (hdrp)
                {
                    if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    {
                        Debug.LogError("HDRP does not support the comparison render request.");
                        Application.Quit(2);
                        yield break;
                    }
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.targetTexture = target;
                    camera.Render();
                    camera.targetTexture = null;
                }
            }

            var previousTarget = RenderTexture.active;
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            RenderTexture.active = previousTarget;
            File.WriteAllBytes(Path.Combine(output, Names[view] + ".png"), image.EncodeToPNG());
        }
        Destroy(image);
        target.Release();
        Destroy(target);
        Debug.Log($"STREET_COMPARISON_CAPTURED {Names.Length} views at {Screen.width}x{Screen.height}");
        Application.Quit(0);
    }
}
