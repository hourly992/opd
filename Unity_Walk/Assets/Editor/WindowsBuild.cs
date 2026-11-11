using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class WindowsBuild
{
    public static void Build()
    {
        PrepareMaterials();
        PlayerSettings.companyName = "OPD";
        PlayerSettings.productName = "Walk";
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = UnityEngine.FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;

        string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        string outputFolder = Path.Combine(projectRoot, "Build", "Windows");
        Directory.CreateDirectory(outputFolder);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Walk.unity" },
            locationPathName = Path.Combine(outputFolder, "Walk.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Windows build failed: " + report.summary.result);
    }

    private static void PrepareMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        CreateMaterial("Floor", new UnityEngine.Color(0.38f, 0.43f, 0.47f));
        CreateMaterial("Block", new UnityEngine.Color(0.85f, 0.55f, 0.25f));
        CreateMaterial("Wall", new UnityEngine.Color(0.55f, 0.61f, 0.65f));
        AssetDatabase.SaveAssets();
    }

    private static void CreateMaterial(string name, UnityEngine.Color color)
    {
        string path = "Assets/Resources/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
        if (material == null)
        {
            var shader = UnityEngine.Shader.Find("Standard");
            if (shader == null)
                throw new Exception("The Standard shader is unavailable.");
            material = new UnityEngine.Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.SetFloat("_Glossiness", 0.15f);
        EditorUtility.SetDirty(material);
    }
}
