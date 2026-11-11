using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictStage04
{
    const string Output="street_qa/district_stage04_editor";
    [Serializable] class RecordedPose {public string name;public Vector3 position,rotationEuler,scale;}
    [Serializable] class RecordedBuilding {public string name;public RecordedPose transform;}
    [Serializable] class InitialInventory {public RecordedBuilding[] buildings;public RecordedPose house5;}
    [Serializable] class PoseCheck {public int checkedBuildings;public float maximumPositionDelta,maximumRotationDelta,maximumScaleDelta;public bool passed;}
    public static void VerifySavedBuildingPoses()
    {
        OpenSaved();var original=JsonUtility.FromJson<InitialInventory>(File.ReadAllText("street_qa/photo_scene_inventory.json"));
        var result=new PoseCheck();
        void Check(RecordedPose recorded,Transform actual)
        {
            if(!actual)throw new InvalidOperationException("Original building is missing: "+recorded.name);
            result.checkedBuildings++;result.maximumPositionDelta=Mathf.Max(result.maximumPositionDelta,Vector3.Distance(recorded.position,actual.position));
            result.maximumRotationDelta=Mathf.Max(result.maximumRotationDelta,Quaternion.Angle(Quaternion.Euler(recorded.rotationEuler),actual.rotation));
            result.maximumScaleDelta=Mathf.Max(result.maximumScaleDelta,Vector3.Distance(recorded.scale,actual.lossyScale));
        }
        foreach(var building in original.buildings)Check(building.transform,GameObject.Find(building.name)?.transform);
        Check(original.house5,GameObject.Find(original.house5.name)?.transform);
        result.passed=result.checkedBuildings==19&&result.maximumPositionDelta<.0001f&&result.maximumRotationDelta<.001f&&result.maximumScaleDelta<.0001f;
        File.WriteAllText("street_qa/district_complete/building_pose_preservation.json",JsonUtility.ToJson(result,true));
        if(!result.passed)throw new InvalidOperationException("A building pose differs from the initial inventory.");
        Debug.Log("DISTRICT_BUILDING_POSES checked="+result.checkedBuildings+" positionDelta="+result.maximumPositionDelta+" rotationDelta="+result.maximumRotationDelta+" scaleDelta="+result.maximumScaleDelta);
    }
    public static void AuditSaved()
    {
        OpenSaved();DistrictSurfaceAuditStage04.Check();
    }
    public static void DiagnoseEndpoints()
    {
        OpenSaved();var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var method=typeof(StreetQA).GetMethod("PavedDestination",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        foreach(var route in access.routes)
        {
            var p=route.points.Last();if((bool)method.Invoke(null,new object[]{p,access.surfaces}))continue;
            Debug.Log("ENDPOINT_FAIL "+route.address+" point="+p.ToString("F4"));
            foreach(var hit in Physics.RaycastAll(p+Vector3.up*2,Vector3.down,4))
            {
                var mc=hit.collider as MeshCollider;var provenance=hit.collider.GetComponent<SavedPavedSurface>();
                var renderer=hit.collider.GetComponent<Renderer>();
                Debug.Log("ENDPOINT_HIT go="+hit.collider.name+" mesh="+(mc&&mc.sharedMesh?mc.sharedMesh.name:"none")+" y="+hit.point.y+" normal="+hit.normal+" connector="+access.surfaces.Contains(mc)+" destination="+(provenance&&provenance.existingNetworkDestination)+" materials="+(renderer?string.Join(",",renderer.sharedMaterials.Select(m=>m?m.name:"null")):"none"));
            }
        }
    }
    public static void RefineAndBuild()
    {
        OpenSaved();Directory.CreateDirectory(Output);
        var paving=AssetDatabase.LoadAssetAtPath<Material>("Assets/PhotoReconstruction/Stage04Courtyard/Materials/CourtGreyRectangularPavers.mat");
        paving.SetColor("_Color",new Color(.65f,.67f,.65f));paving.SetFloat("_Colorize",.12f);paving.SetFloat("_PaverGreyFraction",1);EditorUtility.SetDirty(paving);
        var court=GameObject.Find("Двор №7 · мощение и подпорные стенки по фото · stage04");
        if(!court)throw new InvalidOperationException("Apply the court before final refinements.");
        var surface=court.GetComponentsInChildren<MeshCollider>().Single(c=>c.sharedMesh&&c.sharedMesh.name.StartsWith("Stage04CourtPaving",StringComparison.Ordinal));
        CleanCourtInternalSlabSides(surface);
        SavedSurfaceStage03.MarkPrimaryNetwork();
        var provenance=surface.GetComponent<SavedPavedSurface>();if(!provenance)provenance=surface.gameObject.AddComponent<SavedPavedSurface>();
        provenance.existingNetworkDestination=true;provenance.purpose="Photographed inner courtyard pedestrian network";
        PhotoEntranceStage01.ApplyCourt7();
        RemoveCoveredInteriorEdges(court,surface);
        int grassTriangles=0;
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if(!filter.sharedMesh||!filter.sharedMesh.name.Contains("StreetGrass_"))continue;
            var renderer=filter.GetComponent<Renderer>();if(!renderer||!renderer.bounds.Intersects(new Bounds(new Vector3(42,.5f,38),new Vector3(40,4,25))))continue;
            var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);var v=mesh.vertices;int removed=0;
            bool Covered(Vector3 p)=>surface.Raycast(new Ray(p+Vector3.up*2,Vector3.down),out var hit,3)&&hit.normal.y>.82f;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var indices=mesh.GetTriangles(sub);var keep=new System.Collections.Generic.List<int>();
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=filter.transform.TransformPoint(v[indices[i]]);var b=filter.transform.TransformPoint(v[indices[i+1]]);var c=filter.transform.TransformPoint(v[indices[i+2]]);
                    if(Covered(a)||Covered(b)||Covered(c)||Covered((a+b+c)/3)){removed++;continue;}
                    keep.Add(indices[i]);keep.Add(indices[i+1]);keep.Add(indices[i+2]);
                }
                mesh.SetTriangles(keep,sub);
            }
            if(removed==0){UnityEngine.Object.DestroyImmediate(mesh);continue;}
            mesh.name="FinalCourtGrass_"+filter.sharedMesh.name;
            string path="Assets/PhotoReconstruction/Stage04Courtyard/Meshes/FinalGrass_"+GlobalObjectId.GetGlobalObjectIdSlow(filter).targetObjectId+".asset";
            AssetDatabase.CreateAsset(mesh,path);filter.sharedMesh=mesh;grassTriangles+=removed;
        }
        OptimizeFlatGround();
        SavedSurfaceStage03.ReprojectCurrentChecks();
        DistrictSurfaceAuditStage04.Check();
        var report=JsonUtility.FromJson<DistrictSurfaceAuditStage04.Report>(File.ReadAllText("street_qa/district_stage04_geometry_audit.json"));
        if(!report.passed)throw new InvalidOperationException("Final refinements must retain zero route findings.");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        Capture("court_eye_level_final",new Vector3(26,1.7f,37),new Vector3(56,1.6f,40));
        Capture("court_walls_final",new Vector3(44,1.7f,40),new Vector3(30,.35f,45));
        Debug.Log("DISTRICT_FINAL_REFINEMENTS grassOnPavingTrianglesRemoved="+grassTriangles+" routesPassed=True");
        StreetBuilder.BuildGeneratedScene();
    }
    static void OptimizeFlatGround()
    {
        var filter=UnityEngine.Object.FindObjectsByType<MeshFilter>().Single(f=>f.name=="Grass"&&f.sharedMesh&&f.sharedMesh.vertexCount>50000&&f.GetComponent<Renderer>().bounds.size.x>1000);
        var original=filter.sharedMesh;var local=original.vertices;var world=local.Select(filter.transform.TransformPoint).ToArray();
        if(AssetDatabase.GetAssetPath(original).EndsWith("/DistrictGroundOptimized.asset",StringComparison.Ordinal))
        {Debug.Log("DISTRICT_TERRAIN_OPTIMIZED already optimized; preserved verified surface");return;}
        var bounds=filter.GetComponent<Renderer>().bounds;
        int nx=Mathf.RoundToInt(bounds.size.x/5)+1,nz=Mathf.RoundToInt(bounds.size.z/5)+1;
        if(nx*nz!=local.Length)throw new InvalidOperationException("Unexpected terrain grid layout.");
        float baseline=world.Min(p=>p.y);var indices=new System.Collections.Generic.List<int>();int merged=0;
        void Quad(int a,int b,int c,int d){indices.Add(a);indices.Add(c);indices.Add(b);indices.Add(b);indices.Add(c);indices.Add(d);}
        for(int z=0;z<nz-1;z+=4)for(int x=0;x<nx-1;x+=4)
        {
            int right=Mathf.Min(x+4,nx-1),top=Mathf.Min(z+4,nz-1);bool flat=true;
            for(int j=z;j<=top;j++)for(int i=x;i<=right;i++)if(Mathf.Abs(world[j*nx+i].y-baseline)>.000001f)flat=false;
            if(flat){Quad(z*nx+x,z*nx+right,top*nx+x,top*nx+right);merged++;}
            else for(int j=z;j<top;j++)for(int i=x;i<right;i++)Quad(j*nx+i,j*nx+i+1,(j+1)*nx+i,(j+1)*nx+i+1);
        }
        var mesh=UnityEngine.Object.Instantiate(original);mesh.name="PhotoDistrictGround04";mesh.triangles=indices.ToArray();mesh.RecalculateBounds();
        string path="Assets/PhotoReconstruction/Stage04Terrain/Meshes/DistrictGroundOptimized.asset";AssetDatabase.CreateAsset(mesh,path);
        filter.sharedMesh=mesh;var collider=filter.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;Physics.SyncTransforms();
        int checks=0;
        for(int z=0;z<nz-1;z+=3)for(int x=0;x<nx-1;x+=3)
        {
            var point=world[z*nx+x]*.2f+world[z*nx+x+1]*.37f+world[(z+1)*nx+x]*.43f;checks++;
            if(!collider.Raycast(new Ray(point+Vector3.up,Vector3.down),out var hit,2)||Mathf.Abs(hit.point.y-point.y)>.001f)throw new InvalidOperationException("Optimized terrain differs from the original sampled surface: "+point);
        }
        File.WriteAllText(Output+"/ground_optimization.json","{\"trianglesBefore\":"+original.triangles.Length/3+",\"trianglesAfter\":"+indices.Count/3+",\"flatBlocksMerged\":"+merged+",\"physicalChecks\":"+checks+",\"surfacePreserved\":true}");
        Debug.Log("DISTRICT_TERRAIN_OPTIMIZED triangles="+indices.Count/3+" previous="+original.triangles.Length/3+" physicalChecks="+checks);
    }
    static void RemoveCoveredInteriorEdges(GameObject court,MeshCollider paving)
    {
        int removed=0;
        bool Covered(Vector3 p)=>paving.Raycast(new Ray(p+Vector3.up*2,Vector3.down),out var hit,3)&&hit.normal.y>.82f;
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if(!filter.sharedMesh||filter.sharedMesh.name=="PhotoDistrictGround04"||filter.name=="Grass"||filter.transform.IsChildOf(court.transform)||filter.GetComponentInParent<StreetBuilding>())continue;
            var renderer=filter.GetComponent<MeshRenderer>();if(!renderer||!renderer.bounds.Intersects(paving.bounds))continue;
            var original=filter.sharedMesh;var vertices=original.vertices;var materials=renderer.sharedMaterials;
            var mesh=UnityEngine.Object.Instantiate(original);int count=0;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var material=sub<materials.Length?materials[sub]:null;string n=material?material.name:"";
                bool candidate=n.Contains("Asphalt")||n.Contains("Concrete")||n.Contains("Kerb")||n.Contains("Edge")||material&&material.HasProperty("_SurfaceClass")&&material.GetFloat("_SurfaceClass")>5.5f;
                if(!candidate)continue;
                var indices=mesh.GetTriangles(sub);var keep=new System.Collections.Generic.List<int>();
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=filter.transform.TransformPoint(vertices[indices[i]]);var b=filter.transform.TransformPoint(vertices[indices[i+1]]);var c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                    bool enclosed=Mathf.Max(a.y,Mathf.Max(b.y,c.y))<.27f&&Covered((a+b+c)/3)&&Covered(a)&&Covered(b)&&Covered(c);
                    if(enclosed){count++;continue;}keep.Add(indices[i]);keep.Add(indices[i+1]);keep.Add(indices[i+2]);
                }
                mesh.SetTriangles(keep,sub);
            }
            if(count==0){UnityEngine.Object.DestroyImmediate(mesh);continue;}
            mesh.name="CourtInteriorEdgeClean_"+original.name;
            AssetDatabase.CreateAsset(mesh,"Assets/PhotoReconstruction/Stage04Courtyard/Meshes/InteriorEdges_"+GlobalObjectId.GetGlobalObjectIdSlow(filter).targetObjectId+".asset");
            filter.sharedMesh=mesh;var collider=filter.GetComponent<MeshCollider>();if(collider){collider.sharedMesh=null;collider.sharedMesh=mesh;}
            removed+=count;
        }
        Physics.SyncTransforms();Debug.Log("DISTRICT_INTERIOR_EDGES obsolete covered edge triangles removed="+removed);
    }
    static void CleanCourtInternalSlabSides(MeshCollider collider)
    {
        var original=collider.sharedMesh;var vertices=original.vertices;var indices=original.triangles;var keep=new System.Collections.Generic.List<int>();int removed=0;
        bool TopAt(Vector3 point)=>collider.Raycast(new Ray(point+Vector3.up*2,Vector3.down),out var hit,3)&&hit.normal.y>.85f;
        for(int i=0;i<indices.Length;i+=3)
        {
            var a=collider.transform.TransformPoint(vertices[indices[i]]);var b=collider.transform.TransformPoint(vertices[indices[i+1]]);var c=collider.transform.TransformPoint(vertices[indices[i+2]]);
            var n=Vector3.Cross(b-a,c-a).normalized;var outward=new Vector3(n.x,0,n.z).normalized;var centre=(a+b+c)/3;
            if(Mathf.Abs(n.y)<.5f&&outward.sqrMagnitude>.5f&&TopAt(centre+outward*.06f)&&TopAt(centre-outward*.06f)){removed++;continue;}
            keep.Add(indices[i]);keep.Add(indices[i+1]);keep.Add(indices[i+2]);
        }
        if(removed==0)return;
        var mesh=UnityEngine.Object.Instantiate(original);mesh.triangles=keep.ToArray();mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,"Assets/PhotoReconstruction/Stage04Courtyard/Meshes/Stage04CourtPavingClean.asset");
        collider.GetComponent<MeshFilter>().sharedMesh=mesh;collider.sharedMesh=null;collider.sharedMesh=mesh;Physics.SyncTransforms();
        Debug.Log("DISTRICT_COURT internal slab faces removed="+removed+" exterior slab closed");
    }
    [MenuItem("Street/Photo reconstruction/Apply district terrain and central courtyard")]
    public static void ApplyAndBuild()
    {
        OpenSaved();Directory.CreateDirectory(Output);
        if(GameObject.Find("Двор №7 · мощение и подпорные стенки по фото · stage04"))throw new InvalidOperationException("Stage04 already saved. Use its restoration point before changing the layout.");
        var backup=Path.GetFullPath("../.local-backups/district_stage04_20261104");Directory.CreateDirectory(backup);
        if(!File.Exists(backup+"/Street.unity"))File.Copy("Assets/Scenes/Street.unity",backup+"/Street.unity");
        var buildings=UnityEngine.Object.FindObjectsByType<StreetBuilding>();
        var poses=buildings.ToDictionary(b=>b,b=>(b.transform.position,b.transform.rotation,b.transform.localScale));
        if(File.Exists(Output+"/before_routes.json")&&!File.Exists(Output+"/before_routes_original.json"))File.Copy(Output+"/before_routes.json",Output+"/before_routes_original.json");
        SavedSurfaceStage03.RefreshMergedSurfaces();
        DistrictSurfaceAuditStage04.Check();File.Copy("street_qa/district_stage04_geometry_audit.json",Output+"/before_routes.json",true);
        Capture("before_center",new Vector3(19,9,17),new Vector3(44,.1f,38));
        PhotoCentralCourtyardStage04.Apply();
        PhotoDistrictTerrainStage04.Apply();
        PhotoStreetLampStage03.UpgradeCourtyardGlobes();
        SavedSurfaceStage03.ReprojectCurrentChecks();
        foreach(var b in buildings)
            if(b.transform.position!=poses[b].Item1||b.transform.rotation!=poses[b].Item2||b.transform.localScale!=poses[b].Item3)throw new InvalidOperationException("District stage moved a building: "+b.name);
        Physics.SyncTransforms();DistrictSurfaceAuditStage04.Check();
        File.Copy("street_qa/district_stage04_geometry_audit.json",Output+"/after_routes.json",true);
        var report=JsonUtility.FromJson<DistrictSurfaceAuditStage04.Report>(File.ReadAllText("street_qa/district_stage04_geometry_audit.json"));
        if(!report.passed)throw new InvalidOperationException("District route audit found gaps/unsafe slopes. Scene has not been saved; inspect district_stage04_geometry_audit.json.");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        Capture("after_center",new Vector3(19,9,17),new Vector3(44,.1f,38));
        Capture("court_eye_level",new Vector3(26,1.7f,37),new Vector3(56,1.6f,40));
        Capture("court_walls",new Vector3(44,1.7f,40),new Vector3(30,.35f,45));
        Capture("north",new Vector3(-98,11,181),new Vector3(-86,0,145));
        Capture("west",new Vector3(-116,9,12),new Vector3(-70,0,12));
        Capture("east",new Vector3(231,10,36),new Vector3(193,0,34));
        Capture("south",new Vector3(139,11,-215),new Vector3(72,0,-158));
        StreetPavingAudit.Check();
        Debug.Log("DISTRICT_STAGE04_READY buildingsUnmoved="+buildings.Length+" routeSamples="+report.totalSamples+" gaps=0");
        StreetBuilder.BuildGeneratedScene();
    }
    static void OpenSaved()
    {
        for(int i=0;i<EditorSceneManager.sceneCount;i++)if(EditorSceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scenes before applying district stage.");
        EditorSceneManager.OpenScene("Assets/Scenes/Street.unity");Physics.SyncTransforms();
    }
    static void Capture(string name,Vector3 position,Vector3 target)
    {
        var go=new GameObject("Temporary district review");var camera=go.AddComponent<Camera>();camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
        camera.farClipPlane=1200;camera.fieldOfView=64;camera.clearFlags=CameraClearFlags.Skybox;
        var rt=new RenderTexture(1600,900,24);camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
        var picture=new Texture2D(1600,900,TextureFormat.RGB24,false);picture.ReadPixels(new Rect(0,0,1600,900),0,0);picture.Apply();
        File.WriteAllBytes(Output+"/"+name+".png",picture.EncodeToPNG());RenderTexture.active=previous;
        UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(picture);
    }
}
