using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FullVisualPass
{
    private const string ScenePath = "Assets/Scenes/Street.unity";

    public static void Apply()
    {
        Open();
        FullVisualAudit.Save("before");
        string layout = FullVisualAudit.LayoutHash();
        DistrictLeafPalette.Apply(true);
        FullVisualMaterialLibrary.Apply();
        FullVisualGeometry.Apply();
        FullVisualLighting.Apply();
        DistrictLeafCarpets.Apply(true);
        DistrictLeafDrift.Apply(true);
        if (FullVisualAudit.LayoutHash() != layout)
            throw new InvalidOperationException("The district layout changed during the visual pass.");
        Verify();
        FullVisualAudit.Save("after");
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Could not save the updated Street scene.");
        AssetDatabase.SaveAssets();
        Debug.Log("FULL_VISUAL_SCENE_SAVED layout=" + layout);
    }

    public static void Build()
    {
        Open();
        Verify();
        FullVisualAudit.Save("built");
        StreetBuilder.BuildGeneratedScene();
    }

    public static void BakeLighting()
    {
        Open();
        FullVisualMaterialLibrary.Apply();
        FullVisualProbeBake.Bake();
    }

    private static void Verify()
    {
        Physics.SyncTransforms();
        DistrictSurfaceAuditStage04.Check();
        DistrictStage05.VerifyPoses();
        Directory.CreateDirectory(FullVisualAudit.Output);
        File.Copy("street_qa/district_stage04_geometry_audit.json",
            FullVisualAudit.Output + "/geometry_audit.json", true);
        File.Copy("street_qa/district_stage05_editor/building_poses.json",
            FullVisualAudit.Output + "/building_poses.json", true);
    }

    private static void Open()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scenes before editing the main street.");
        EditorSceneManager.OpenScene(ScenePath);
    }
}
