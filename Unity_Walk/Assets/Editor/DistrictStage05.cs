using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bounded saved-scene workflow. Never calls the archived district generator.
public static class DistrictStage05
{
    const string Out="street_qa/district_stage05_editor";
    [Serializable] class Entry {public string name,mesh;public Vector3 position;public Bounds bounds;public string[] materials;public Vector3[] outline;}
    [Serializable] class Inventory {public Entry[] islands;public ResidentialAccess.Route[] routes;}
    public static void Inspect()
    {
        Open();Directory.CreateDirectory(Out);
        var islands=UnityEngine.Object.FindObjectsByType<MeshFilter>().Where(f=>f.sharedMesh&&f.sharedMesh.name.StartsWith("Stage04RaisedIsland_")).Select(f=>{
            var r=f.GetComponent<Renderer>();return new Entry{name=f.name,mesh=f.sharedMesh.name,position=f.transform.position,bounds=r.bounds,materials=r.sharedMaterials.Select(m=>m.name).ToArray(),outline=f.sharedMesh.vertices.Select(f.transform.TransformPoint).Distinct().ToArray()};
        }).ToArray();
        File.WriteAllText(Out+"/inventory.json",JsonUtility.ToJson(new Inventory{islands=islands,routes=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>().routes},true));
        Capture("before_court",new Vector3(26,1.7f,37),new Vector3(61,1.6f,38.3f));
        Capture("before_south",new Vector3(139,11,-215),new Vector3(72,0,-158));
        Debug.Log("STAGE05_INSPECT islands="+islands.Length);
    }
    public static void ApplyCivil()
    {
        Open();Directory.CreateDirectory(Out);
        PhotoCourtCivilStage05.Apply();
        Check("civil");Save();
        Capture("civil_court",new Vector3(26,1.7f,37),new Vector3(61,1.6f,38.3f));
    }
    public static void DiagnoseCourt()
    {
        Open();var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        for(float x=55.5f;x<62;x+=.25f)
        {
            var p=new Vector3(x,2,38.305f);var hits=Physics.RaycastAll(p,Vector3.down,3).OrderByDescending(h=>h.point.y);
            Debug.Log("COURT_FLOOR "+x+" "+string.Join("; ",hits.Select(h=>h.collider.name+" y="+h.point.y+" n="+h.normal.y+" registered="+access.surfaces.Contains(h.collider as MeshCollider))));
        }
    }
    public static void AuditJoints()
    {
        Open();Directory.CreateDirectory(Out);
        Capture("court_door_close",new Vector3(56.8f,1.7f,40.3f),new Vector3(62.4f,2,38.305f));
        var lights=UnityEngine.Object.FindObjectsByType<Light>();var shadows=lights.Select(l=>l.shadows).ToArray();
        for(int i=0;i<lights.Length;i++)lights[i].shadows=LightShadows.None;
        Capture("court_door_no_shadow_diagnosis",new Vector3(56.8f,1.7f,40.3f),new Vector3(62.4f,2,38.305f));
        for(int i=0;i<lights.Length;i++)lights[i].shadows=shadows[i];
        PhotoCourtJointsStage05.Audit();
    }
    public static void FinalizeAndBuild()
    {
        Open();Directory.CreateDirectory(Out);
        PhotoEntranceStage05.Apply();PhotoFacadeStage05.Apply();Check("entrances");
        PhotoSurfaceEdgesStage05.Apply();Check("edges");
        Save();
        Capture("court_eye",new Vector3(26,1.7f,37),new Vector3(61,1.6f,38.3f));
        Capture("court_detail",new Vector3(41,1.65f,39.5f),new Vector3(34,.4f,43.5f));
        Capture("north",new Vector3(-140,8,163),new Vector3(-90,0,139));
        Capture("west",new Vector3(-116,9,12),new Vector3(-70,0,12));
        Capture("east",new Vector3(231,10,36),new Vector3(193,0,34));
        Capture("south",new Vector3(139,11,-215),new Vector3(72,0,-158));
        StreetBuilder.BuildGeneratedScene();
    }
    public static void RefineFinalAndBuild()
    {
        Open();Directory.CreateDirectory(Out);
        PhotoEntranceStage05.RefineResiduals();
        PhotoFacadeStage05.RefineAtticVent();
        PhotoCourtSeamRepairStage05.Apply();
        PhotoSurfaceEdgesStage05.UpgradeSavedMaterials();
        PhotoCourtJointsStage05.Audit();Check("final");Save();
        Capture("court_eye_final",new Vector3(26,1.7f,37),new Vector3(61,1.6f,38.3f));
        Capture("court_door_final",new Vector3(56.8f,1.7f,40.3f),new Vector3(62.4f,2,38.305f));
        Capture("shop_door_final",new Vector3(120.5f,1.7f,8.4f),new Vector3(126.8f,1.6f,5.869f));
        Capture("court_connector_final",new Vector3(49,4.5f,33.8f),new Vector3(56,.1f,38.305f));
        Capture("south_final",new Vector3(139,11,-215),new Vector3(72,0,-158));
        StreetBuilder.BuildGeneratedScene();
    }
    static void Check(string stage)
    {
        Physics.SyncTransforms();SavedSurfaceStage03.ReprojectCurrentChecks();DistrictSurfaceAuditStage04.Check();
        string src="street_qa/district_stage04_geometry_audit.json";File.Copy(src,Out+"/"+stage+"_geometry_audit.json",true);
        var report=JsonUtility.FromJson<DistrictSurfaceAuditStage04.Report>(File.ReadAllText(src));
        if(!report.passed)throw new InvalidOperationException("Stage05 "+stage+" failed the independent route audit; scene not saved.");
        foreach(var f in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if(!f.sharedMesh||!AssetDatabase.GetAssetPath(f.sharedMesh).Contains("Stage05"))continue;
            var v=f.sharedMesh.vertices;if(v.Any(p=>!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)))throw new InvalidOperationException("Nonfinite stage05 mesh: "+f.name);
            if(f.sharedMesh.normals.Length!=v.Length)throw new InvalidOperationException("Missing stage05 normals: "+f.name);
        }
        VerifyPoses();
    }
    [Serializable] class Pose {public string name;public Vector3 position,rotationEuler,scale;}
    [Serializable] class Building {public string name;public Pose transform;}
    [Serializable] class Original {public Building[] buildings;public Pose house5;}
    [Serializable] class PoseReport {public int checkedBuildings;public float maxPositionDelta,maxRotationDelta,maxScaleDelta;public bool passed;}
    public static void VerifyPoses()
    {
        var source=JsonUtility.FromJson<Original>(File.ReadAllText("street_qa/photo_scene_inventory.json"));var report=new PoseReport();
        foreach(var p in source.buildings.Select(b=>b.transform).Concat(new[]{source.house5}))
        {
            var t=GameObject.Find(p.name)?.transform;if(!t)throw new InvalidOperationException("Missing original building "+p.name);
            report.checkedBuildings++;report.maxPositionDelta=Mathf.Max(report.maxPositionDelta,Vector3.Distance(t.position,p.position));
            report.maxRotationDelta=Mathf.Max(report.maxRotationDelta,Quaternion.Angle(t.rotation,Quaternion.Euler(p.rotationEuler)));
            report.maxScaleDelta=Mathf.Max(report.maxScaleDelta,Vector3.Distance(t.lossyScale,p.scale));
        }
        report.passed=report.checkedBuildings==19&&report.maxPositionDelta<.0001f&&report.maxRotationDelta<.001f&&report.maxScaleDelta<.0001f;
        Directory.CreateDirectory(Out);File.WriteAllText(Out+"/building_poses.json",JsonUtility.ToJson(report,true));
        if(!report.passed)throw new InvalidOperationException("Stage05 changed a building pose.");
    }
    static void Open()
    {
        for(int i=0;i<EditorSceneManager.sceneCount;i++)if(EditorSceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save the open scene before the stage05 operation.");
        EditorSceneManager.OpenScene("Assets/Scenes/Street.unity");Physics.SyncTransforms();
    }
    static void Save(){EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();}
    static void Capture(string name,Vector3 p,Vector3 target)
    {
        var go=new GameObject("Temporary stage05 capture");var cam=go.AddComponent<Camera>();cam.transform.SetPositionAndRotation(p,Quaternion.LookRotation(target-p));
        cam.farClipPlane=1200;cam.fieldOfView=64;cam.clearFlags=CameraClearFlags.Skybox;
        var rt=new RenderTexture(1600,900,24);cam.targetTexture=rt;cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;
        var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();
        File.WriteAllBytes(Out+"/"+name+".png",tex.EncodeToPNG());RenderTexture.active=old;
        UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
    }
}
