using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Repairs two previously aligned low-threshold bays in the saved scene. No source-map
// generation, building transform edits, or inferred propagation to other entrances.
public static class PhotoEntranceStage05
{
    private const string Root = "Assets/PhotoReconstruction/Stage05Entrances";
    private const string Applied = "Входы · фотоориентиры №7 и №11 · stage05";
    private sealed class Target
    {
        internal string key, address, marker, description;
        internal Vector3 entrance;
        internal bool shop;
        internal string[] sources;
        internal Vector3 Forward => Vector3.left;
        internal Vector3 Right => Vector3.forward;
        internal Vector3 P(float x, float y, float z) => entrance + Right*x + Vector3.up*y + Forward*z;
    }
    private sealed class Plan
    {
        internal Target target;
        internal StreetBuilding building;
        internal Renderer[] renderers;
        internal Mesh[] meshes;
        internal int[] removed;
    }
    [Serializable] private sealed class Change
    {
        public string target, observation, uncertainty;
        public string[] sources;
        public int[] removedTemplateTriangles;
        public Vector3 entrance;
        public float doorHeight, portalTopBefore, canopyTop;
        public bool buildingPosePreserved;
    }
    [Serializable] private sealed class Report
    {
        public string scene, status, scope;
        public Change[] changes;
    }
    [Serializable] private sealed class ResidualChange
    {
        public string target, sourceMesh, newMesh;
        public int lod, restoredTriangles;
        public float restoredArea;
    }
    [Serializable] private sealed class ResidualReport
    {
        public string note;
        public int restoredTriangles;
        public ResidualChange[] lods;
    }

