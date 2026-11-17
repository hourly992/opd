using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static partial class StreetGarageLevelRepair
{
    public static void BuildBefore()
    {
        Build("Build/GarageBefore/DuninaGorkavicha.exe");
    }

    [MenuItem("Street/Build garage repair and Windows game")]
    public static void BuildAfter()
    {
        Build("Build/StreetWindowsHDRPGarage/DuninaGorkavicha.exe");
    }

    private static void Build(string output)
    {
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Street.unity", "Assets/Scenes/Street_LeafStudy.unity" },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = Path.GetFullPath(output)
        });
        if (result.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Garage player build failed: {result.summary.result}.");
        UnityEngine.Debug.Log($"GARAGE_BUILD_SUCCEEDED {output}");
    }
}
