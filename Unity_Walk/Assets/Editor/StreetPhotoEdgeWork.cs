using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetPhotoEdgeWork
{
    private const string Configuration = "Assets/Editor/PhotoCourtAreas.json";
    private const string Checks = "QA_HDRP/boundary-final";

    [Serializable]
    private sealed class Scope
    {
        public PhotoArea[] areas;
        public string[] savedRoadMeshes;
    }

    [Serializable]
    private sealed class PhotoArea
    {
        public int photo;
        public Vector2[] polygon;
    }

    [MenuItem("Street/Finish edges within photographed court")]
    public static void Apply()
    {
        var scope=JsonUtility.FromJson<Scope>(File.ReadAllText(Configuration));
        if(scope?.areas==null || scope.savedRoadMeshes==null ||
            scope.areas.Any(x=>x.photo<2 || x.photo>6 || x.polygon==null || x.polygon.Length<3))
            throw new InvalidOperationException("Missing photographed world boundaries.");
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Street.unity",OpenSceneMode.Single);
        var root=scene.GetRootGameObjects().Single(x=>x.name==StreetPhotoBoundaryRepair.NewRootName);
        var names=scope.savedRoadMeshes.Select(path =>
        {
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh) throw new InvalidOperationException($"Missing reviewed asphalt source {path}.");
            return mesh.name;
        }).ToArray();
        var report=StreetPhotoSiteBorders.Apply(scene,root.transform,
            scope.areas.Select(x=>x.polygon).ToArray(),StreetPhotoBoundaryRepair.Output,names);
        StreetPhotoBoundaryRepair.CloseOuterAprons(root.transform);
        Directory.CreateDirectory(Checks);
        File.WriteAllText(Checks+"/edges-edit.json",JsonUtility.ToJson(report,true));
        if(!report.passed)throw new InvalidOperationException("The photographed edges failed edit-time scope or physics verification.");
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save the photographed road edges.");
        AssetDatabase.SaveAssets();
        Debug.Log("PHOTO_EDGE_SAVED "+JsonUtility.ToJson(report));
    }

    public static void ApplyAndBuild()
    {
        Apply();
        StreetPhotoBoundaryRepair.Build();
    }
}