    // Restores the exact original masonry row accidentally included above the
    // former portal cut. The apparent black beam is a missing strip of facade.
    public static void RefineResiduals()
    {
        if(EditorSceneManager.GetActiveScene().path!="Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before restoring entrance masonry.");
        const string markerName="Входы · кладка над проёмами восстановлена · stage05";
        if(GameObject.Find(markerName)){Debug.Log("PHOTO_ENTRANCE_RESIDUALS masonry already restored.");return;}
        var targets=new[]{
            new Target{key="Court7",address="7",marker="Access · подход к крыльцу 1",entrance=new Vector3(61.091938f,0,38.305f)},
            new Target{key="Shop11",address="11",marker="Access · подход к крыльцу 5",entrance=new Vector3(125.4542f,0,5.86925f)}
        };
        Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.Refresh();
        var report=new List<ResidualChange>();
        foreach(var target in targets)
        {
            var building=UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(b=>b.address==target.address&&
                b.GetComponentsInChildren<Transform>().Any(a=>a.name==target.marker&&Vector3.Distance(a.position,target.entrance)<.03f));
            var lods=building.GetComponent<LODGroup>().GetLODs();
            string originalPrefix=target.shop||target.key=="Shop11"?
                "Assets/PhotoReconstruction/Stage01/Meshes/Photo11Entrance_LOD":
                "Assets/PhotoReconstruction/Stage04Courtyard/Meshes/Court7Entrance_LOD";
            for(int lod=0;lod<3;lod++)
            {
                var filter=lods[lod].renderers.Single(r=>r&&r.GetComponent<MeshFilter>()&&r.bounds.size.y>10).GetComponent<MeshFilter>();
                Mesh current=filter.sharedMesh,original=AssetDatabase.LoadAssetAtPath<Mesh>(originalPrefix+lod+".asset");
                if(!original||current.vertexCount!=original.vertexCount||current.subMeshCount!=original.subMeshCount)
                    throw new InvalidOperationException("The saved facade topology differs from the inspected original: "+target.key+" LOD"+lod);
                var originalVertices=original.vertices;var currentVertices=current.vertices;
                var world=originalVertices.Select(filter.transform.TransformPoint).ToArray();
                var replacement=UnityEngine.Object.Instantiate(current);
                int found=0,restored=0;float area=0;
                for(int sub=0;sub<original.subMeshCount;sub++)
                {
                    var before=original.GetTriangles(sub);var after=current.GetTriangles(sub);
                    var extra=new List<int>();
                    for(int i=0;i<before.Length;i+=3)
                    {
                        int a=before[i],b=before[i+1],c=before[i+2];
                        if(!MasonryRow(world[a],target)||!MasonryRow(world[b],target)||!MasonryRow(world[c],target))continue;
                        if(currentVertices[a]!=originalVertices[a]||currentVertices[b]!=originalVertices[b]||currentVertices[c]!=originalVertices[c])
                            throw new InvalidOperationException("Refusing to restore altered original masonry vertices.");
                        float triangleArea=Vector3.Cross(world[b]-world[a],world[c]-world[a]).magnitude*.5f;
                        if(triangleArea<.000001f)continue;
                        found++;area+=triangleArea;
                        bool present=false;
                        for(int j=0;j<after.Length;j+=3)
                            if(after[j]==a&&after[j+1]==b&&after[j+2]==c){present=true;break;}
                        if(!present){extra.Add(a);extra.Add(b);extra.Add(c);restored++;}
                    }
                    if(extra.Count>0)replacement.SetTriangles(after.Concat(extra).ToArray(),sub,false);
                }
                if(found!=14||Mathf.Abs(area-1.925f)>.002f||restored!=0&&restored!=14)
                    throw new InvalidOperationException("Unexpected facade seam geometry: "+target.key+" LOD"+lod+" found="+found+" restored="+restored+" area="+area);
                string newPath="";
                if(restored>0)
                {
                    replacement.name=target.key+"_SealedFacade_LOD"+lod+"_"+Guid.NewGuid().ToString("N").Substring(0,8);
                    replacement.RecalculateBounds();newPath=Root+"/Meshes/"+replacement.name+".asset";
                    AssetDatabase.CreateAsset(replacement,newPath);filter.sharedMesh=replacement;EditorUtility.SetDirty(filter);
                }
                else UnityEngine.Object.DestroyImmediate(replacement);
                report.Add(new ResidualChange{target=target.key,lod=lod,sourceMesh=AssetDatabase.GetAssetPath(current),
                    newMesh=newPath,restoredTriangles=restored,restoredArea=restored>0?area:0});
            }
        }
        var result=new ResidualReport{note="Restored the exact original 5.50 x 0.35 m coplanar masonry row (y=4.20 to 4.55 m); old portal geometry remains removed. Original materials, UVs and vertex positions are preserved.",
            restoredTriangles=report.Sum(r=>r.restoredTriangles),lods=report.ToArray()};
        Directory.CreateDirectory("street_qa");File.WriteAllText("street_qa/photo_entrance_residuals_stage05.json",JsonUtility.ToJson(result,true));
        var marker=new GameObject(markerName);marker.tag="EditorOnly";
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("PHOTO_ENTRANCE_RESIDUALS restored masonry triangles="+result.restoredTriangles+" across "+report.Count+" LODs");
    }

    private static bool MasonryRow(Vector3 p,Target t)
    {
        Vector3 d=p-t.entrance;
        return p.y>=4.199f&&p.y<=4.551f&&Mathf.Abs(Vector3.Dot(d,t.Right))<=2.751f&&
            Mathf.Abs(Vector3.Dot(d,t.Forward)+1.34f)<.002f;
    }

