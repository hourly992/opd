using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Existing pole centres are recovered from the saved low rings, not from map data.
public static class PhotoStreetLampStage03
{
    const string Root="Assets/PhotoReconstruction/Stage03Lamps";
    static Material steel,lens,bolts;
    public static void UpgradeCourtyardGlobes()
    {
        var network=UnityEngine.Object.FindAnyObjectByType<StreetLampNetwork>();if(!network)throw new InvalidOperationException("Apply street lighting first.");
        steel=Own("CourtyardCastMetal","Assets/StreetGenerated/LampSteel.mat",new Color(.075f,.095f,.085f),.5f,.24f);
        lens=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/WarmLedLens.mat");
        bolts=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/PoleFasteners.mat");
        var prefab=Prefab("CourtyardGlobe3m9",3.9f,false);var lights=network.lamps.Where(l=>l).ToList();
        var previous=network.GetComponentsInChildren<CapsuleCollider>().Where(c=>c.name=="CourtyardLamp5m").ToArray();
        foreach(var pole in previous)
        {
            var p=pole.transform.position;var q=pole.transform.rotation;
            lights.RemoveAll(l=>l&&l.transform.IsChildOf(pole.transform));UnityEngine.Object.DestroyImmediate(pole.gameObject);
            Place(prefab,p,q,network.transform,lights);
        }
        network.lamps=lights.ToArray();AssetDatabase.SaveAssets();
        Debug.Log("STAGE04_LAMP_GLOBES replaced="+previous.Length+" appearance based on summer courtyard7 photos; additional pole positions remain provisional");
    }
    public static void Apply()
    {
        if(UnityEngine.Object.FindAnyObjectByType<StreetLampNetwork>())return;
        foreach(string folder in new[]{"Meshes","Materials","Prefabs"})Directory.CreateDirectory(Root+"/"+folder);
        AssetDatabase.Refresh();
        steel=Own("GalvanizedPole","Assets/StreetGenerated/LampSteel.mat",new Color(.47f,.50f,.51f),.62f,.32f);
        lens=Own("WarmLedLens","Assets/StreetGenerated/Pilot/Materials/LampDiffuser.mat",new Color(.93f,.89f,.74f),.05f,.22f);
        lens.SetColor("_EmissionColor",new Color(.16f,.12f,.065f));
        bolts=Own("PoleFasteners","Assets/StreetGenerated/LampSteel.mat",new Color(.3f,.32f,.33f),.72f,.40f);
        var root=new GameObject("Фонари · сохранённые опоры и дополнение дворов stage03");
        var network=root.AddComponent<StreetLampNetwork>();var lights=new List<Light>();var positions=new List<Vector3>();
        var street=Prefab("StreetLampTwin8m",8.13f,true);
        var court=Prefab("CourtyardLamp5m",5.02f,false);
        var old=UnityEngine.Object.FindObjectsByType<MeshFilter>().SingleOrDefault(f=>f.sharedMesh&&f.sharedMesh.name=="Map_LampSteel");
        if(old)
        {
            var points=old.sharedMesh.vertices.Select(old.transform.TransformPoint).ToArray();
            foreach(var p in points.Where(p=>p.y>.12f&&p.y<.24f))
                if(positions.All(q=>new Vector2(q.x-p.x,q.z-p.z).sqrMagnitude>.25f))
                {
                    var ring=points.Where(q=>q.y>.12f&&q.y<.24f&&new Vector2(q.x-p.x,q.z-p.z).sqrMagnitude<.1f).ToArray();
                    var centre=ring.Aggregate(Vector3.zero,(a,b)=>a+b)/ring.Length;centre.y=0;
                    var head=points.Where(q=>q.y>7.9f&&(q-centre).sqrMagnitude<72).OrderByDescending(q=>new Vector2(q.x-centre.x,q.z-centre.z).sqrMagnitude).FirstOrDefault();
                    var axis=head-centre;axis.y=0;if(axis.sqrMagnitude<.1f)axis=Vector3.forward;
                    Place(street,centre,Quaternion.LookRotation(axis),root.transform,lights);positions.Add(centre);
                }
            old.GetComponent<MeshRenderer>().enabled=false;
            RemoveOldHeadBoxes(positions);
        }
        network.retainedStreetPositions=positions.Count;
        foreach(var f in UnityEngine.Object.FindObjectsByType<MeshFilter>().Where(f=>f.sharedMesh&&f.sharedMesh.name=="CourtyardLamp"))positions.Add(f.transform.position);
        var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        Physics.SyncTransforms();
        foreach(var route in access.routes.Where(r=>r.points!=null&&r.points.Length>=4).OrderBy(r=>r.address,StringComparer.Ordinal))
        {
            int i=route.points.Length/2;var point=route.points[i];
            if(point.x< -110||point.x>230||point.z< -255||point.z>175)continue;
            var direction=route.points[Mathf.Min(i+1,route.points.Length-1)]-route.points[Mathf.Max(0,i-1)];direction.y=0;
            if(direction.sqrMagnitude<.01f)continue;direction.Normalize();
            var right=new Vector3(direction.z,0,-direction.x);
            foreach(int sign in new[]{1,-1})
            {
                var p=point+right*(sign*1.17f);
                if(positions.Any(q=>new Vector2(q.x-p.x,q.z-p.z).sqrMagnitude<18*18))continue;
                var hit=Physics.RaycastAll(p+Vector3.up*3,Vector3.down,5).Where(h=>h.normal.y>.85f&&!h.collider.isTrigger).OrderByDescending(h=>h.point.y).FirstOrDefault();
                if(!hit.collider||hit.point.y>.32f||hit.point.y<-.05f)continue;
                if(IsCarriageway(hit))continue;
                p.y=hit.point.y;
                if(Physics.CheckCapsule(p+Vector3.up*.45f,p+Vector3.up*2,.18f,~0,QueryTriggerInteraction.Ignore))continue;
                if(UnityEngine.Object.FindObjectsByType<StreetBuilding>().Any(b=>b.GetComponents<BoxCollider>().Any(c=>Vector3.Distance(c.ClosestPoint(p+Vector3.up),p+Vector3.up)<1.6f)))continue;
                Place(court,p,Quaternion.LookRotation(-right*sign),root.transform,lights);positions.Add(p);network.addedCourtyardPositions++;
                break;
            }
            if(network.addedCourtyardPositions>=22)break;
        }
        network.lamps=lights.ToArray();
        var evidence=new GameObject("Editor evidence · additional courtyard lamp positions are a design provision");evidence.tag="EditorOnly";evidence.transform.SetParent(root.transform,false);
        var marker=evidence.AddComponent<PhotoSurveyMarker>();marker.category="lighting";
        marker.observation="Сохранены точки прежних уличных опор. Двусторонние светильники и масштаб сопоставлены с уличными фотографиями.";
        marker.uncertainty="Добавленные дворовые опоры — проектное дополнение вдоль существующих подходов, а не подтверждённая съёмкой расстановка. Модели оценочные.";
        marker.sourceUrls=new[]{"https://yandex.ru/maps/org/91706029280/"};
        marker.studyBounds=new Bounds(new Vector3(60,0,-40),new Vector3(340,10,440));
        AssetDatabase.SaveAssets();
        Debug.Log("STAGE03_LAMPS streetPositionsRetained="+network.retainedStreetPositions+" addedCourtyard="+network.addedCourtyardPositions+" lamps="+lights.Count+" daytimeLocalLights=disabled shadows=none");
    }
    static bool IsCarriageway(RaycastHit hit)
    {
        var mc=hit.collider as MeshCollider;var renderer=hit.collider.GetComponent<MeshRenderer>();if(!mc||!mc.sharedMesh||!renderer)return false;
        int end=0;
        for(int sub=0;sub<mc.sharedMesh.subMeshCount;sub++)
        {
            end+=(int)mc.sharedMesh.GetIndexCount(sub)/3;if(hit.triangleIndex>=end)continue;
            var material=renderer.sharedMaterials[sub];float kind=material&&material.HasProperty("_SurfaceClass")?material.GetFloat("_SurfaceClass"):0;
            return kind>3.5f&&kind<4.5f;
        }
        return false;
    }
    static Material Own(string name,string path,Color colour,float metallic,float smooth)
    {
        var source=AssetDatabase.LoadAssetAtPath<Material>(path);if(!source)throw new InvalidOperationException("Missing lamp PBR source: "+path);
        var material=new Material(source){name=name,enableInstancing=true};material.SetColor("_Color",colour);material.SetFloat("_Metallic",metallic);material.SetFloat("_Smoothness",smooth);
        string target=Root+"/Materials/"+name+".mat";var existing=AssetDatabase.LoadAssetAtPath<Material>(target);
        if(existing){EditorUtility.CopySerialized(material,existing);UnityEngine.Object.DestroyImmediate(material);return existing;}
        AssetDatabase.CreateAsset(material,target);return material;
    }
    static GameObject Prefab(string name,float height,bool twin)
    {
        var root=new GameObject(name);var group=root.AddComponent<LODGroup>();var lods=new LOD[3];
        for(int lod=0;lod<3;lod++)
        {
            var mesh=new EnvironmentMesh();int sides=lod==0?16:lod==1?10:6;
            mesh.Cylinder(steel,new Vector3(0,.11f,0),new Vector3(0,height-.42f,0),.09f,.045f,sides);
            mesh.RoundedBox(steel,new Vector3(0,.075f,0),new Vector3(.36f,.15f,.36f),.035f);
            if(lod==0)foreach(float x in new[]{-.12f,.12f})foreach(float z in new[]{-.12f,.12f})
                mesh.Cylinder(bolts,new Vector3(x,.15f,z),new Vector3(x,.19f,z),.016f,.016f,6);
            mesh.RoundedBox(bolts,new Vector3(0,.73f,-.086f),new Vector3(.08f,.29f,.018f),.009f);
            if(twin)foreach(int sign in new[]{-1,1})
            {
                var points=new[]{new Vector3(0,height-.7f,0),new Vector3(0,height-.28f,sign*.32f),new Vector3(0,height-.08f,sign*.86f),new Vector3(0,height,sign*1.35f)};
                for(int i=1;i<points.Length;i++)mesh.Cylinder(steel,points[i-1],points[i],.04f-i*.003f,.036f-i*.003f,sides);
                mesh.RoundedBox(steel,new Vector3(0,height,sign*1.44f),new Vector3(.31f,.12f,.70f),.10f);
                mesh.RoundedBox(lens,new Vector3(0,height-.066f,sign*1.47f),new Vector3(.255f,.018f,.54f),.06f);
            }
            else
            {
                mesh.Cylinder(steel,new Vector3(0,height-.65f,0),new Vector3(0,height-.19f,0),.055f,.05f,sides);
                foreach(int sign in new[]{-1,1})
                {
                    Vector3 previous=new Vector3(0,height-.85f,0);
                    int pieces=lod==0?12:6;
                    for(int part=1;part<=pieces;part++)
                    {
                        float t=(float)part/pieces;
                        var p=new Vector3(sign*(.47f*Mathf.Sin(t*Mathf.PI*.5f)),height-.85f+.43f*t-.14f*Mathf.Sin(t*Mathf.PI),0);
                        mesh.Cylinder(steel,previous,p,.018f,.018f,lod==0?8:6);previous=p;
                    }
                    mesh.Cylinder(steel,previous,previous+Vector3.up*.10f,.065f,.045f,sides);
                    mesh.Ellipsoid(lens,new Vector3(sign*.47f,height-.18f,0),Vector3.one*.205f,lod==0?20:lod==1?12:8,lod==0?10:6);
                }
                mesh.Ellipsoid(lens,new Vector3(0,height+.04f,0),Vector3.one*.22f,lod==0?20:lod==1?12:8,lod==0?10:6);
                mesh.Cylinder(steel,new Vector3(0,.14f,0),new Vector3(0,.62f,0),.13f,.085f,sides);
            }
            var go=mesh.Create(name+"_LOD"+lod,"LOD"+lod,Root);go.transform.SetParent(root.transform,false);
            lods[lod]=new LOD(lod==0?.08f:lod==1?.028f:.009f,new[]{go.GetComponent<MeshRenderer>()});
        }
        group.SetLODs(lods);group.RecalculateBounds();var collider=root.AddComponent<CapsuleCollider>();collider.radius=.105f;collider.height=height-.4f;collider.center=Vector3.up*(height*.5f);
        return EnvironmentMesh.SavePrefab(root,name,Root);
    }
    static void Place(GameObject prefab,Vector3 point,Quaternion rotation,Transform parent,List<Light> lights)
    {
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);instance.transform.SetPositionAndRotation(point,rotation);
        bool twin=prefab.name.Contains("Twin");bool globe=prefab.name.Contains("Globe");float h=twin?8.13f:globe?3.9f:5.02f;
        foreach(int sign in twin?new[]{-1,1}:new[]{1})
        {
            var go=new GameObject("Тёплый свет · включается в сумерках");go.transform.SetParent(instance.transform,false);go.transform.localPosition=new Vector3(0,h-.1f,globe?0:sign*1.45f);
            go.transform.localRotation=Quaternion.Euler(90,0,0);var light=go.AddComponent<Light>();light.type=LightType.Spot;light.spotAngle=112;light.range=twin?18:13;
            light.intensity=twin?2.6f:2.0f;light.color=new Color(1,.83f,.61f);light.shadows=LightShadows.None;light.renderMode=LightRenderMode.ForcePixel;light.enabled=false;lights.Add(light);
        }
    }
    static void RemoveOldHeadBoxes(List<Vector3> poles)
    {
        var old=UnityEngine.Object.FindObjectsByType<MeshFilter>().FirstOrDefault(f=>f.sharedMesh&&f.sharedMesh.name=="Map_White");if(!old)return;
        var mesh=UnityEngine.Object.Instantiate(old.sharedMesh);mesh.name="Stage03RetainedWhite";var vertices=mesh.vertices;
        for(int sub=0;sub<mesh.subMeshCount;sub++)
        {
            var keep=new List<int>();var indices=mesh.GetTriangles(sub);
            for(int i=0;i<indices.Length;i+=3)
            {
                bool lamp=true;for(int j=0;j<3;j++){var p=old.transform.TransformPoint(vertices[indices[i+j]]);lamp&=p.y>7.9f&&p.y<8.3f&&poles.Any(q=>new Vector2(q.x-p.x,q.z-p.z).sqrMagnitude<5);}
                if(!lamp){keep.Add(indices[i]);keep.Add(indices[i+1]);keep.Add(indices[i+2]);}
            }
            mesh.SetTriangles(keep,sub);
        }
        AssetDatabase.CreateAsset(mesh,Root+"/Meshes/Stage03RetainedWhite.asset");old.sharedMesh=mesh;
    }
}
