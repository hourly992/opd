using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Offline repair of authored meshes. Never reads archived map input.
public static class SavedSurfaceStage03
{
    const string Folder="../tools/surface_stage03";
    const string AssetsRoot="Assets/PhotoReconstruction/Stage03Surfaces";
    [Serializable] public class Surface
    {
        public string key,name,role,material,objectId;
        public int sub;
        public Vector3[] vertices,protectedPoints;
        public int[] triangles;
    }
    [Serializable] public class Building {public float[] min,max;}
    [Serializable] public class Input {public Surface[] surfaces;public Building[] buildings;}
    [Serializable] public class Triangle {public float[] a,b,c;}
    [Serializable] public class Ring {public Vector3[] points;public bool hole;}
    [Serializable] public class Boundary {public Vector3[] points;public bool hole;}
    [Serializable] public class Patched {public string key;public Triangle[] triangles;public Boundary[] boundaries;}
    [Serializable] public class Output {public Patched[] surfaces;}
    [Serializable] public class CheckReport
    {
        public int buildingsUnmoved,sourceBatches,newTriangles,asphaltTriangles,updatedParkingChecks,updatedAccessChecks,shiftedProbePoints;
        public float maximumProbeShift;
        public string note;
    }
    static void OpenSaved()
    {
        for(int i=0;i<EditorSceneManager.sceneCount;i++)if(EditorSceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scenes first.");
        EditorSceneManager.OpenScene("Assets/Scenes/Street.unity");Physics.SyncTransforms();
    }
    static Vector3 V(float[] p)=>new Vector3(p[0],p[1],p[2]);
    static int shiftedProbes;
    static float maximumProbeShift;
    static string Role(MeshFilter filter,Material material)
    {
        string name=filter.sharedMesh.name;
        if(name=="ReferenceRoad"||name.Contains("ReferenceWalkways"))return null;
        if(!(name=="Map_Asphalt"||name=="Map_Path"||name=="Map_Pavement"||name.StartsWith("ParkingSurface_")
            ||name.Contains("ResidentialAccessSurfaces")||name=="PilotSurfaces"||name.Contains("ReferenceWalkways")))return null;
        if(!material)return null;
        if(material.HasProperty("_SurfaceClass")&&material.GetFloat("_SurfaceClass")>5.5f)return "walkway";
        string n=material.name;
        if(n.Contains("Asphalt")&&!n.Contains("Footpath"))return "asphalt";
        if(n.Contains("Footpath")||name=="Map_Path"||name=="Map_Pavement"||n=="WarmPaving")return "walkway";
        return null;
    }

    public static void RefreshMergedSurfaces()
    {
        var root=GameObject.Find("Покрытия · исправление стыков stage03");if(!root)throw new InvalidOperationException("Apply stage03 before refreshing its saved topology.");
        var input=JsonUtility.FromJson<Input>(File.ReadAllText(Folder+"/input.json"));
        var output=JsonUtility.FromJson<Output>(File.ReadAllText(Folder+"/output_unity.json"));
        var filters=root.GetComponentsInChildren<MeshFilter>();int count=0;
        for(int index=0;index<output.surfaces.Length;index++)
        {
            string name="Stage03Surface_"+index;
            var filter=filters.Single(f=>f.sharedMesh&&f.sharedMesh.name==name);var patch=output.surfaces[index];
            bool asphalt=patch.key=="secondaryAsphaltUnion";
            var source=asphalt?input.surfaces.First(s=>s.role=="asphalt"):input.surfaces.Single(s=>s.key==patch.key);
            var material=asphalt?AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Pilot/Materials/ReferenceServiceAsphalt.mat")
                :AssetDatabase.LoadAssetAtPath<Material>(source.material);
            var mesh=new EnvironmentMesh();
            foreach(var triangle in patch.triangles)
            {
                var a=V(triangle.a);var b=V(triangle.b);var c=V(triangle.c);var cross=Vector3.Cross(b-a,c-a);
                var normal=cross/cross.magnitude;
                if(normal.y<0)normal=-normal;if(normal.y<.8f||float.IsNaN(normal.y)||float.IsInfinity(normal.y))throw new InvalidOperationException("Refresh contains an invalid or steep top triangle.");
                mesh.Face(material,normal,a,b,c);
            }
            foreach(var ring in patch.boundaries)
                for(int i=0;i<ring.points.Length-1;i++)
                {
                    var a=ring.points[i];var b=ring.points[i+1];
                    mesh.Face(material,new Vector3(b.z-a.z,0,a.x-b.x).normalized,a,b,new Vector3(b.x,-.02f,b.z),new Vector3(a.x,-.02f,a.z));
                }
            var temporary=mesh.Create(name,"Temporary saved surface refresh",AssetsRoot);
            filter.sharedMesh=temporary.GetComponent<MeshFilter>().sharedMesh;
            filter.GetComponent<MeshRenderer>().sharedMaterials=temporary.GetComponent<MeshRenderer>().sharedMaterials;
            var collider=filter.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=filter.sharedMesh;
            UnityEngine.Object.DestroyImmediate(temporary);count++;
        }
        Physics.SyncTransforms();AssetDatabase.SaveAssets();
        Debug.Log("STAGE04_SURFACE_REFRESH batches="+count+" only existing route gap caps; primary profiles unchanged");
    }
    public static void ReprojectCurrentChecks()
    {
        var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        access.surfaceChecks=Resample(access.surfaceChecks,access.surfaces);
        access.entranceLandingChecks=Resample(access.entranceLandingChecks,access.surfaces);
        foreach(var parking in UnityEngine.Object.FindObjectsByType<ParkingSite>())
        {
            parking.surfaceChecks=Resample(parking.surfaceChecks,parking.surfaces);
            parking.accessChecks=Resample(parking.accessChecks,parking.surfaces);
        }
        Debug.Log("DISTRICT_CHECK_HEIGHTS reprojected onto current hardscape; counts preserved, grass excluded");
    }
    public static void MarkPrimaryNetwork()
    {
        var network=UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
        // The first three saved references are the original carriageway, sidewalks
        // and kerbs. Later entries are the repaired secondary network.
        foreach(var collider in network.surfaces.Take(3))
        {
            var provenance=collider.GetComponent<SavedPavedSurface>();if(!provenance)provenance=collider.gameObject.AddComponent<SavedPavedSurface>();
            provenance.existingNetworkDestination=true;provenance.purpose="Existing primary street and pavement";EditorUtility.SetDirty(provenance);
        }
    }

    public static void Export()
    {
        OpenSaved();Directory.CreateDirectory(Folder);
        var protectedPoints=new List<Vector3>();
        foreach(var p in UnityEngine.Object.FindObjectsByType<ParkingSite>())
        {
            protectedPoints.AddRange(p.accessRoute??Array.Empty<Vector3>());
            protectedPoints.AddRange((p.accessChecks??Array.Empty<Vector3>()).Where((v,i)=>i%5==0));
            foreach(var vehicle in p.vehicles??Array.Empty<StreetVehicle>())
                foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
                    protectedPoints.Add(vehicle.transform.TransformPoint(vehicle.localBounds.center+new Vector3(x*vehicle.localBounds.extents.x,0,z*vehicle.localBounds.extents.z)));
        }
        var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        foreach(var route in access.routes)
            protectedPoints.AddRange(route.points.Where((v,i)=>i==0||i==route.points.Length-1||i%8==0));
        foreach(var t in UnityEngine.Object.FindObjectsByType<Transform>().Where(t=>t.name.StartsWith("Access · ")||t.name.StartsWith("Подход ")))
            protectedPoints.Add(t.position);
        var surfaces=new List<Surface>();
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if(!filter.sharedMesh)continue;
            var renderer=filter.GetComponent<MeshRenderer>();if(!renderer)continue;
            var original=filter.sharedMesh;var vertices=original.vertices;
            string id=GlobalObjectId.GetGlobalObjectIdSlow(filter).ToString();
            for(int sub=0;sub<original.subMeshCount;sub++)
            {
                if(sub>=renderer.sharedMaterials.Length)continue;
                var material=renderer.sharedMaterials[sub];string role=Role(filter,material);if(role==null)continue;
                var indices=original.GetTriangles(sub);var used=new Dictionary<int,int>();var points=new List<Vector3>();var top=new List<int>();
                for(int i=0;i<indices.Length;i+=3)
                {
                    Vector3 a=filter.transform.TransformPoint(vertices[indices[i]]),b=filter.transform.TransformPoint(vertices[indices[i+1]]),c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                    if(Vector3.Cross(b-a,c-a).normalized.y<.85f||Mathf.Max(a.y,Mathf.Max(b.y,c.y))>.32f)continue;
                    for(int j=0;j<3;j++)
                    {
                        int index=indices[i+j];if(!used.TryGetValue(index,out int target)){target=points.Count;used[index]=target;points.Add(filter.transform.TransformPoint(vertices[index]));}
                        top.Add(target);
                    }
                }
                if(top.Count==0)continue;
                var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);bounds.Expand(3);
                surfaces.Add(new Surface{key=id+"/"+sub,objectId=id,sub=sub,name=filter.name+" / "+original.name,role=role,
                    material=AssetDatabase.GetAssetPath(material),vertices=points.ToArray(),triangles=top.ToArray(),
                    protectedPoints=protectedPoints.Where(p=>p.x>=bounds.min.x&&p.x<=bounds.max.x&&p.z>=bounds.min.z&&p.z<=bounds.max.z).ToArray()});
            }
        }
        var buildings=UnityEngine.Object.FindObjectsByType<StreetBuilding>().SelectMany(b=>b.GetComponents<BoxCollider>())
            .Where(c=>c.bounds.size.y>2&&c.bounds.min.y<.4f).Select(c=>new Building{min=new[]{c.bounds.min.x,c.bounds.min.y,c.bounds.min.z},max=new[]{c.bounds.max.x,c.bounds.max.y,c.bounds.max.z}}).ToArray();
        File.WriteAllText(Folder+"/input.json",JsonUtility.ToJson(new Input{surfaces=surfaces.ToArray(),buildings=buildings}));
        Directory.CreateDirectory("street_qa/surface_stage03_editor");
        Capture("before_east_junction",new Vector3(229,8,23),new Vector3(208,0,25));
        Capture("before_south_parking",new Vector3(163,9,-209),new Vector3(129,0,-181));
        Capture("before_sidewalk",new Vector3(119.8f,1.65f,8.8f),new Vector3(127.4f,1.1f,5.87f));
        Debug.Log("SAVED_SURFACE_EXPORT batches="+surfaces.Count+" triangles="+surfaces.Sum(s=>s.triangles.Length/3)+" protected="+protectedPoints.Count+" OSM_INPUT=none");
    }

    public static void ApplyAndBuild()
    {
        OpenSaved();
        var buildings=UnityEngine.Object.FindObjectsByType<StreetBuilding>();
        var poses=buildings.ToDictionary(b=>b,b=>(b.transform.position,b.transform.rotation,b.transform.localScale));
        var input=JsonUtility.FromJson<Input>(File.ReadAllText(Folder+"/input.json"));
        // Unity JsonUtility cannot deserialize jagged ring arrays; only triangle output is consumed here.
        var output=JsonUtility.FromJson<Output>(File.ReadAllText(Folder+"/output_unity.json"));
        var root=GameObject.Find("Покрытия · исправление стыков stage03");
        if(root)throw new InvalidOperationException("Stage03 is already present. Restore the stage03 backup before reapplying changed topology.");
        Directory.CreateDirectory(AssetsRoot+"/Meshes");AssetDatabase.Refresh();
        root=new GameObject("Покрытия · исправление стыков stage03");
        var evidence=new GameObject("Editor evidence · mesh repair of saved layout; not a surveyed new road plan");
        evidence.tag="EditorOnly";evidence.transform.SetParent(root.transform,false);
        var survey=evidence.AddComponent<PhotoSurveyMarker>();survey.category="surface topology";
        survey.observation="Объединены существующие асфальтовые покрытия и устранены зубчатые границы. Здания и оси улиц сохранены.";
        survey.uncertainty="Радиусы скруглений и сглаживание отметок приняты для ремонта сетки. Это не новый обмер и не доказательство точных контуров всех дворов.";
        survey.sourceUrls=Array.Empty<string>();survey.studyBounds=new Bounds(new Vector3(60,0,-40),new Vector3(340,1,440));
        var results=new Dictionary<string,MeshCollider>();
        int newTriangles=0,asphaltTriangles=0;
        foreach(var patch in output.surfaces)
        {
            if(patch.triangles==null||patch.triangles.Length==0)throw new InvalidOperationException("Empty patched surface: "+patch.key);
            bool asphalt=patch.key=="secondaryAsphaltUnion";
            var source=asphalt?input.surfaces.First(s=>s.role=="asphalt"):input.surfaces.Single(s=>s.key==patch.key);
            var material=asphalt?AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Pilot/Materials/ReferenceServiceAsphalt.mat")
                :AssetDatabase.LoadAssetAtPath<Material>(source.material);
            var mesh=new EnvironmentMesh();
            foreach(var triangle in patch.triangles)
            {
                var a=V(triangle.a);var b=V(triangle.b);var c=V(triangle.c);
                var cross=Vector3.Cross(b-a,c-a);var normal=cross/cross.magnitude;if(normal.y<0)normal=-normal;
                if(normal.y<.8f||float.IsNaN(a.y)||float.IsNaN(b.y)||float.IsNaN(c.y))throw new InvalidOperationException("Invalid road triangle: "+patch.key);
                mesh.Face(material,normal,a,b,c);
            }
            foreach(var boundary in patch.boundaries??Array.Empty<Boundary>())
                for(int i=0;i<boundary.points.Length-1;i++)
                {
                    var a=boundary.points[i];var b=boundary.points[i+1];var normal=new Vector3(b.z-a.z,0,a.x-b.x).normalized;
                    mesh.Face(material,normal,a,b,new Vector3(b.x,-.02f,b.z),new Vector3(a.x,-.02f,a.z));
                }
            var go=mesh.Create("Stage03Surface_"+results.Count,asphalt?"Объединённый асфальт проездов и въездов":"Скруглённое мощение · "+results.Count,AssetsRoot);
            go.transform.SetParent(root.transform,false);
            var mc=go.AddComponent<MeshCollider>();mc.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;
            var provenance=go.AddComponent<SavedPavedSurface>();
            provenance.existingNetworkDestination=asphalt||!source.name.Contains("ResidentialAccessSurfaces");
            provenance.purpose=asphalt?"Existing secondary road and parking union":provenance.existingNetworkDestination?"Existing authored footpath":"Entrance connector";
            GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            results.Add(patch.key,mc);newTriangles+=patch.triangles.Length;if(asphalt)asphaltTriangles=patch.triangles.Length;
        }
        foreach(var group in input.surfaces.GroupBy(s=>s.objectId))
        {
            if(!GlobalObjectId.TryParse(group.Key,out var id))throw new InvalidOperationException("Bad saved object ID");
            var filter=GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as MeshFilter;if(!filter)throw new InvalidOperationException("Saved surface missing: "+group.Key);
            var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);mesh.name="Stage03Retained_"+filter.sharedMesh.name;
            foreach(var surface in group)
            {
                if(surface.name.Contains("ReferenceWalkways"))continue;
                if(surface.role=="asphalt"&&results.ContainsKey("secondaryAsphaltUnion")||results.ContainsKey(surface.key))mesh.SetTriangles(Array.Empty<int>(),surface.sub);
            }
            // Old paper-thin route sides and rectangular apron borders must not remain
            // outside the new rounded top. Main street kerbs remain a separate untouched mesh.
            if(filter.sharedMesh.name.Contains("ResidentialAccessSurfaces"))
            {
                var mats=filter.GetComponent<MeshRenderer>().sharedMaterials;
                for(int sub=0;sub<mats.Length;sub++)if(mats[sub]&&mats[sub].name=="ResidentialAccessEdge")mesh.SetTriangles(Array.Empty<int>(),sub);
            }
            string path=AssetsRoot+"/Meshes/"+mesh.name+"_"+group.First().key.Split('/').Last()+"_"+results.Count+".asset";
            // Asset names include a deterministic scene file ID to avoid repeated generic mesh names.
            path=AssetsRoot+"/Meshes/Retained_"+id.targetObjectId+".asset";
            AssetDatabase.CreateAsset(mesh,path);filter.sharedMesh=mesh;
            var collider=filter.GetComponent<MeshCollider>();
            if(mesh.triangles.Length==0)
            {
                filter.GetComponent<MeshRenderer>().enabled=false;
                if(collider){collider.sharedMesh=null;collider.enabled=false;}
            }
            else if(collider)collider.sharedMesh=mesh;
        }
        var previousKerb=GameObject.Find("Photo11 · exposed path edges · provisional alignment");if(previousKerb)previousKerb.SetActive(false);
        var asphaltSurface=results["secondaryAsphaltUnion"];
        var network=UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
        var activeHardscape=results.Values.Concat(network.surfaces).Distinct().ToArray();
        foreach(var site in UnityEngine.Object.FindObjectsByType<ParkingSite>())
        {
            site.surfaces=site.surfaces.Concat(activeHardscape).Distinct().ToArray();
        }
        var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        access.surfaces=access.surfaces.Concat(activeHardscape).Distinct().ToArray();
        network.surfaces=network.surfaces.Concat(results.Values).Distinct().ToArray();
        Physics.SyncTransforms();
        AddWalkwayEdging(output,input,root.transform);
        shiftedProbes=0;maximumProbeShift=0;
        int parkingChecks=0;
        foreach(var site in UnityEngine.Object.FindObjectsByType<ParkingSite>())
        {
            site.surfaceChecks=Resample(site.surfaceChecks,site.surfaces);parkingChecks+=site.surfaceChecks.Length;
            site.accessChecks=Resample(site.accessChecks,site.surfaces);
        }
        access.surfaceChecks=Resample(access.surfaceChecks,access.surfaces);
        access.entranceLandingChecks=Resample(access.entranceLandingChecks,access.surfaces);
        foreach(var b in buildings)if(b.transform.position!=poses[b].Item1||b.transform.rotation!=poses[b].Item2||b.transform.localScale!=poses[b].Item3)throw new InvalidOperationException("A building moved");
        PhotoStreetLampStage03.Apply();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        Capture("after_east_junction",new Vector3(229,8,23),new Vector3(208,0,25));
        Capture("after_south_parking",new Vector3(163,9,-209),new Vector3(129,0,-181));
        Capture("after_sidewalk",new Vector3(119.8f,1.65f,8.8f),new Vector3(127.4f,1.1f,5.87f));
        File.WriteAllText("street_qa/surface_stage03_editor/applied.json",JsonUtility.ToJson(new CheckReport{buildingsUnmoved=buildings.Length,sourceBatches=input.surfaces.Length,
            newTriangles=newTriangles,asphaltTriangles=asphaltTriangles,updatedParkingChecks=parkingChecks,updatedAccessChecks=access.surfaceChecks.Length,
            shiftedProbePoints=shiftedProbes,maximumProbeShift=maximumProbeShift,
            note="Existing hardscape union/fillets, no new mapped route. Probe heights are reprojected onto repaired surface; samples falling outside a deliberately rounded corner are moved to its nearest supported point within 1.2m. Door/vehicle/route points were protected in offline geometry."},true));
        StreetPavingAudit.Check();StreetBuilder.BuildGeneratedScene();
    }

    static void AddWalkwayEdging(Output output,Input input,Transform parent)
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/PhotoReconstruction/Stage01/Materials/PhotoWhitewashedKerb.mat");
        var mesh=new EnvironmentMesh();int count=0;
        foreach(var patch in output.surfaces.Where(p=>p.key!="secondaryAsphaltUnion"))
        {
            var source=input.surfaces.Single(s=>s.key==patch.key);
            if(!source.name.Contains("ResidentialAccessSurfaces")&&source.name!="Мощение, отмостка, клумбы и площадка / PilotSurfaces")continue;
            foreach(var boundary in patch.boundaries??Array.Empty<Boundary>())
            {
                var points=boundary.points;
                Vector3 Miter(int index)
                {
                    int length=points.Length-1;index=(index+length)%length;
                    var previous=points[(index+length-1)%length];var current=points[index];var next=points[(index+1)%length];
                    var a=new Vector3(current.z-previous.z,0,previous.x-current.x).normalized;
                    var b=new Vector3(next.z-current.z,0,current.x-next.x).normalized;
                    var sum=(a+b).normalized;float denominator=Vector3.Dot(sum,b);
                    return denominator>.55f?sum/denominator:b;
                }
                for(int i=0;i<points.Length-1;i++)
                {
                    var a=points[i];var b=points[i+1];var outward=new Vector3(b.z-a.z,0,a.x-b.x).normalized;
                    var ma=Miter(i);var mb=Miter(i+1);
                    int pieces=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/.5f));
                    for(int part=0;part<pieces;part++)
                    {
                        var p=Vector3.Lerp(a,b,(float)part/pieces);var q=Vector3.Lerp(a,b,(float)(part+1)/pieces);
                        if(p.x< -140||p.x>240||p.z< -270||p.z>180)continue;
                        var test=(p+q)*.5f+outward*.07f;
                        var hits=Physics.RaycastAll(test+Vector3.up*2,Vector3.down,3).Where(h=>h.normal.y>.85f).OrderByDescending(h=>h.point.y).ToArray();
                        if(hits.Length==0)continue;var hit=hits[0];
                        if(hit.collider.name!="Grass"&&hit.collider.name!="Газон"||hit.point.y>Mathf.Min(p.y,q.y)+.01f)continue;
                        var mp=Vector3.Lerp(ma,mb,(float)part/pieces);var mq=Vector3.Lerp(ma,mb,(float)(part+1)/pieces);
                        var innerP=p+Vector3.up*.001f;var innerQ=q+Vector3.up*.001f;
                        var c=innerQ+mq*.083f;var d=innerP+mp*.083f;
                        var e=p+mp*.095f-Vector3.up*.007f;var f=q+mq*.095f-Vector3.up*.007f;
                        var g=new Vector3(e.x,-.015f,e.z);var h=new Vector3(f.x,-.015f,f.z);
                        mesh.Face(material,Vector3.up,innerP,innerQ,c,d);
                        mesh.Face(material,(Vector3.up+outward).normalized,d,c,f,e);
                        mesh.Face(material,outward,e,f,h,g);
                        mesh.Face(material,(p-q).normalized,innerP,d,e,g,new Vector3(innerP.x,-.015f,innerP.z));
                        mesh.Face(material,(q-p).normalized,innerQ,new Vector3(innerQ.x,-.015f,innerQ.z),h,f,c);count++;
                    }
                }
            }
        }
        if(count>0)
        {
            var go=mesh.Create("Stage03WalkwayEdging","Низкие кромки скруглённых подходов",AssetsRoot);go.transform.SetParent(parent,false);
            go.AddComponent<MeshCollider>().sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;
        }
        Debug.Log("STAGE03_EDGING grassFacingPieces="+count);
    }

    static Vector3[] Resample(Vector3[] points,MeshCollider[] surfaces)
    {
        var result=new List<Vector3>();
        foreach(var point in points??Array.Empty<Vector3>())
        {
            bool Sample(Vector3 p,out Vector3 found)
            {
                found=default;bool valid=false;float top=float.NegativeInfinity;
                foreach(var c in surfaces)if(c&&c.enabled&&c.Raycast(new Ray(p+Vector3.up*2,Vector3.down),out var hit,3)&&hit.normal.y>.8f&&hit.point.y>top&&HardSurface(c,hit.triangleIndex))
                {top=hit.point.y;found=hit.point;valid=true;}
                return valid;
            }
            if(Sample(point,out var q)){result.Add(q);continue;}
            bool resolved=false;
            for(float distance=.05f;distance<=1.21f&&!resolved;distance+=.05f)
                for(int i=0;i<16;i++)
                {
                    float angle=i*Mathf.PI/8;
                    if(Sample(point+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*distance,out q))
                    {result.Add(q);resolved=true;shiftedProbes++;maximumProbeShift=Mathf.Max(maximumProbeShift,distance);break;}
                }
            if(!resolved)throw new InvalidOperationException("Repair left an unsupported original test point: "+point);
        }
        return result.ToArray();
    }
    static bool HardSurface(MeshCollider collider,int triangle)
    {
        if(!collider.sharedMesh||triangle<0)return false;var renderer=collider.GetComponent<MeshRenderer>();if(!renderer)return false;
        int end=0;
        for(int sub=0;sub<collider.sharedMesh.subMeshCount;sub++)
        {
            end+=(int)collider.sharedMesh.GetIndexCount(sub)/3;if(triangle>=end)continue;
            var material=renderer.sharedMaterials[sub];if(!material)return false;
            float kind=material.HasProperty("_SurfaceClass")?material.GetFloat("_SurfaceClass"):0;
            return !(kind>.5f&&kind<1.5f)&&!material.name.Contains("Grass")&&!material.name.Contains("VergeSurface");
        }
        return false;
    }
    static void Capture(string name,Vector3 position,Vector3 target)
    {
        var go=new GameObject("Temporary surface review");var camera=go.AddComponent<Camera>();camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
        camera.farClipPlane=1200;camera.fieldOfView=62;camera.clearFlags=CameraClearFlags.Skybox;
        var rt=new RenderTexture(1600,900,24);camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
        var image=new Texture2D(1600,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
        File.WriteAllBytes("street_qa/surface_stage03_editor/"+name+".png",image.EncodeToPNG());RenderTexture.active=previous;
        UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);
    }
}
