using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// A local edit of saved scene assets. Never invokes the archived street generator.
public static class PhotoStage01
{
    const string Root = "Assets/PhotoReconstruction/Stage01";
    const string Output = "street_qa/photo_stage01_editor";

    [MenuItem("Street/Photo reconstruction/Apply stage 01 and build saved scene")]
    public static void ApplyAndBuild()
    {
        Directory.CreateDirectory(Output);
        for(int i=0;i<EditorSceneManager.sceneCount;i++)
            if(EditorSceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save the current scene before applying a photo stage.");
        EditorSceneManager.OpenScene("Assets/Scenes/Street.unity");
        var buildings = UnityEngine.Object.FindObjectsByType<StreetBuilding>();
        var poses = buildings.ToDictionary(b => b, b => (b.transform.position,b.transform.rotation,b.transform.localScale));
        Directory.CreateDirectory(Root+"/Materials"); Directory.CreateDirectory(Root+"/Meshes");
        AssetDatabase.Refresh();
        Capture("before_paving",new Vector3(40.64f,2.4f,-130.8f),Quaternion.Euler(15.14f,148.39f,0));
        Capture("before_entrance11",new Vector3(119.8f,1.65f,8.8f),Quaternion.LookRotation(new Vector3(127.4f,1.1f,5.87f)-new Vector3(119.8f,1.65f,8.8f)));
        PhotoEntranceStage01.Apply();
        ApplyPaving();
        MarkUncertainties();
        foreach(var b in buildings)
            if(b.transform.position!=poses[b].Item1||b.transform.rotation!=poses[b].Item2||b.transform.localScale!=poses[b].Item3)
                throw new InvalidOperationException("A building moved: "+b.name);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/Scenes/Street.unity");
        AssetDatabase.SaveAssets();
        Capture("after_paving",new Vector3(40.64f,2.4f,-130.8f),Quaternion.Euler(15.14f,148.39f,0));
        Capture("after_entrance11",new Vector3(119.8f,1.65f,8.8f),Quaternion.LookRotation(new Vector3(127.4f,1.1f,5.87f)-new Vector3(119.8f,1.65f,8.8f)));
        PhotoSportAssetBuilder.Generate();
        Preview(PhotoSportAssetBuilder.RampPrefabPath,"sport_ramp");
        Preview(PhotoSportAssetBuilder.WorkoutPrefabPath,"sport_workout");
        StreetPavingAudit.Check();
        var audit=File.ReadAllText("street_qa/paving_editor_audit.json");
        if(audit.Contains("\"category\""))throw new InvalidOperationException("Pavement audit has findings");
        Debug.Log("PHOTO_STAGE01_READY buildingsUnmoved="+buildings.Length+" OSM_READS=0; saved-scene edit only");
        StreetBuilder.BuildGeneratedScene();
    }

    [MenuItem("Street/Photo reconstruction/Finish selected path edges and build saved scene")]
    public static void FinishEdgesAndBuild()
    {
        const string folder="street_qa/photo_stage02_editor";
        Directory.CreateDirectory(folder);
        for(int i=0;i<EditorSceneManager.sceneCount;i++)
            if(EditorSceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save the open scene before applying a photo stage.");
        EditorSceneManager.OpenScene("Assets/Scenes/Street.unity");
        var buildings=UnityEngine.Object.FindObjectsByType<StreetBuilding>();
        var poses=buildings.ToDictionary(b=>b,b=>(b.transform.position,b.transform.rotation,b.transform.localScale));
        var position=new Vector3(119.8f,1.65f,8.8f);
        var rotation=Quaternion.LookRotation(new Vector3(127.4f,1.1f,5.87f)-position);
        Capture("before_path_edges",position,rotation,folder);
        ApplyPaving();PhotoAccessKerbStage02.Apply();MarkUncertainties();
        foreach(var b in buildings)
            if(b.transform.position!=poses[b].Item1||b.transform.rotation!=poses[b].Item2||b.transform.localScale!=poses[b].Item3)
                throw new InvalidOperationException("A building moved: "+b.name);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/Scenes/Street.unity");AssetDatabase.SaveAssets();
        Capture("after_path_edges",position,rotation,folder);
        Capture("after_south_paving",new Vector3(40.64f,2.4f,-130.8f),Quaternion.Euler(15.14f,148.39f,0),folder);
        StreetPavingAudit.Check();
        if(File.ReadAllText("street_qa/paving_editor_audit.json").Contains("\"category\""))throw new InvalidOperationException("Pavement audit has findings");
        Debug.Log("PHOTO_STAGE02_READY buildingsUnmoved="+buildings.Length+" OSM_READS=0");
        StreetBuilder.BuildGeneratedScene();
    }

    static void ApplyPaving()
    {
        var original=AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Pilot/Materials/ReferenceRedPaving.mat");
        var paving=CloneMaterial(original,"PhotoRedRunningBond");
        paving.SetFloat("_PaverPattern",1); paving.SetFloat("_PaverGreyFraction",0);
        paving.SetFloat("_PaverJointMeters",.0035f); paving.SetFloat("_Weathering",.12f);
        paving.SetColor("_Color",new Color(.96f,.93f,.91f));
        var originalKerb=AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Pilot/Materials/ReferenceKerbstone.mat");
        var kerb=CloneMaterial(originalKerb,"PhotoWhitewashedKerb");
        kerb.SetFloat("_Colorize",.65f);kerb.SetColor("_Color",new Color(.70f,.70f,.66f));
        kerb.SetFloat("_BumpStrength",.24f);kerb.SetFloat("_Weathering",.16f);
        int changed=0;
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if(!filter.sharedMesh)continue;
            string baseName=filter.sharedMesh.name.Replace("PhotoStage01_","");
            if(baseName!="ReferenceWalkways"&&baseName!="ReferenceKerbs"&&baseName!="ResidentialAccessSurfaces")continue;
            var renderer=filter.GetComponent<MeshRenderer>();if(!renderer)continue;
            var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh); var vertices=mesh.vertices;
            var mats=renderer.sharedMaterials.ToList();var extra=new List<(Material material,List<int> indices)>();
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                Material source=mats[sub];
                bool isPaving=source&&source.HasProperty("_SurfaceClass")&&source.GetFloat("_SurfaceClass")>5.5f;
                bool isKerb=source==originalKerb || source&&source.name=="VergeBlueEdge";
                // Already split material batches are retained on repeat runs.
                if(source==paving||source==kerb)continue;
                if(!isPaving&&!isKerb)continue;
                var kept=new List<int>();var selected=new List<int>();var indices=mesh.GetTriangles(sub);
                for(int i=0;i<indices.Length;i+=3)
                {
                    var center=filter.transform.TransformPoint((vertices[indices[i]]+vertices[indices[i+1]]+vertices[indices[i+2]])/3);
                    bool south=center.z>=-245&&center.z<=-80;
                    bool entrance11=center.x>=114f&&center.x<=129f&&center.z>=-3f&&center.z<=11f;
                    var target=south||entrance11?selected:kept;
                    target.Add(indices[i]);target.Add(indices[i+1]);target.Add(indices[i+2]);
                }
                mesh.SetTriangles(kept,sub);
                if(selected.Count>0){extra.Add((isPaving?paving:kerb,selected));changed+=selected.Count/3;}
            }
            if(extra.Count==0){UnityEngine.Object.DestroyImmediate(mesh);continue;}
            int count=mesh.subMeshCount;mesh.subMeshCount=count+extra.Count;
            for(int i=0;i<extra.Count;i++){mesh.SetTriangles(extra[i].indices,count+i);mats.Add(extra[i].material);}
            mesh.name="PhotoStage01_"+baseName;
            string path=Root+"/Meshes/"+mesh.name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}
            else AssetDatabase.CreateAsset(mesh,path);
            filter.sharedMesh=mesh;renderer.sharedMaterials=mats.ToArray();
            var collider=filter.GetComponent<MeshCollider>();if(collider)collider.sharedMesh=mesh;
        }
        EditorUtility.SetDirty(paving);EditorUtility.SetDirty(kerb);
        Debug.Log("PHOTO_PAVING trianglesRetained="+changed+" positions unchanged, south reference sector and bounded entrance 11 only");
    }

    static Material CloneMaterial(Material source,string name)
    {
        if(!source)throw new InvalidOperationException("Missing source material "+name);
        string path=Root+"/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(source);AssetDatabase.CreateAsset(material,path);}else EditorUtility.CopySerialized(source,material);
        material.name=name;material.enableInstancing=true;return material;
    }

    static void MarkUncertainties()
    {
        var previous=GameObject.Find("Фотосверка · неподтверждённые участки");
        if(previous)UnityEngine.Object.DestroyImmediate(previous);
        var root=new GameObject("Фотосверка · неподтверждённые участки");root.tag="EditorOnly";
        void Mark(string name,string category,Bounds bounds,string observation,string uncertainty,params string[] sources)
        {
            var go=new GameObject(name);go.tag="EditorOnly";go.transform.SetParent(root.transform);
            go.transform.position=bounds.center;var tag=go.AddComponent<PhotoSurveyMarker>();
            tag.category=category;tag.studyBounds=bounds;tag.sourceUrls=sources;
            tag.observation=observation;tag.uncertainty=uncertainty;
        }
        foreach(var site in UnityEngine.Object.FindObjectsByType<PlaygroundSite>())
            Mark(site.name+" · комплектация временная","playground",site.worldBounds,"Существующая площадка сохранена.","Набор оборудования и положение отдельных элементов не подтверждены адресными фото. Библиотека Строителей 57 не размещена здесь автоматически.");
        Mark("Центральный двор 5/7 · высоты не измерены","terrain",new Bounds(new Vector3(38,0,45),new Vector3(100,2,130)),"Найдены аэрофото и сведения о подземной парковке.","Нет внешних высотных отметок; существующая плоская земля временная. Это область исследования, не проектная граница поднятого двора.","https://gelio.livejournal.com/248828.html","https://rutorgi.com/lot/4261740");
        Mark("Южный участок · рисунок покрытия по фото №15","paving",new Bounds(new Vector3(40,0,-162.5f),new Vector3(150,1,165)),"На летнем фото подтверждены красная перевязка и светлые бордюры.","Применён тип покрытия. Существующая ширина и план дорожек пока не являются результатом обмера.","https://yandex.ru/maps/org/159257372149/");
        foreach(var building in UnityEngine.Object.FindObjectsByType<StreetBuilding>())
            Mark(building.name+" · инвентаризация входов","entrances",new Bounds(building.transform.position,new Vector3(8,1,8)),"Положения зданий сохранены.","Типовые четыре ступени в остальных секциях не считаются подтверждёнными. Первый исправленный низкий узел — №11; точный фасадный рисунок требует дальнейшего сопоставления.");
    }

    static void Preview(string path,string name)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position=new Vector3(-5000,0,-5000);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=instance.transform.position+Vector3.down*.15f;floor.transform.localScale=new Vector3(25,.2f,25);
        Capture(name,instance.transform.position+new Vector3(7,4,-8),Quaternion.LookRotation(instance.transform.position+Vector3.up-new Vector3(-4993,4,-5008)));
        UnityEngine.Object.DestroyImmediate(instance);UnityEngine.Object.DestroyImmediate(floor);
    }

    static void Capture(string name,Vector3 position,Quaternion rotation,string folder=null)
    {
        var go=new GameObject("Temporary photo review camera");go.hideFlags=HideFlags.HideAndDontSave;
        var camera=go.AddComponent<Camera>();camera.transform.SetPositionAndRotation(position,rotation);
        camera.farClipPlane=1200;camera.fieldOfView=60;camera.clearFlags=CameraClearFlags.Skybox;
        var rt=new RenderTexture(1600,900,24);camera.targetTexture=rt;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=rt;
        var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,900),0,0);texture.Apply();
        File.WriteAllBytes((folder??Output)+"/"+name+".png",texture.EncodeToPNG());RenderTexture.active=previous;
        UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);
    }
}
