using System;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

// Repeat the same doorway views before and after editing the saved scene.
public static class DistrictEntranceSurvey
{
    internal const string Output = "street_qa/district_entrances";

    public static void Before()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Street.unity");
        Capture("before");
    }

    internal static void Capture(string pass)
    {
        string folder = Path.Combine(Output, pass);
        Directory.CreateDirectory(folder);
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var buildings = UnityEngine.Object.FindObjectsByType<StreetBuilding>();
        var routes = access.routes.Where(r => r.address.Contains(" / ", StringComparison.Ordinal))
            .OrderBy(r => r.address, StringComparer.Ordinal).ToArray();
        var go = new GameObject("District entrance inspection camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 55;
        camera.nearClipPlane = .045f;
        camera.farClipPlane = 180;
        var target = new RenderTexture(400, 225, 24);
        var frame = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        var previousTarget = RenderTexture.active;
        camera.targetTexture = target;
        try
        {
            using var index = File.CreateText(Path.Combine(folder, "entries.tsv"));
            for (int i = 0; i < routes.Length; i++)
            {
                var route = routes[i];
                Vector3 center = route.points[0];
                var building = buildings.FirstOrDefault(b => route.address.StartsWith(b.address + " (OSM " + b.osmId + ")", StringComparison.Ordinal));
                var marker = building ? building.transform.Find(route.address.Split(new[] { " / " }, 2, StringSplitOptions.None)[1]) : null;
                Vector3 outward = marker ? marker.forward : Vector3.ProjectOnPlane(route.points[1] - center, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
                WriteView("front", center + outward * 4.1f + Vector3.up * 1.65f, center - outward + Vector3.up * 1.25f);
                WriteView("side", center + outward * 1.25f + right * 2.35f + Vector3.up * 1.6f,
                    center - outward * .75f - right * .35f + Vector3.up * .75f);
                index.WriteLine($"{i:D3}\t{route.address}\t{center:F3}");

                void WriteView(string name, Vector3 from, Vector3 to)
                {
                    camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
                    camera.Render();
                    RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    File.WriteAllBytes(Path.Combine(folder, $"{i:D3}_{name}.png"), frame.EncodeToPNG());
                }
            }
            Debug.Log($"DISTRICT_ENTRANCE_SURVEY pass={pass} routes={routes.Length} views={routes.Length * 2}");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(frame);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
