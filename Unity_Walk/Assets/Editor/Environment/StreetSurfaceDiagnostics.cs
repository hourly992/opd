using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A repeatable render diagnostic. It never saves its temporary lighting overrides to the scene.
internal static class StreetSurfaceDiagnostics
{
    [MenuItem("Street/Capture surface shadow diagnostic")]
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Street.unity");
        var camera = Camera.main;
        var view = Object.FindAnyObjectByType<PilotBlock>().inspectionViews[0];
        camera.transform.SetPositionAndRotation(view.position, view.rotation);
        var sun = RenderSettings.sun;
        var shadows = sun.shadows;
        float bias = sun.shadowBias, normal = sun.shadowNormalBias;
        string directory = Path.GetFullPath("street_qa/surface_diagnostic"); Directory.CreateDirectory(directory);
        var target = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
        var priorTarget = camera.targetTexture; var priorActive = RenderTexture.active;
        camera.targetTexture = target;
        void Shot(string name)
        {
            for (int i = 0; i < 8; i++) camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
        try
        {
            Shot("01_current_shadows");
            sun.shadows = LightShadows.None; Shot("02_without_shadows");
            sun.shadows = shadows; sun.shadowBias = .065f; sun.shadowNormalBias = .25f;
            Shot("03_larger_bias");
        }
        finally
        {
            sun.shadows = shadows; sun.shadowBias = bias; sun.shadowNormalBias = normal;
            camera.targetTexture = priorTarget; RenderTexture.active = priorActive;
            target.Release(); Object.DestroyImmediate(target);
        }
        Debug.Log("SURFACE_DIAGNOSTIC_COMPLETE " + directory);
    }
}
