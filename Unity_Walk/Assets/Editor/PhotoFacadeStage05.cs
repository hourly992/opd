using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A single matched courtyard-facing bay. It does not regenerate a building and it
// deliberately does not spread one photographed window family over the district.
public static class PhotoFacadeStage05
{
    private const string Root="Assets/PhotoReconstruction/Stage05Facade";
    private const string Applied="Фасад №7 · круглое окно по фото · stage05";
    private static readonly Vector3 Entrance=new Vector3(61.091938f,0,38.305f);
    private static readonly Vector3 Forward=Vector3.left,Right=Vector3.forward;
    private const float Spring=14.60f,Wall=-1.34f,Radius=2.05f;
    private const float RoundCenter=15.75f,RoundRadius=.54f;
    private static readonly string[] Sources={
        "https://realty.yandex.ru/offer/4006141392816993274/",
        "https://avatars.mds.yandex.net/get-realty-offers/15038303/2d86a840-819c-4ef2-a67e-6098b9d2a49e/app_large",
        "https://avatars.mds.yandex.net/get-realty-offers/14021747/b50d2391-f3f6-4cf7-b362-9896c4513b7f/app_large"
    };
    [Serializable] private sealed class Report
    {
        public string target,observation,uncertainty;
        public string[] sources;
        public int[] removedTriangles,newTriangles;
        public float previousArchDiameter,newRoundOpeningDiameter;
        public bool buildingPosePreserved,lowerLightsPreserved;
    }
    private sealed class Cut
    {
        internal MeshFilter filter;
        internal Mesh mesh;
        internal Renderer renderer;
        internal int removed;
    }
    private static Vector3 P(float x,float y,float z)=>Entrance+Right*x+Vector3.up*y+Forward*z;

