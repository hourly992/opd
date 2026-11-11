using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LeafCourtyardStudy
{
    internal const string ScenePath = "Assets/Scenes/Street_LeafStudy.unity";
    private const string Original = "Assets/Scenes/Street.unity";

    internal static bool InStudyArea(Bounds bounds) =>
        bounds.max.x >= 22 && bounds.min.x <= 65 && bounds.max.z >= 28 && bounds.min.z <= 49 &&
        bounds.size.x < 110 && bounds.size.z < 110;

    public static void Build()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scenes before rebuilding the courtyard experiment.");

        string originalHash = Hash(Original);
        var scene = EditorSceneManager.OpenScene(Original);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new IOException("Could not create the courtyard study scene.");

        DistrictLeafLighting.Apply();
        DistrictLeafPalette.Apply();
        DistrictLeafCarpets.Apply();
        DistrictLeafDrift.Apply();

        var player = GameObject.FindGameObjectWithTag("Player").transform;
        player.SetPositionAndRotation(new Vector3(26, 1.2f, 37),
            Quaternion.LookRotation(new Vector3(30, 0, 3)));
        DistrictSurfaceAuditStage04.Check();
        DistrictStage05.VerifyPoses();
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Could not save the courtyard experiment.");
        AssetDatabase.SaveAssets();

        string folder = Path.GetFullPath("Build/LeafStudy");
        Directory.CreateDirectory(folder);
        string oldProduct = PlayerSettings.productName;
        try
        {
            PlayerSettings.productName = "Двор №7 — осенний эксперимент";
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine(folder, "LeafCourtyard.exe"), options = BuildOptions.None
            });
            if (build.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("The courtyard study build failed.");
        }
        finally
        {
            PlayerSettings.productName = oldProduct;
        }
        if (Hash(Original) != originalHash)
            throw new InvalidOperationException("The source street scene changed during the courtyard experiment.");
        Debug.Log($"LEAF_COURTYARD_BUILD_SUCCEEDED {folder} originalSceneHash={originalHash}");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
    }
}
