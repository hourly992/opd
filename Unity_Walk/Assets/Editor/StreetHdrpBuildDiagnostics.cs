using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class StreetHdrpBuildDiagnostics
{
    public static void ConfigureAndMigrate()
    {
        StreetHdrpSetup.Configure();
        StreetHdrpMaterials.Migrate();
    }

    public static void ApplyLightingAndBuildWindows()
    {
        StreetHdrpSetup.Configure();
        StreetBuilder.Build();
    }

    public static void BuildBothWindowsTargets()
    {
        BuildWindows();
        StreetBuilder.Build();
    }

    public static void BuildProfileBaseline()
    {
        string path = Path.GetFullPath("Build/StreetWindowsHDRPProfileBaseline/DuninaGorkavicha.exe");
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Street.unity" },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = path,
            options = BuildOptions.None
        });
        if (result.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Baseline build failed: {result.summary.result}");
        Debug.Log($"STREET_HDRP_PROFILE_BASELINE {path}");
    }

    public static void BuildWindows()
    {
        PlayerSettings.productName = "Дунина-Горкавича — диагностика HDRP";
        string[] scenes =
        {
            "Assets/Scenes/Street.unity",
            "Assets/Scenes/Street_LeafStudy.unity",
            "Assets/Scenes/BlosmComparison.unity",
            "Assets/Scenes/Walk.unity"
        };
        string path = Path.GetFullPath("Build/StreetWindowsHDRPDiagnostics/Diagnostics.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = path,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"HDRP diagnostic build failed: {report.summary.result}");
        Debug.Log($"STREET_HDRP_DIAGNOSTIC_BUILD {path}");
    }
}
