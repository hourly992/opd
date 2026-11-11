using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Finishes exposed edges of an existing connector; never invents a new route.
public static class PhotoAccessKerbStage02
{
    const string Root="Assets/PhotoReconstruction/Stage02";
    const string Name="Photo11 · exposed path edges · provisional alignment";
    sealed class Edge
    {
        public Vector3 a,b,outward;
        public int count;
    }
    [Serializable] sealed class Report
    {
        public string note;
        public int boundaryEdges, finishedEdges, triangles;
        public float width, maxHeightChange;
        public Vector3[] checks;
    }
    static Vector2Int Key(Vector3 p)=>new Vector2Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.z*1000));
    static bool Selected(Vector3 p)=>p.x>114&&p.x<130&&p.z>-3&&p.z<11;

    public static void Apply()
    {
        var previous=GameObject.Find(Name);
        if(previous)UnityEngine.Object.DestroyImmediate(previous);
        Physics.SyncTransforms();
        var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        if(!access||access.surfaces.Length!=1)throw new InvalidOperationException("Inspect the saved access surface before finishing its edges.");
        var collider=access.surfaces[0];var source=collider.sharedMesh;
        var renderer=collider.GetComponent<MeshRenderer>();var v=source.vertices;
        var edges=new Dictionary<(Vector2Int,Vector2Int),Edge>();
        void Add(Vector3 a,Vector3 b,Vector3 third)
        {
            var ka=Key(a);var kb=Key(b);if(ka==kb)return;
            bool reverse=ka.x>kb.x||ka.x==kb.x&&ka.y>kb.y;
            var key=reverse?(kb,ka):(ka,kb);
            if(edges.TryGetValue(key,out var edge)){edge.count++;return;}
            var direction=b-a;direction.y=0;
            var outward=Vector3.Cross(Vector3.up,direction).normalized;
            if(Vector3.Dot(third-(a+b)*.5f,outward)>0)outward=-outward;
            edges.Add(key,new Edge{a=a,b=b,outward=outward,count=1});
        }
        for(int sub=0;sub<source.subMeshCount;sub++)
        {
            var material=renderer.sharedMaterials[sub];
            if(!material||!material.HasProperty("_SurfaceClass")||material.GetFloat("_SurfaceClass")<5.5f)continue;
            var indices=source.GetTriangles(sub);
            for(int t=0;t<indices.Length;t+=3)
            {
                var a=collider.transform.TransformPoint(v[indices[t]]);
                var b=collider.transform.TransformPoint(v[indices[t+1]]);
                var c=collider.transform.TransformPoint(v[indices[t+2]]);
                if(Vector3.Dot(Vector3.Cross(b-a,c-a).normalized,Vector3.up)<.85f)continue;
                Add(a,b,c);Add(b,c,a);Add(c,a,b);
            }
        }
        var candidates=edges.Values.Where(e=>e.count==1&&Selected((e.a+e.b)*.5f)).ToArray();
        var accepted=candidates.Where(e=>ExposedToGrass((e.a+e.b)*.5f+e.outward*.06f,(e.a.y+e.b.y)*.5f)).ToArray();
        if(accepted.Length<2)throw new InvalidOperationException("No verified exposed path edges at entrance11; no kerbs were placed.");
        var normals=new Dictionary<Vector2Int,List<Vector3>>();
        foreach(var e in accepted)foreach(var p in new[]{e.a,e.b})
        {
            var key=Key(p);if(!normals.ContainsKey(key))normals.Add(key,new List<Vector3>());
            if(normals[key].All(n=>Vector3.Dot(n,e.outward)<.999f))normals[key].Add(e.outward);
        }
        Vector3 Offset(Vector3 p,Vector3 outward)
        {
            var sum=normals[Key(p)].Aggregate(Vector3.zero,(a,b)=>a+b).normalized;
            float denominator=Vector3.Dot(sum,outward);
            return denominator>.55f?sum/denominator:outward;
        }
        var materialKerb=AssetDatabase.LoadAssetAtPath<Material>("Assets/PhotoReconstruction/Stage01/Materials/PhotoWhitewashedKerb.mat");
        if(!materialKerb)throw new InvalidOperationException("Photo-referenced kerb material is missing.");
        Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.Refresh();
        var mesh=new EnvironmentMesh();var checks=new List<Vector3>();float maxChange=0;
        foreach(var edge in accepted)
        {
            // A flush 80-mm garden edging, with a real side, 8-mm outer chamfer and
            // shared miter positions. No bar is built across asphalt or another path.
            var oa=Offset(edge.a,edge.outward);var ob=Offset(edge.b,edge.outward);
            Vector3 P(Vector3 p,Vector3 offset,float width,float height)=>p+offset*width+Vector3.up*height;
            var a=P(edge.a,oa,0,.001f);var b=P(edge.b,ob,0,.001f);
            var c=P(edge.b,ob,.072f,.001f);var d=P(edge.a,oa,.072f,.001f);
            var e=P(edge.a,oa,.08f,-.007f);var f=P(edge.b,ob,.08f,-.007f);
            var g=new Vector3(e.x,-.012f,e.z);var h=new Vector3(f.x,-.012f,f.z);
            mesh.Face(materialKerb,Vector3.up,a,b,c,d);
            mesh.Face(materialKerb,(Vector3.up+edge.outward).normalized,d,c,f,e);
            mesh.Face(materialKerb,edge.outward,e,f,h,g);
            mesh.Face(materialKerb,Vector3.down,g,h,new Vector3(b.x,-.012f,b.z),new Vector3(a.x,-.012f,a.z));
            mesh.Face(materialKerb,(edge.a-edge.b).normalized,a,d,e,g,new Vector3(a.x,-.012f,a.z));
            mesh.Face(materialKerb,(edge.b-edge.a).normalized,b,new Vector3(b.x,-.012f,b.z),h,f,c);
            checks.Add((a+b+c+d)*.25f);maxChange=Mathf.Max(maxChange,Mathf.Abs(a.y-b.y));
        }
        var go=mesh.Create("Photo11AccessKerb",Name,Root);go.transform.SetParent(access.transform,true);
        var savedMesh=go.GetComponent<MeshFilter>().sharedMesh;
        var newCollider=go.AddComponent<MeshCollider>();newCollider.sharedMesh=savedMesh;
        GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
        Physics.SyncTransforms();
        foreach(var p in checks)
            if(!newCollider.Raycast(new Ray(p+Vector3.up,Vector3.down),out var hit,1.1f)||hit.normal.y<.85f||Mathf.Abs(hit.point.y-p.y)>.002f)
                throw new InvalidOperationException("New kerb top has a gap or inverted face: "+p);
        var marker=new GameObject("Editor evidence · kerb profile photographed; route alignment remains provisional");
        marker.tag="EditorOnly";marker.transform.SetParent(go.transform,false);
        var evidence=marker.AddComponent<PhotoSurveyMarker>();evidence.category="kerb";
        evidence.sourceUrls=new[]{"https://yandex.ru/maps/org/91706029280/","https://yandex.ru/maps/org/226187181184/"};
        evidence.observation="Низкая светлая кромка у красной дорожки подтверждена фото №11. Трёхмерное сечение добавлено только на открытых границах существующей дорожки.";
        evidence.uncertainty="План существующего подхода сохранён как временный. Ширина 80мм и фаска8мм — оценка, не обмер.";
        evidence.studyBounds=go.GetComponent<Renderer>().bounds;
        File.WriteAllText("street_qa/photo_access_kerb_stage02.json",JsonUtility.ToJson(new Report{
            note="Existing connector unchanged. Only grass-facing boundary edges finished. No kerb crosses existing asphalt/pavement; dimensions are inferred, alignment provisional.",
            boundaryEdges=candidates.Length,finishedEdges=accepted.Length,triangles=savedMesh.triangles.Length/3,
            width=.08f,maxHeightChange=maxChange,checks=checks.ToArray()},true));
        Debug.Log("PHOTO_ACCESS_KERB finishedEdges="+accepted.Length+" topChecks="+checks.Count+" routePositionsUnchanged=True");
    }

    static bool ExposedToGrass(Vector3 point,float pathHeight)
    {
        // Prefer the highest horizontal solid surface. The terrain under a road
        // must never be mistaken for a free grass-facing edge.
        foreach(var hit in Physics.RaycastAll(point+Vector3.up*3,Vector3.down,4).OrderByDescending(h=>h.point.y))
        {
            if(hit.normal.y<.85f||hit.collider.isTrigger)continue;
            if(hit.point.y>pathHeight+.025f)return false;
            if(hit.collider.name=="Grass")return true;
            var mc=hit.collider as MeshCollider;var renderer=hit.collider.GetComponent<Renderer>();
            if(!mc||!mc.sharedMesh||!renderer||hit.triangleIndex<0)return false;
            int end=0;
            for(int sub=0;sub<mc.sharedMesh.subMeshCount;sub++)
            {
                end+=(int)mc.sharedMesh.GetIndexCount(sub)/3;if(hit.triangleIndex>=end)continue;
                var material=renderer.sharedMaterials[sub];
                float kind=material&&material.HasProperty("_SurfaceClass")?material.GetFloat("_SurfaceClass"):0;
                return kind>.5f&&kind<1.5f;
            }
            return false;
        }
        return false;
    }
}