    public static void Apply()
    {
        if(EditorSceneManager.GetActiveScene().path!="Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Photo facade stage05 needs the existing Street scene.");
        if(GameObject.Find(Applied)){Debug.Log("PHOTO_FACADE_STAGE05 already applied.");return;}
        var building=UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(b=>b.address=="7"&&
            b.GetComponentsInChildren<Transform>().Any(t=>t.name=="Access · подход к крыльцу 1"&&Vector3.Distance(t.position,Entrance)<.03f));
        var group=building.GetComponent<LODGroup>();var lods=group.GetLODs();
        // Entrance stage05 may already have added its small renderer to each LOD.
        // Select only the original full-height saved building mesh.
        Cut[] cuts=lods.Select((l,i)=>Prepare(l.renderers.Single(r=>r&&r.GetComponent<MeshFilter>()&&r.bounds.size.y>10),i)).ToArray();
        Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.Refresh();
        Vector3 position=building.transform.position,scale=building.transform.localScale;Quaternion rotation=building.transform.rotation;
        var root=new GameObject(Applied);root.transform.SetParent(building.transform,true);
        int[] added=new int[3];
        for(int lod=0;lod<3;lod++)
        {
            Cut cut=cuts[lod];string path=Root+"/Meshes/Court7_RoundWindowCut_LOD"+lod+".asset";
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved){EditorUtility.CopySerialized(cut.mesh,saved);UnityEngine.Object.DestroyImmediate(cut.mesh);}
            else{saved=cut.mesh;AssetDatabase.CreateAsset(saved,path);}
            cut.filter.sharedMesh=saved;EditorUtility.SetDirty(cut.filter);
            Material Find(string name)=>cut.renderer.sharedMaterials.First(m=>m&&m.name==name);
            var construction=new EnvironmentMesh();
            Build(construction,Find("CreamBrick"),Find("RedBrick"),Find("IvoryFrames"),Find("Glass0"),lod);
            var part=construction.Create("Court7_RoundWindow_LOD"+lod,"Круглое окно №7 · LOD"+lod,Root);
            part.transform.SetParent(root.transform,true);
            var mesh=part.GetComponent<MeshFilter>().sharedMesh;
            added[lod]=(int)Enumerable.Range(0,mesh.subMeshCount).Sum(s=>(long)mesh.GetIndexCount(s))/3;
            GameObjectUtility.SetStaticEditorFlags(part,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccluderStatic);
            lods[lod].renderers=lods[lod].renderers.Concat(new[]{part.GetComponent<Renderer>()}).ToArray();
        }
        group.SetLODs(lods);group.RecalculateBounds();EditorUtility.SetDirty(group);
        var marker=new GameObject("Наблюдение: круглый световой проём; размеры и сопоставление секции оценочные");
        marker.tag="EditorOnly";marker.transform.SetParent(root.transform,false);
        var survey=marker.AddComponent<PhotoSurveyMarker>();survey.category="Оконная архитектура дворового фасада №7";
        survey.observation="Summer No7 courtyard photo: the cream central section above the low grey entrance has a small round glazed opening below its roof; it does not have the model's wide radial semicircular fanlight.";
        survey.uncertainty="Only one previously aligned courtyard-facing model bay is corrected. Address and observed window family are supported; exact section correspondence and opening dimensions are estimates. Existing building storey count and roof geometry still differ from available photos and are not asserted accurate.";
        survey.sourceUrls=Sources;survey.geometryProvisional=true;survey.studyBounds=new Bounds(P(0,15.7f,Wall),new Vector3(.6f,2.4f,4.5f));
        var view=new GameObject("Opening QA · stage05 Court7 round light").transform;view.SetParent(building.transform,true);
        view.position=P(3.0f,15.2f,8.0f);view.rotation=Quaternion.LookRotation(P(0,RoundCenter,Wall)-view.position);
        bool unchanged=position==building.transform.position&&rotation==building.transform.rotation&&scale==building.transform.localScale;
        if(!unchanged)throw new InvalidOperationException("Building pose changed during round-window correction.");
        Directory.CreateDirectory("street_qa");File.WriteAllText("street_qa/photo_facade_stage05.json",JsonUtility.ToJson(new Report{
            target=building.name+" courtyard-facing central upper light",observation=survey.observation,uncertainty=survey.uncertainty,
            sources=Sources,removedTriangles=cuts.Select(c=>c.removed).ToArray(),newTriangles=added,
            previousArchDiameter=Radius*2,newRoundOpeningDiameter=RoundRadius*2,buildingPosePreserved=unchanged,lowerLightsPreserved=true},true));
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("PHOTO_FACADE_STAGE05 removed="+cuts.Sum(c=>c.removed)+" new="+added.Sum()+" one bay, all three LODs");
    }

    public static void RefineAtticVent()
    {
        if(EditorSceneManager.GetActiveScene().path!="Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before refining the photographed gable.");
        var facade=GameObject.Find(Applied);
        if(!facade)throw new InvalidOperationException("Apply the photographed round window before refining its gable.");
        var group=facade.GetComponentInParent<LODGroup>();
        var lods=group.GetLODs();
        // The black dot is a decorative ellipsoid on top of an intact masonry face.
        // Its coordinates come from the inspected saved mesh and its Gable geometry.
        Vector3 ventCenter=P(0,17.6322f,-1.235f);
        const float limitSquared=.181f*.181f;
        for(int lod=0;lod<lods.Length;lod++)
        {
            var renderer=lods[lod].renderers.Single(r=>r&&r.GetComponent<MeshFilter>()&&r.bounds.size.y>10);
            var filter=renderer.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;
            var selected=mesh.vertices.Select(p=>(filter.transform.TransformPoint(p)-ventCenter).sqrMagnitude<limitSquared).ToArray();
            var materials=renderer.sharedMaterials;
            Mesh replacement=null;
            int removed=0;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                if(sub>=materials.Length||!materials[sub]||materials[sub].name!="WindowRecess")continue;
                var triangles=mesh.GetTriangles(sub);var kept=new List<int>(triangles.Length);
                for(int i=0;i<triangles.Length;i+=3)
                {
                    if(selected[triangles[i]]&&selected[triangles[i+1]]&&selected[triangles[i+2]])removed++;
                    else{kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);}
                }
                if(kept.Count==triangles.Length)continue;
                if(!replacement)replacement=UnityEngine.Object.Instantiate(mesh);
                replacement.SetTriangles(kept,sub,false);
            }
            if(removed!=0&&(lod!=0||removed!=120))
                throw new InvalidOperationException("Unexpected attic vent topology in LOD"+lod+": "+removed);
            if(replacement)
            {
                replacement.name="Court7_FacadeWithoutTemplateVent_LOD"+lod+"_"+Guid.NewGuid().ToString("N").Substring(0,8);
                replacement.RecalculateBounds();
                AssetDatabase.CreateAsset(replacement,Root+"/Meshes/"+replacement.name+".asset");
                filter.sharedMesh=replacement;EditorUtility.SetDirty(filter);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
            Debug.Log("PHOTO_FACADE_ATTIC_VENT LOD"+lod+" removed="+removed);
        }
    }

    private static Cut Prepare(Renderer renderer,int lod)
    {
        var filter=renderer.GetComponent<MeshFilter>();var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);
        mesh.name="Court7_RoundWindowCut_LOD"+lod;
        var world=mesh.vertices.Select(filter.transform.TransformPoint).ToArray();int removed=0;
        bool Select(Vector3 p)
        {
            Vector3 d=p-Entrance;float along=Vector3.Dot(d,Forward),across=Mathf.Abs(Vector3.Dot(d,Right));
            return across<=2.201f&&p.y>=Spring-.003f&&p.y<=Spring+Radius+.151f&&along>=Wall-.211f&&along<=Wall+.012f;
        }
        for(int sub=0;sub<mesh.subMeshCount;sub++)
        {
            int[] source=mesh.GetTriangles(sub);var keep=new List<int>(source.Length);
            for(int t=0;t<source.Length;t+=3)
            {
                bool selected=Select(world[source[t]])&&Select(world[source[t+1]])&&Select(world[source[t+2]]);
                if(selected)removed++;else{keep.Add(source[t]);keep.Add(source[t+1]);keep.Add(source[t+2]);}
            }
            mesh.SetTriangles(keep,sub,false);
        }
        if(removed<150)throw new InvalidOperationException("Inspected upper light not fully found in LOD"+lod+"; removed="+removed);
        mesh.RecalculateBounds();return new Cut{filter=filter,renderer=renderer,mesh=mesh,removed=removed};
    }

    private static Vector3 Circle(float angle,float radius,float z)=>P(Mathf.Cos(angle)*radius,RoundCenter+Mathf.Sin(angle)*radius,z);
    private static Vector3 Boundary(float angle)
    {
        float x=Mathf.Cos(angle),y=Mathf.Sin(angle);
        float edgeX=Radius/Mathf.Max(.000001f,Mathf.Abs(x));
        float edgeY=(y>=0?Spring+Radius-RoundCenter:RoundCenter-Spring)/Mathf.Max(.000001f,Mathf.Abs(y));
        float scale=Mathf.Min(edgeX,edgeY);return P(x*scale,RoundCenter+y*scale,Wall);
    }
    private static void Build(EnvironmentMesh m,Material cream,Material red,Material trim,Material glass,int lod)
    {
        int count=lod==0?48:lod==1?32:20;
        var angles=Enumerable.Range(0,count).Select(i=>i*Mathf.PI*2/count).ToList();
        // Include all four rectangle corners so radial masonry sectors close the exact
        // rectangular archived opening without cutting corner gaps or overlapping wall.
        foreach(float x in new[]{-Radius,Radius})foreach(float y in new[]{Spring,Spring+Radius})
            angles.Add(Mathf.Repeat(Mathf.Atan2(y-RoundCenter,x),Mathf.PI*2));
        angles=angles.OrderBy(a=>a).ToList();
        const float outer=.62f,frameOuter=RoundRadius,frameInner=.49f;
        for(int i=0;i<angles.Count;i++)
        {
            float a=angles[i],b=i+1<angles.Count?angles[i+1]:angles[0]+Mathf.PI*2;
            m.Face(cream,Forward,Circle(a,outer,Wall),Boundary(a),Boundary(b),Circle(b,outer,Wall));
            m.Face(red,Forward,Circle(a,RoundRadius,Wall+.002f),Circle(a,outer,Wall+.002f),Circle(b,outer,Wall+.002f),Circle(b,RoundRadius,Wall+.002f));
            Vector3 inward=-(Right*Mathf.Cos((a+b)*.5f)+Vector3.up*Mathf.Sin((a+b)*.5f));
            m.Face(red,inward,Circle(a,RoundRadius,Wall),Circle(b,RoundRadius,Wall),Circle(b,RoundRadius,Wall-.09f),Circle(a,RoundRadius,Wall-.09f));
            m.Face(trim,Forward,Circle(a,frameInner,Wall-.085f),Circle(a,frameOuter,Wall-.085f),Circle(b,frameOuter,Wall-.085f),Circle(b,frameInner,Wall-.085f));
            if(lod<2)m.Face(trim,inward,Circle(a,frameInner,Wall-.085f),Circle(b,frameInner,Wall-.085f),Circle(b,frameInner,Wall-.145f),Circle(a,frameInner,Wall-.145f));
            Vector3[] points={P(0,RoundCenter,Wall-.149f),Circle(a,frameInner,Wall-.149f),Circle(b,frameInner,Wall-.149f)};
            Vector4 UV(float angle)=>new Vector4(.5f+.5f*Mathf.Cos(angle),.5f+.5f*Mathf.Sin(angle),.39f,1);
            m.WindowFace(glass,Forward,points,new[]{new Vector4(.5f,.5f,.39f,1),UV(a),UV(b)});
        }
    }
}
