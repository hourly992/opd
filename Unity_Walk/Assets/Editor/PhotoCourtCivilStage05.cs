using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Photo-supported local civil details. Heights remain explicit estimates, never a surveyed DEM.
public static class PhotoCourtCivilStage05
{
    const string Root="Assets/PhotoReconstruction/Stage05Civil";
    const string Name="Двор №7 · сопряжения и рельеф газонов stage05";
    const string Source="https://realty.yandex.ru/offer/4006141392816993274/";
    [Serializable] class Report
    {
        public string source=Source,note="Raised grass beds, segmented red/brown coping and continuous grey door paving observed in the No.7 summer photographs. Existing island contours and building/road levels preserved. Soil crown 0.045m, block lengths and passage dimensions are visual estimates. No absolute elevations or garage ramp are inferred.";
        public int islands,copingBlocks,soilTriangles,approachCells,physicalJointChecks,removedGrassTriangles;public float maxSoilCrown=.045f,maxApproachSlope,maxJointHeightDifference;public bool passed;
    }
    public static void Apply()
    {
        if(EditorSceneManager.GetActiveScene().path!="Assets/Scenes/Street.unity")throw new InvalidOperationException("Open the saved Street scene first.");
        if(GameObject.Find(Name)){Debug.Log("CIVIL05 already applied.");return;}
        foreach(var sub in new[]{"Meshes","Materials"})Directory.CreateDirectory(Root+"/"+sub);AssetDatabase.Refresh();
        var root=new GameObject(Name);var report=new Report();
        var filters=UnityEngine.Object.FindObjectsByType<MeshFilter>().Where(f=>f.sharedMesh&&f.sharedMesh.name.StartsWith("Stage04RaisedIsland_",StringComparison.Ordinal)).ToArray();
        if(filters.Length!=3)throw new InvalidOperationException("The three saved raised No.7 islands are required.");
        var capSource=AssetDatabase.LoadAssetAtPath<Material>("Assets/PhotoReconstruction/Stage04Courtyard/Materials/RedBrownCoping.mat");
        var cap=Clone(capSource,"CopingBlocks",new Color(.40f,.29f,.25f));
        var capOther=Clone(capSource,"CopingBlocksAlternate",new Color(.43f,.31f,.27f));
        var mortar=Clone(capSource,"CopingMortar",new Color(.24f,.23f,.21f));
        foreach(var f in filters)
        {
            var mr=f.GetComponent<MeshRenderer>();var mats=mr.sharedMaterials;var old=f.sharedMesh;var vertices=old.vertices;var world=vertices.Select(f.transform.TransformPoint).ToArray();
            int grassSub=Array.FindIndex(mats,m=>m&&m.name=="CourtMaintainedGrass"),capSub=Array.FindIndex(mats,m=>m&&m.name=="RedBrownCoping");
            if(grassSub<0||capSub<0)throw new InvalidOperationException("Unexpected saved island materials: "+f.name);
            var grassPoints=old.GetTriangles(grassSub).Select(i=>world[i]).Distinct().ToArray();
            var inside=Hull(grassPoints.Select(XZ));var outside=Hull(world.Select(XZ));
            if(inside.Count!=outside.Count)throw new InvalidOperationException("Island rings have unmatched corners; refusing a guessed coping.");
            // Hull ordering starts at the same left-most corner for the parallel convex rings.
            float grassY=grassPoints.Average(v=>v.y),top=grassY+.025f;
            var body=UnityEngine.Object.Instantiate(old);body.name="IslandBody05_"+report.islands;
            body.SetTriangles(Array.Empty<int>(),grassSub);body.SetTriangles(Array.Empty<int>(),capSub);body.RecalculateBounds();
            body=Save(body,Root+"/Meshes/"+body.name+".asset");f.sharedMesh=body;
            var oldCollider=f.GetComponent<MeshCollider>();oldCollider.sharedMesh=null;oldCollider.sharedMesh=body;
            var detailed=new EnvironmentMesh();
            for(int edge=0;edge<outside.Count;edge++)
            {
                int next=(edge+1)%outside.Count;var a=outside[edge];var b=outside[next];var c=inside[edge];var d=inside[next];
                float length=Vector2.Distance(a,b);if(length<.03f)continue;
                // Continuous mortar bed closes joints; beveled masonry units sit over it.
                Prism(detailed,mortar,new[]{At(a,top-.014f),At(b,top-.014f),At(d,top-.014f),At(c,top-.014f)},.052f,.001f);
                int count=Mathf.Max(1,Mathf.RoundToInt(length/.285f));
                for(int n=0;n<count;n++)
                {
                    float gap=Mathf.Min(.006f/length,.15f/count),t0=(float)n/count+gap*.5f,t1=(float)(n+1)/count-gap*.5f;
                    var quad=new[]{At(Vector2.Lerp(a,b,t0),top+.012f),At(Vector2.Lerp(a,b,t1),top+.012f),At(Vector2.Lerp(c,d,t1),top+.012f),At(Vector2.Lerp(c,d,t0),top+.012f)};
                    Prism(detailed,(n+edge)%5==0?capOther:cap,quad,.072f,.008f);report.copingBlocks++;
                }
            }
            // Concentric triangulated soil; the boundary stays below the photographed retaining cap.
            var center=inside.Aggregate(Vector2.zero,(a,b)=>a+b)/inside.Count;var boundary=new List<Vector2>();
            for(int i=0;i<inside.Count;i++)
            {
                var a=inside[i];var b=inside[(i+1)%inside.Count];int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/.55f));
                for(int j=0;j<count;j++)boundary.Add(Vector2.Lerp(a,b,(float)j/count));
            }
            Vector3 Point(Vector2 p,float t)=>At(p,grassY+.045f*(1-t*t)*(1-t*t));
            const int rings=5;
            for(int ring=0;ring<rings;ring++)for(int i=0;i<boundary.Count;i++)
            {
                int j=(i+1)%boundary.Count;float t0=(float)ring/rings,t1=(float)(ring+1)/rings;
                var a=Point(Vector2.Lerp(center,boundary[i],t0),t0);var b=Point(Vector2.Lerp(center,boundary[j],t0),t0);
                var c=Point(Vector2.Lerp(center,boundary[j],t1),t1);var d=Point(Vector2.Lerp(center,boundary[i],t1),t1);
                if(ring==0){Face(detailed,mats[grassSub],a,c,d);report.soilTriangles++;}
                else{Face(detailed,mats[grassSub],a,b,c);Face(detailed,mats[grassSub],a,c,d);report.soilTriangles+=2;}
            }
            var go=detailed.Create("IslandRelief05_"+report.islands,"Накрывки отдельными блоками и грунт · "+(report.islands+1),Root);go.transform.SetParent(root.transform,false);
            go.AddComponent<MeshCollider>().sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;
            GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccluderStatic);report.islands++;
        }
        LinkDoor(root.transform,report);
        var evidence=new GameObject("EditorOnly · контуры и отметки приблизительные");evidence.tag="EditorOnly";evidence.transform.SetParent(root.transform,false);
        var marker=evidence.AddComponent<PhotoSurveyMarker>();marker.category="courtyard civil profile and paving";marker.geometryProvisional=true;marker.sourceUrls=new[]{Source};
        marker.observation="На летних фото №7: поднятые газоны, штучные красно-коричневые накрывки и непрерывное серое мощение до низкой двери.";
        marker.uncertainty="Контуры сохранены из предыдущего этапа. Выпуклость грунта 4.5см и размеры накрывок оценочные. Это не абсолютные высоты района и не положение гаражного спуска.";
        marker.studyBounds=new Bounds(new Vector3(43,.3f,38),new Vector3(40,2,24));
        report.passed=report.islands==3&&report.copingBlocks>20&&report.approachCells>0&&report.maxJointHeightDifference<.03f&&report.maxApproachSlope<.15f;
        Directory.CreateDirectory("street_qa/district_stage05_editor");File.WriteAllText("street_qa/district_stage05_editor/civil.json",JsonUtility.ToJson(report,true));
        if(!report.passed)throw new InvalidOperationException("Court civil profile failed its geometric bounds.");
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());Debug.Log("CIVIL05 blocks="+report.copingBlocks+" islands="+report.islands+" approachCells="+report.approachCells+" passed=True");
    }
    static void LinkDoor(Transform parent,Report report)
    {
        Physics.SyncTransforms();var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var floors=access.surfaces.Where(c=>c&&c.enabled&&c.sharedMesh).Distinct().ToArray();
        var rect=new List<Vector2>{new Vector2(41.7f,37.155f),new Vector2(60.141938f,37.155f),new Vector2(60.141938f,39.455f),new Vector2(41.7f,39.455f)};
        float Existing(Vector2 p)
        {
            float y=float.NegativeInfinity;
            foreach(var floor in floors)if(floor.Raycast(new Ray(At(p,1),Vector3.down),out var hit,1.5f)&&hit.normal.y>.82f&&hit.point.y<.2f)y=Mathf.Max(y,hit.point.y);
            return y;
        }
        float Height(Vector2 p)
        {
            // A level cross-section avoids copying the previous strip's abrupt
            // sideways height changes into a newly widened pedestrian connection.
            float center=Existing(new Vector2(p.x,38.305f));if(float.IsFinite(center))return center;
            // Physically observed bare stretch between the court and the old narrow
            // apron: interpolate its nearest existing levels, keeping both joins exact.
            float left=p.x,right=p.x,yl=float.NegativeInfinity,yr=float.NegativeInfinity;
            while(left>41.7f&&!float.IsFinite(yl)){left=Mathf.Max(41.7f,left-.08f);yl=Existing(new Vector2(left,38.305f));}
            while(right<60.141938f&&!float.IsFinite(yr)){right=Mathf.Min(60.141938f,right+.08f);yr=Existing(new Vector2(right,38.305f));}
            if(!float.IsFinite(yl)||!float.IsFinite(yr))throw new InvalidOperationException("No two existing hardscape anchors for court connector: "+p);
            return Mathf.Lerp(yl,yr,Mathf.InverseLerp(left,right,p.x));
        }
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/PhotoReconstruction/Stage04Courtyard/Materials/CourtGreyRectangularPavers.mat");
        var mesh=new EnvironmentMesh();int nx=64,nz=9;var points=new Vector3[nx+1,nz+1];
        for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++){var p=new Vector2(Mathf.Lerp(rect[0].x,rect[1].x,(float)x/nx),Mathf.Lerp(rect[0].y,rect[3].y,(float)z/nz));points[x,z]=At(p,Height(p));}
        for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
        {
            var a=points[x,z];var b=points[x+1,z];var c=points[x+1,z+1];var d=points[x,z+1];Face(mesh,material,a,b,c);Face(mesh,material,a,c,d);report.approachCells++;
            foreach(var pair in new[]{(a,b),(a,d)})report.maxApproachSlope=Mathf.Max(report.maxApproachSlope,Mathf.Abs(pair.Item1.y-pair.Item2.y)/Vector2.Distance(XZ(pair.Item1),XZ(pair.Item2)));
        }
        var old=floors.Select(f=>f.GetComponent<MeshFilter>()).Where(f=>f&&!f.GetComponentInParent<StreetBuilding>()).ToArray();
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(old,new List<List<Vector2>>{rect},Root);
        var go=mesh.Create("CourtDoorContinuousPaving05","Серое мощение до низкой двери №7",Root);go.transform.SetParent(parent,false);var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;
        var provenance=go.AddComponent<SavedPavedSurface>();provenance.existingNetworkDestination=true;provenance.purpose="Continuous photographed grey court paving to existing low doorway";
        access.surfaces=access.surfaces.Concat(new[]{collider}).Distinct().ToArray();Physics.SyncTransforms();
        // Register the entire newly connected route, so later independent audits
        // also cover the formerly bare stretch, not only the short old apron.
        access.routes=access.routes.Concat(new[]{new ResidentialAccess.Route{address="7 · непрерывное мощение двора stage05",usesExistingSurface=true,
            points=new[]{points[0,nz/2],points[nx,nz/2]}}}).ToArray();
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if(!filter.sharedMesh||!(filter.name.Contains("StreetGrass_")||filter.sharedMesh.name.Contains("StreetGrass_")||filter.sharedMesh.name.StartsWith("Grass_",StringComparison.Ordinal)))continue;
            var original=filter.sharedMesh;var v=original.vertices;var copy=UnityEngine.Object.Instantiate(original);int removed=0;
            bool Covered(int i){var p=filter.transform.TransformPoint(v[i]);return p.x>=41.69f&&p.x<=60.15f&&p.z>=37.14f&&p.z<=39.47f;}
            for(int sub=0;sub<copy.subMeshCount;sub++)
            {
                var indices=original.GetTriangles(sub);var keep=new List<int>();
                for(int i=0;i<indices.Length;i+=3){if(Covered(indices[i])||Covered(indices[i+1])||Covered(indices[i+2])){removed++;continue;}keep.Add(indices[i]);keep.Add(indices[i+1]);keep.Add(indices[i+2]);}
                copy.SetTriangles(keep,sub);
            }
            if(removed==0){UnityEngine.Object.DestroyImmediate(copy);continue;}
            copy.RecalculateBounds();filter.sharedMesh=Save(copy,Root+"/Meshes/CourtConnectorGrass_"+GlobalObjectId.GetGlobalObjectIdSlow(filter).targetObjectId+".asset");report.removedGrassTriangles+=removed;
            var lod=filter.GetComponentInParent<LODGroup>();if(lod)lod.RecalculateBounds();EditorUtility.SetDirty(filter);
        }
        foreach(var p in points)
        {
            if(!collider.Raycast(new Ray(p+Vector3.up,Vector3.down),out var hit,2))throw new InvalidOperationException("New court connector has a physics hole.");
            report.physicalJointChecks++;report.maxJointHeightDifference=Mathf.Max(report.maxJointHeightDifference,Mathf.Abs(p.y-hit.point.y));
        }
        EditorUtility.SetDirty(access);
    }
    static Material Clone(Material source,string name,Color color)
    {
        if(!source)throw new InvalidOperationException("Missing court coping material.");var m=UnityEngine.Object.Instantiate(source);m.name=name;m.SetColor("_Color",color);m.enableInstancing=true;
        string path=Root+"/Materials/"+name+".mat";var saved=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(saved){EditorUtility.CopySerialized(m,saved);UnityEngine.Object.DestroyImmediate(m);EditorUtility.SetDirty(saved);return saved;}
        AssetDatabase.CreateAsset(m,path);return m;
    }
    static Mesh Save(Mesh mesh,string path)
    {
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);return saved;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static void Prism(EnvironmentMesh m,Material material,Vector3[] top,float depth,float bevel)
    {
        var center=top.Aggregate(Vector3.zero,(a,b)=>a+b)/top.Length;
        var inner=top.Select(p=>p+(center-p).normalized*bevel).ToArray();
        Face(m,material,inner);
        for(int i=0;i<top.Length;i++)
        {
            int j=(i+1)%top.Length;var a=top[i]-Vector3.up*bevel;var b=top[j]-Vector3.up*bevel;
            var n=Vector3.Cross(b-a,Vector3.up).normalized;if(Vector3.Dot(n,(a+b)*.5f-center)<0)n=-n;
            m.Face(material,(n+Vector3.up).normalized,a,b,inner[j],inner[i]);
            m.Face(material,n,top[i]-Vector3.up*depth,top[j]-Vector3.up*depth,b,a);
        }
    }
    static void Face(EnvironmentMesh m,Material material,params Vector3[] p)
    {
        var n=Vector3.Cross(p[1]-p[0],p[2]-p[0]);if(n.sqrMagnitude<1e-14f)return;if(n.y<0)n=-n;m.Face(material,n.normalized,p);
    }
    static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
    static Vector3 At(Vector2 p,float y)=>new Vector3(p.x,y,p.y);
    static List<Vector2> Hull(IEnumerable<Vector2> input)
    {
        var points=input.Select(p=>new Vector2(Mathf.Round(p.x*10000)/10000,Mathf.Round(p.y*10000)/10000)).Distinct().OrderBy(p=>p.x).ThenBy(p=>p.y).ToList();
        float Cross(Vector2 a,Vector2 b,Vector2 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
        var h=new List<Vector2>();foreach(var p in points){while(h.Count>1&&Cross(h[h.Count-2],h[h.Count-1],p)<.00001f)h.RemoveAt(h.Count-1);h.Add(p);}int k=h.Count;
        for(int i=points.Count-2;i>=0;i--){var p=points[i];while(h.Count>k&&Cross(h[h.Count-2],h[h.Count-1],p)<.00001f)h.RemoveAt(h.Count-1);h.Add(p);}h.RemoveAt(h.Count-1);return h;
    }
}