    public static void Apply()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Stage05 entrance repair requires the existing Street scene.");
        if (GameObject.Find(Applied)) { Debug.Log("PHOTO_ENTRANCES_STAGE05 already present; no second patch."); return; }
        var targets = new[] {
            new Target { key="Court7", address="7", marker="Access · подход к крыльцу 1",
                entrance=new Vector3(61.091938f,0,38.305f),
                description="Summer address No7 photos show a low grey rectangular entrance below a wide green horizontal canopy; the generic high masonry arch and fanlight are unsupported here.",
                sources=new[]{"https://realty.yandex.ru/offer/4006141392816993274/",
                    "https://avatars.mds.yandex.net/get-realty-offers/15038303/2d86a840-819c-4ef2-a67e-6098b9d2a49e/app_large",
                    "https://avatars.mds.yandex.net/get-realty-offers/14021747/b50d2391-f3f6-4cf7-b362-9896c4513b7f/app_large"} },
            new Target { key="Shop11", address="11", marker="Access · подход к крыльцу 5", shop=true,
                entrance=new Vector3(125.4542f,0,5.86925f),
                description="Summer No11 shop photos show green horizontal cladding and a low mono-pitched metal canopy on narrow posts, not a high arched residential portal.",
                sources=new[]{"https://yandex.ru/maps/org/91706029280/",
                    "https://avatars.mds.yandex.net/get-altay/16333276/2a00000198bb44206efe19b972c5a4dbad39/orig"} }
        };
        // Prepare every destructive selection before assigning any changed scene mesh.
        Plan[] plans=targets.Select(Prepare).ToArray();
        Directory.CreateDirectory(Root+"/Meshes"); Directory.CreateDirectory(Root+"/Materials"); AssetDatabase.Refresh();
        var grey=CloneMaterial("EntranceConcrete","PhotoDoorGrey",new Color(.43f,.46f,.47f),.10f);
        var green=CloneMaterial("EntranceSteel","PhotoCanopyGreen",new Color(.19f,.29f,.25f),.20f);
        var metal=CloneMaterial("EntranceSteel","PhotoFrameGraphite",new Color(.11f,.14f,.15f),.17f);
        var plinth=CloneMaterial("EntranceConcrete","PhotoCourtPlinthGrey",new Color(.46f,.48f,.46f),.05f);
        var control=new GameObject(Applied); control.tag="EditorOnly";
        var changes=new List<Change>();
        foreach (Plan plan in plans)
        {
            Target t=plan.target;
            var originalPosition=plan.building.transform.position;
            var originalRotation=plan.building.transform.rotation;
            var originalScale=plan.building.transform.localScale;
            var root=new GameObject("Фотоориентир · "+t.key+" · размеры приблизительные");
            root.transform.SetParent(plan.building.transform,true);
            var group=plan.building.GetComponent<LODGroup>(); var lods=group.GetLODs();
            for(int lod=0;lod<3;lod++)
            {
                string path=Root+"/Meshes/"+t.key+"_FacadeCut_LOD"+lod+".asset";
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved){EditorUtility.CopySerialized(plan.meshes[lod],saved);UnityEngine.Object.DestroyImmediate(plan.meshes[lod]);}
                else {saved=plan.meshes[lod];AssetDatabase.CreateAsset(saved,path);}
                plan.renderers[lod].GetComponent<MeshFilter>().sharedMesh=saved;
                EditorUtility.SetDirty(plan.renderers[lod].GetComponent<MeshFilter>());
                var cream=plan.renderers[lod].sharedMaterials.First(m=>m&&m.name=="CreamBrick");
                var mesh=new EnvironmentMesh();
                BuildFacade(mesh,t,cream,plinth,green,grey,metal,lod);
                var part=mesh.Create(t.key+"_PhotoEntrance_LOD"+lod,"Фото-вход "+t.key+" · LOD"+lod,Root,true);
                part.transform.SetParent(root.transform,true);
                GameObjectUtility.SetStaticEditorFlags(part,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccluderStatic);
                lods[lod].renderers=lods[lod].renderers.Concat(new[]{part.GetComponent<Renderer>()}).ToArray();
            }
            group.SetLODs(lods);group.RecalculateBounds();EditorUtility.SetDirty(group);
            // Closed-door collision is aligned to the leaf; original body colliders and the
            // already checked low entrance platform remain intact.
            AddBox(root.transform,t,"Дверь · коллизия",0,1.28f,-1.35f,new Vector3(1.72f,2.24f,.10f));
            if(t.shop) foreach(int side in new[]{-1,1})
                AddBox(root.transform,t,"Стойка навеса · коллизия",side*1.56f,1.39f,.12f,new Vector3(.06f,2.46f,.06f));
            var view=new GameObject("Opening QA · stage05 "+t.key).transform;view.SetParent(plan.building.transform,true);
            view.position=t.P(2.8f,1.65f,4.3f);view.rotation=Quaternion.LookRotation(t.P(0,1.7f,-1.3f)-view.position);
            var marker=new GameObject("Не обмер: форма по фото; сохранена прежняя приблизительная привязка");marker.tag="EditorOnly";marker.transform.SetParent(root.transform,false);
            var survey=marker.AddComponent<PhotoSurveyMarker>();
            survey.category="Входная группа №"+t.address;
            survey.observation=t.description;
            survey.uncertainty=t.shop?"Door alignment remains the existing approximate bay; published map entrance is 1.32m laterally offset. No exact storefront logo/roller shutter has been invented.":"The address and architectural family are confirmed. This model bay is approximate; exact measured width, height and doorway number are unavailable.";
            survey.sourceUrls=t.sources;survey.geometryProvisional=true;
            survey.studyBounds=new Bounds(t.P(0,2,-1),new Vector3(3,4.6f,5.6f));
            bool unchanged=plan.building.transform.position==originalPosition&&plan.building.transform.rotation==originalRotation&&plan.building.transform.localScale==originalScale;
            if(!unchanged)throw new InvalidOperationException("A building pose changed during its bounded entrance edit.");
            changes.Add(new Change {target=plan.building.name+"/"+t.marker,observation=t.description,uncertainty=survey.uncertainty,
                sources=t.sources,removedTemplateTriangles=plan.removed,entrance=t.entrance,doorHeight=2.10f,
                portalTopBefore=4.54f,canopyTop=t.shop?2.86f:2.96f,buildingPosePreserved=true});
        }
        Physics.SyncTransforms();
        Directory.CreateDirectory("street_qa");
        File.WriteAllText("street_qa/photo_entrances_stage05.json",JsonUtility.ToJson(new Report {
            scene="Assets/Scenes/Street.unity",status="Applied; root orchestrator must run geometry/runtime/visual checks before delivery.",
            scope="Two photo-supported entrance architecture replacements in all three LODs. Saved threshold surfaces and all building root poses retained. Dimensions and precise photo-to-bay correspondence are approximate.",changes=changes.ToArray()},true));
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("PHOTO_ENTRANCES_STAGE05 bays="+changes.Count+" removedTemplateTriangles="+plans.Sum(p=>p.removed.Sum())+" building poses unchanged");
    }

    private static Plan Prepare(Target t)
    {
        // Address + inspected Access point resolve the saved bay, not a map-data lookup.
        var building=UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(b=>b.address==t.address&&
            b.GetComponentsInChildren<Transform>().Any(a=>a.name==t.marker&&Vector3.Distance(a.position,t.entrance)<.03f));
        var access=building.GetComponentsInChildren<Transform>().Single(a=>a.name==t.marker);
        if(Vector3.Dot(access.forward,t.Forward)<.999f)throw new InvalidOperationException("Unexpected entrance direction "+t.key);
        var renderers=building.GetComponent<LODGroup>().GetLODs().Select(l=>l.renderers.Single()).ToArray();
        var result=new Plan {target=t,building=building,renderers=renderers,meshes=new Mesh[3],removed=new int[3]};
        for(int lod=0;lod<3;lod++)
        {
            var filter=renderers[lod].GetComponent<MeshFilter>();
            var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);mesh.name=t.key+"_FacadeCut_LOD"+lod;
            var world=mesh.vertices.Select(filter.transform.TransformPoint).ToArray();
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var source=mesh.GetTriangles(sub);var keep=new List<int>(source.Length);
                for(int i=0;i<source.Length;i+=3)
                {
                    bool selected=Selection(world[source[i]],t)&&Selection(world[source[i+1]],t)&&Selection(world[source[i+2]],t);
                    // Keep the native masonry row above the 4.20 m arch opening.
                    selected&=!(MasonryRow(world[source[i]],t)&&MasonryRow(world[source[i+1]],t)&&MasonryRow(world[source[i+2]],t));
                    if(selected)result.removed[lod]++;
                    else{keep.Add(source[i]);keep.Add(source[i+1]);keep.Add(source[i+2]);}
                }
                mesh.SetTriangles(keep,sub,false);
            }
            mesh.RecalculateBounds();
            if(result.removed[lod]<80)throw new InvalidOperationException("No complete old portal found in "+t.key+" LOD"+lod+": "+result.removed[lod]);
            result.meshes[lod]=mesh;
        }
        return result;
    }

    private static bool Selection(Vector3 p,Target t)
    {
        Vector3 d=p-t.entrance;float along=Vector3.Dot(d,t.Forward),across=Mathf.Abs(Vector3.Dot(d,t.Right));
        // Bounds match the archived *saved geometry* doorway's complete bay faces.
        // Existing landings are separate saved objects and cannot be selected here.
        return across<=2.751f&&p.y>=-.002f&&p.y<=4.551f&&along>=-1.967f&&along<=-.924f;
    }

    private static Material CloneMaterial(string sourceName,string name,Color color,float metallic)
    {
        string path=Root+"/Materials/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material)
        {
            var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Buildings/Materials/"+sourceName+".mat");
            if(!source)throw new InvalidOperationException("Missing existing PBR material "+sourceName);
            material=new Material(source){name=name};AssetDatabase.CreateAsset(material,path);
        }
        material.SetColor("_Color",color);material.SetFloat("_Colorize",1);material.SetFloat("_Metallic",metallic);
        material.SetFloat("_Weathering",.12f);material.SetFloat("_BumpStrength",.16f);material.SetFloat("_RoughnessScale",.70f);
        material.SetFloat("_RoughnessBias",.17f);material.enableInstancing=true;EditorUtility.SetDirty(material);return material;
    }
    private static void AddBox(Transform root,Target t,string name,float x,float y,float z,Vector3 size)
    {
        var go=new GameObject(name);go.transform.SetParent(root,true);go.transform.position=t.P(x,y,z);go.transform.rotation=Quaternion.LookRotation(t.Forward);
        go.AddComponent<BoxCollider>().size=size;
    }
    private static void Box(EnvironmentMesh m,Target t,Material mat,float x,float y,float z,float sx,float sy,float sz)
        =>m.Box(mat,t.P(x,y,z),new Vector3(sx,sy,sz),Quaternion.LookRotation(t.Forward));
    private static void Rect(EnvironmentMesh m,Target t,Material mat,float left,float bottom,float right,float top,float z)
    {
        if(right-left<.0001f||top-bottom<.0001f)return;
        m.Face(mat,t.Forward,t.P(left,bottom,z),t.P(right,bottom,z),t.P(right,top,z),t.P(left,top,z));
    }

    private static void BuildFacade(EnvironmentMesh m,Target t,Material cream,Material plinth,Material green,Material grey,Material metal,int lod)
    {
        const float wall=-1.34f,bottom=.16f,top=2.38f,half=.86f;
        // Fill the former 5.5 x 4.2m arch with the existing facade masonry, keeping a
        // real rectangular opening and inward jamb faces instead of an opaque door decal.
        Material lower=t.shop?green:plinth;
        Rect(m,t,lower,-2.75f,0,-half,1.26f,wall);Rect(m,t,lower,half,0,2.75f,1.26f,wall);
        Rect(m,t,cream,-2.75f,1.26f,-half,4.2f,wall);Rect(m,t,cream,half,1.26f,2.75f,4.2f,wall);
        Rect(m,t,cream,-half,top,half,4.2f,wall);
        Rect(m,t,lower,-half,0,half,bottom,wall);
        float panelTop=t.shop?2.70f:2.58f;
        Material cladding=t.shop?green:grey;
        Rect(m,t,cladding,-1.50f,bottom,-half,panelTop,wall+.016f);
        Rect(m,t,cladding,half,bottom,1.50f,panelTop,wall+.016f);
        Rect(m,t,cladding,-half,top,half,panelTop,wall+.016f);
        m.Face(grey,t.Right,t.P(-half,bottom,wall),t.P(-half,bottom,wall-.18f),t.P(-half,top,wall-.18f),t.P(-half,top,wall));
        m.Face(grey,-t.Right,t.P(half,bottom,wall-.18f),t.P(half,bottom,wall),t.P(half,top,wall),t.P(half,top,wall-.18f));
        m.Face(grey,Vector3.down,t.P(-half,top,wall),t.P(-half,top,wall-.18f),t.P(half,top,wall-.18f),t.P(half,top,wall));
        // A normal-height grey metal leaf with an unequal secondary leaf and narrow frame.
        Box(m,t,grey,0,1.27f,wall-.08f,1.60f,2.10f,.075f);
        foreach(int side in new[]{-1,1})Box(m,t,metal,side*.83f,1.27f,wall-.015f,.06f,2.22f,.12f);
        foreach(float y in new[]{.19f,2.35f})Box(m,t,metal,0,y,wall-.015f,1.72f,.06f,.12f);
        Box(m,t,metal,.28f,1.27f,wall-.035f,.025f,2.10f,.014f);
        Box(m,t,plinth,0,.175f,wall+.015f,1.80f,.03f,.30f);
        if(lod<2)
        {
            Box(m,t,metal,.12f,1.22f,wall+.034f,.025f,.29f,.032f);
            foreach(float y in new[]{.42f,2.10f})Box(m,t,metal,0,y,wall-.036f,1.46f,.012f,.012f);
            Box(m,t,metal,1.10f,1.38f,wall+.045f,.10f,.22f,.035f);
            Box(m,t,grey,1.10f,1.40f,wall+.066f,.045f,.045f,.008f);
            if(lod==0) foreach(float y in new[]{.62f,1.79f})Box(m,t,metal,-.78f,y,wall+.025f,.045f,.095f,.05f);
        }
        if(t.shop)
        {
            // Low mono-pitched canopy, horizontal green panel courses and narrow posts.
            m.Face(green,Vector3.up,t.P(-2.60f,2.86f,wall+.04f),t.P(2.60f,2.86f,wall+.04f),t.P(2.60f,2.67f,.26f),t.P(-2.60f,2.67f,.26f));
            m.Face(green,Vector3.down,t.P(-2.60f,2.82f,wall+.04f),t.P(-2.60f,2.63f,.26f),t.P(2.60f,2.63f,.26f),t.P(2.60f,2.82f,wall+.04f));
            foreach(int side in new[]{-1,1})m.Face(green,t.Right*side,t.P(side*2.60f,2.86f,wall+.04f),t.P(side*2.60f,2.67f,.26f),t.P(side*2.60f,2.63f,.26f),t.P(side*2.60f,2.82f,wall+.04f));
            Box(m,t,green,0,2.65f,.27f,5.25f,.08f,.10f);
            foreach(int side in new[]{-1,1})Box(m,t,metal,side*1.56f,1.40f,.12f,.055f,2.48f,.055f);
            if(lod<2)for(float y=.60f;y<2.60f;y+=.42f)
                foreach(int side in new[]{-1,1})Box(m,t,metal,side*1.16f,y,wall+.018f,.62f,.009f,.008f);
        }
        else
        {
            // The photographed central entry has a wide horizontal top with short
            // dropped green end consoles. It has no 4.5m freestanding arched portal.
            Box(m,t,green,0,2.83f,wall+.38f,5.20f,.26f,.86f);
            foreach(int side in new[]{-1,1})
            {
                Box(m,t,green,side*2.19f,2.56f,wall+.22f,.25f,.43f,.50f);
                Box(m,t,green,side*1.84f,2.36f,wall+.22f,.95f,.13f,.50f);
            }
            if(lod<2)Box(m,t,grey,0,2.57f,wall+.13f,.34f,.08f,.12f);
        }
    }
}
