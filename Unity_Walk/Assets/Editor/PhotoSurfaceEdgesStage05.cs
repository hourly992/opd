using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A finish of the saved asphalt boundary. This does not regenerate the road,
// alter its top vertices, load map data, move buildings, or save the scene.
public static class PhotoSurfaceEdgesStage05
{
    const string Root="Assets/PhotoReconstruction/Stage05Edges";
    const string AppliedName="Кромки проездов · фаска и подсыпка · stage05";
    const float SampleStep=.35f, NominalWidth=.32f, MinimumWidth=.14f, MaximumWidth=.55f;
    static readonly RaycastHit[] Hits=new RaycastHit[128];

    [Serializable] public sealed class Report
    {
        public string note;
        public string[] sourceObjects;
        public int sourceMeshes,sourceTopTriangles,boundaryEdges,nonManifoldEdges,sections,acceptedSections,
            protectedSections,groundUnavailableSections,excessiveRiseSections,ambiguousJunctionSections,
            rejectedSpans,emittedSpans,triangles,meshBatches,grassMeshes,grassTrianglesRemoved,physicsQueries;
        public float candidateBoundaryLength,finishedBoundaryLength,minimumWidth,maximumWidth,maximumDrop,maximumSlope,maximumRejectedSlope,maximumSharedJoinGap;
        public bool sourceMeshReferencesPreserved,originalCollidersPreserved,buildingPosesPreserved,finite,passed;
    }
    readonly struct Key : IEquatable<Key>
    {
        public readonly int x,y,z;
        public Key(Vector3 p){x=Mathf.RoundToInt(p.x*2000);y=Mathf.RoundToInt(p.y*2000);z=Mathf.RoundToInt(p.z*2000);}
        public bool Equals(Key other)=>x==other.x&&y==other.y&&z==other.z;
        public override bool Equals(object value)=>value is Key other&&Equals(other);
        public override int GetHashCode()=>unchecked((x*397^y)*397^z);
        public int Compare(Key other){int c=x.CompareTo(other.x);if(c!=0)return c;c=y.CompareTo(other.y);return c!=0?c:z.CompareTo(other.z);}
    }
    readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        readonly Key a,b;
        public EdgeKey(Key first,Key second){if(first.Compare(second)<0){a=first;b=second;}else{a=second;b=first;}}
        public bool Equals(EdgeKey other)=>a.Equals(other.a)&&b.Equals(other.b);
        public override bool Equals(object value)=>value is EdgeKey other&&Equals(other);
        public override int GetHashCode()=>unchecked(a.GetHashCode()*397^b.GetHashCode());
    }
    sealed class Edge
    {
        public Key ka,kb;public Vector3 a,b,outward;public int count;
    }
    sealed class Section
    {
        public Vector3 edge,outward;public readonly List<Section> neighbors=new List<Section>();
        public bool valid;public float width,ground;public Vector3[] profile;
    }
    sealed class Span{public Section a,b;}
    sealed class SurfaceInfo
    {
        public int[] ends;public Material[] materials;
        public Material At(int triangle){for(int i=0;i<ends.Length;i++)if(triangle<ends[i])return i<materials.Length?materials[i]:null;return null;}
    }

    public static void Apply()
    {
        if(EditorSceneManager.GetActiveScene().path!="Assets/Scenes/Street.unity")throw new InvalidOperationException("Open the saved Street scene before finishing its edges.");
        if(GameObject.Find(AppliedName)){Debug.Log("PHOTO_EDGES05 already applied; no duplicate shoulders created.");return;}
        Physics.SyncTransforms();
        var report=new Report{note="Saved secondary asphalt only. An estimated narrow asphalt arris and compacted-soil/aggregate taper meet the actual current terrain; construction widths are not survey measurements. All existing hardscape tops, buildings, entrances, road axes and colliders remain intact. No invented drainage structures or new roads. Protected pavement/curb/building/prop adjacency is rejected using physics. Shared section profiles are reused at every emitted span joint.",minimumWidth=float.MaxValue};
        var allFilters=UnityEngine.Object.FindObjectsByType<MeshFilter>();
        var originalMeshes=allFilters.ToDictionary(f=>f,f=>f.sharedMesh);
        var originalColliders=UnityEngine.Object.FindObjectsByType<Collider>().ToDictionary(c=>c,c=>(c.enabled,c is MeshCollider mc?mc.sharedMesh:null));
        var poses=UnityEngine.Object.FindObjectsByType<StreetBuilding>().ToDictionary(b=>b.transform,b=>(b.transform.position,b.transform.rotation,b.transform.localScale));
        var detailedHouse=UnityEngine.Object.FindObjectsByType<Transform>().FirstOrDefault(t=>t.name.StartsWith("Дом №5",StringComparison.Ordinal));
        if(detailedHouse&&!poses.ContainsKey(detailedHouse))poses.Add(detailedHouse,(detailedHouse.position,detailedHouse.rotation,detailedHouse.localScale));
        var ground=UnityEngine.Object.FindObjectsByType<MeshCollider>().Single(c=>c.enabled&&c.sharedMesh&&c.name=="Grass"&&c.bounds.size.x>1000);
        var surfaces=new Dictionary<Collider,SurfaceInfo>();
        foreach(var mc in UnityEngine.Object.FindObjectsByType<MeshCollider>())
        {
            if(!mc.enabled||!mc.sharedMesh)continue;var renderer=mc.GetComponent<Renderer>();if(!renderer||!renderer.enabled)continue;
            int end=0;var info=new SurfaceInfo{ends=new int[mc.sharedMesh.subMeshCount],materials=renderer.sharedMaterials};
            for(int i=0;i<info.ends.Length;i++){end+=(int)mc.sharedMesh.GetIndexCount(i)/3;info.ends[i]=end;}surfaces.Add(mc,info);
        }
        var edges=new Dictionary<EdgeKey,Edge>();var sources=new List<string>();
        foreach(var filter in allFilters)
        {
            var mesh=filter.sharedMesh;var renderer=filter.GetComponent<Renderer>();
            if(!mesh||!renderer||!renderer.enabled||!filter.gameObject.activeInHierarchy||mesh.name.Contains("ReferenceRoad")||mesh.name.Contains("ReferenceWalkways"))continue;
            var vertices=mesh.vertices;var materials=renderer.sharedMaterials;bool source=false;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                if(sub>=materials.Length||!IsAsphalt(materials[sub]))continue;
                var indices=mesh.GetTriangles(sub);
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=filter.transform.TransformPoint(vertices[indices[i]]);var b=filter.transform.TransformPoint(vertices[indices[i+1]]);var c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                    var cross=Vector3.Cross(b-a,c-a);if(cross.sqrMagnitude<1e-12f||cross.y/cross.magnitude<.85f||Mathf.Max(a.y,Mathf.Max(b.y,c.y))>.5f)continue;
                    AddEdge(a,b,edges);AddEdge(b,c,edges);AddEdge(c,a,edges);source=true;report.sourceTopTriangles++;
                }
            }
            if(source)sources.Add(filter.name+" / "+mesh.name);
        }
        if(sources.Count==0)throw new InvalidOperationException("No existing secondary asphalt tops found; refusing to create guessed shoulders.");
        var boundary=edges.Values.Where(e=>e.count==1).ToArray();report.sourceObjects=sources.ToArray();report.sourceMeshes=sources.Count;report.boundaryEdges=boundary.Length;
        report.nonManifoldEdges=edges.Values.Count(e=>e.count>2);
        var nodes=new Dictionary<Key,Section>();var normals=new Dictionary<Key,List<Vector3>>();
        foreach(var e in boundary)
        {
            foreach(var p in new[]{(e.ka,e.a),(e.kb,e.b)})
            {
                if(!nodes.ContainsKey(p.Item1)){nodes.Add(p.Item1,new Section{edge=p.Item2});normals.Add(p.Item1,new List<Vector3>());}
                normals[p.Item1].Add(e.outward);
            }
            report.candidateBoundaryLength+=Vector3.Distance(e.a,e.b);
        }
        foreach(var kv in nodes)
        {
            var ns=normals[kv.Key];var sum=ns.Aggregate(Vector3.zero,(a,b)=>a+b);
            if(ns.Count!=2||sum.sqrMagnitude<.04f){report.ambiguousJunctionSections++;continue;}
            sum.Normalize();float cosine=Vector3.Dot(sum,ns[0]);if(cosine<.5f){report.ambiguousJunctionSections++;continue;}
            kv.Value.outward=sum/cosine;
        }
        var spans=new List<Span>();var sections=nodes.Values.ToList();
        foreach(var e in boundary)
        {
            var first=nodes[e.ka];var last=nodes[e.kb];var previous=first;int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(e.a,e.b)/SampleStep));
            for(int i=1;i<=count;i++)
            {
                var next=i==count?last:new Section{edge=Vector3.Lerp(e.a,e.b,i/(float)count),outward=e.outward};
                if(i!=count)sections.Add(next);previous.neighbors.Add(next);next.neighbors.Add(previous);spans.Add(new Span{a=previous,b=next});previous=next;
            }
        }
        foreach(var section in sections)Prepare(section,ground,surfaces,report);
        report.sections=sections.Count;report.acceptedSections=sections.Count(s=>s.valid);
        // A short tapered termination avoids a rectangular butt at a curb or entrance.
        foreach(var section in sections.Where(s=>s.valid))if(section.neighbors.Any(n=>!n.valid))section.width=0;
        foreach(var section in sections.Where(s=>s.valid))section.profile=Profile(section);
        Directory.CreateDirectory(Root+"/Meshes");Directory.CreateDirectory(Root+"/Materials");AssetDatabase.Refresh();
        var arris=Material("AsphaltArris",AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Pilot/Materials/ReferenceServiceAsphalt.mat"));
        arris.SetFloat("_AlbedoContrast",.6f);arris.SetFloat("_RoughnessBias",.17f);arris.SetFloat("_Parallax",.001f);arris.SetColor("_TileMeters",new Color(.75f,.75f,0,0));
        var soil=Material("SoilAggregate",null);ConfigureSoil(soil);
        var builders=new Dictionary<string,EnvironmentMesh>();var emitted=new List<Span>();
        foreach(var span in spans)
        {
            if(!span.a.valid||!span.b.valid){report.rejectedSpans++;continue;}
            if(span.a.width<=0&&span.b.width<=0){report.rejectedSpans++;continue;}
            // Intermediate coverage checks also protect narrow pavement connections.
            var midpoint=(span.a.edge+span.b.edge)*.5f;var outside=(span.a.outward+span.b.outward)*.5f;float width=(span.a.width+span.b.width)*.5f;
            if(!Clear(midpoint+outside*(width*.25f),ground,surfaces,report)||!Clear(midpoint+outside*(width*.65f),ground,surfaces,report)||!Clear(midpoint+outside*width,ground,surfaces,report))
            {report.rejectedSpans++;continue;}
            if(!ValidProfile(span.a.profile)||!ValidProfile(span.b.profile))throw new InvalidOperationException("Invalid shoulder section.");
            if(!SafeSpan(span.a.profile,span.b.profile,report)){report.rejectedSpans++;continue;}
            string zone=Zone(midpoint);if(!builders.TryGetValue(zone,out var builder)){builder=new EnvironmentMesh();builders.Add(zone,builder);}
            Face(builder,arris,span.a.profile[0],span.a.profile[1],span.b.profile[1],span.b.profile[0],report);
            Face(builder,soil,span.a.profile[1],span.a.profile[2],span.b.profile[2],span.b.profile[1],report);
            emitted.Add(span);report.emittedSpans++;report.finishedBoundaryLength+=Vector3.Distance(span.a.edge,span.b.edge);
            if(span.a.width>0)report.minimumWidth=Mathf.Min(report.minimumWidth,span.a.width);if(span.b.width>0)report.minimumWidth=Mathf.Min(report.minimumWidth,span.b.width);report.maximumWidth=Mathf.Max(report.maximumWidth,Mathf.Max(span.a.width,span.b.width));
        }
        if(emitted.Count==0)throw new InvalidOperationException("All saved asphalt edges are protected; no guessed edge geometry was committed.");
        var root=new GameObject(AppliedName);var created=new List<MeshCollider>();
        foreach(var pair in builders)
        {
            var go=pair.Value.Create("Shoulder_"+pair.Key,"Переход асфальт–грунт · "+pair.Key,Root);go.transform.SetParent(root.transform,false);
            var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;created.Add(collider);
            GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);report.meshBatches++;
        }
        Physics.SyncTransforms();RemoveGrassOverShoulder(allFilters,created,report);
        var evidence=new GameObject("Размеры подсыпки приблизительные · геодезические отметки не заданы");evidence.tag="EditorOnly";evidence.transform.SetParent(root.transform,false);
        var marker=evidence.AddComponent<PhotoSurveyMarker>();marker.category="asphalt boundary construction estimate";marker.geometryProvisional=true;
        marker.observation="Существующая кромка вторичного асфальта без переноса верха. У бордюров, тротуаров, входов и объектов подсыпка не создаётся. Наружный край заглублён в фактический грунт.";
        marker.uncertainty="Ширина обычно 0,32м, допускается 0,14–0,55м; форма и толщина подсыпки — временное инженерное решение, а не обмер реальной обочины. Новые лотки, колодцы и ливнёвки не размещались.";
        marker.sourceUrls=new[]{"https://yandex.ru/maps/org/91706029280/","https://yandex.ru/maps/org/159257372149/"};marker.studyBounds=new Bounds(new Vector3(40,0,-20),new Vector3(400,1,470));
        report.sourceMeshReferencesPreserved=originalMeshes.Where(kv=>!Grass(kv.Key)).All(kv=>kv.Key&&kv.Key.sharedMesh==kv.Value);
        report.originalCollidersPreserved=originalColliders.All(kv=>kv.Key&&kv.Key.enabled==kv.Value.Item1&&(!(kv.Key is MeshCollider mc)||mc.sharedMesh==kv.Value.Item2));
        report.buildingPosesPreserved=poses.All(kv=>kv.Key&&kv.Key.position==kv.Value.Item1&&kv.Key.rotation==kv.Value.Item2&&kv.Key.localScale==kv.Value.Item3);
        // A profile object is shared, including its exact vertex arrays, between every
        // adjacent span: there is no separately recomputed/mismatched corner endpoint.
        report.maximumSharedJoinGap=0;report.finite=created.All(c=>c.sharedMesh.vertices.All(Finite));
        report.passed=report.sourceMeshReferencesPreserved&&report.originalCollidersPreserved&&report.buildingPosesPreserved&&report.finite&&report.maximumSlope<1.1f;
        Directory.CreateDirectory("street_qa/stage05");File.WriteAllText("street_qa/stage05/surface_edges.json",JsonUtility.ToJson(report,true));
        if(!report.passed)throw new InvalidOperationException("Shoulder pass failed its preservation/finite/slope check; do not save.");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("PHOTO_EDGES05 length="+report.finishedBoundaryLength+" spans="+report.emittedSpans+" triangles="+report.triangles+" batches="+report.meshBatches+" grassTriangles="+report.grassTrianglesRemoved+" maxSlope="+report.maximumSlope+" sourceTopsUnchanged="+report.sourceMeshReferencesPreserved);
    }

    static void AddEdge(Vector3 a,Vector3 b,Dictionary<EdgeKey,Edge> edges)
    {
        var ka=new Key(a);var kb=new Key(b);if(ka.Equals(kb))return;var key=new EdgeKey(ka,kb);
        if(edges.TryGetValue(key,out var edge)){edge.count++;return;}
        edges.Add(key,new Edge{a=a,b=b,ka=ka,kb=kb,count=1,outward=new Vector3(a.z-b.z,0,b.x-a.x).normalized});
    }
    public static void UpgradeSavedMaterials()
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/SoilAggregate.mat");
        if(!material)throw new InvalidOperationException("Apply the saved shoulder geometry before upgrading its material.");
        ConfigureSoil(material);AssetDatabase.SaveAssets();
        Debug.Log("PHOTO_EDGES05 PBR soil albedo/normal/AO-roughness-height configured.");
    }
    static void ConfigureSoil(Material material)
    {
        StreetSurfaceLibrary.Apply(material,"gravel_floor_02",.65f,new Color(.22f,.195f,.16f),.35f);
        material.SetFloat("_Colorize",1);material.SetFloat("_RoughnessBias",.12f);material.SetFloat("_Parallax",.001f);
        material.SetFloat("_Weathering",.12f);material.enableInstancing=true;EditorUtility.SetDirty(material);
    }
    static bool IsAsphalt(Material material)
    {
        if(!material)return false;string n=material.name.ToLowerInvariant();return n.Contains("asphalt")&&!n.Contains("footpath")&&!n.Contains("arris");
    }
    static void Prepare(Section section,MeshCollider ground,Dictionary<Collider,SurfaceInfo> surfaces,Report report)
    {
        if(section.outward.sqrMagnitude<.5f)return;
        if(!Ground(section.edge,ground,out float edgeGround)){report.groundUnavailableSections++;return;}
        float drop=section.edge.y-edgeGround;if(drop<.012f||drop>.28f){report.excessiveRiseSections++;return;}
        float desired=Mathf.Clamp(Mathf.Max(NominalWidth,drop*2.6f),MinimumWidth,MaximumWidth);
        for(float width=desired;width>=MinimumWidth-.001f;width-=.035f)
        {
            var outside=section.edge+section.outward*width;
            if(!Ground(outside,ground,out float outerGround))continue;
            if(outerGround>section.edge.y-.012f||section.edge.y-outerGround>.30f)continue;
            if(!Clear(section.edge+section.outward*.035f,ground,surfaces,report)||!Clear(section.edge+section.outward*(width*.5f),ground,surfaces,report)||!Clear(outside,ground,surfaces,report))continue;
            section.valid=true;section.width=width;section.ground=outerGround;report.maximumDrop=Mathf.Max(report.maximumDrop,section.edge.y-outerGround);return;
        }
        report.protectedSections++;
    }
    static Vector3[] Profile(Section s)
    {
        var a=s.edge-Vector3.up*.0008f;
        if(s.width==0)return new[]{a,a,a};
        var b=s.edge+s.outward*(s.width*.17f);b.y=Mathf.Lerp(a.y,s.ground,.28f);
        var c=s.edge+s.outward*s.width;c.y=s.ground-.008f;
        return new[]{a,b,c};
    }
    static bool Clear(Vector3 p,MeshCollider ground,Dictionary<Collider,SurfaceInfo> surfaces,Report report)
    {
        report.physicsQueries++;int count=Physics.RaycastNonAlloc(p+Vector3.up*2,Vector3.down,Hits,3,~0,QueryTriggerInteraction.Ignore);
        if(count==Hits.Length)throw new InvalidOperationException("Shoulder safety ray buffer overflow.");
        for(int i=0;i<count;i++)
        {
            var hit=Hits[i];var collider=hit.collider;if(collider==ground||collider is CharacterController||hit.point.y<p.y-.6f||hit.normal.y<.2f)continue;
            // Trees, lamps and vehicles are protected too; no skirt is laid through them.
            if(surfaces.TryGetValue(collider,out var info))
            {
                var material=info.At(hit.triangleIndex);string n=material?material.name.ToLowerInvariant():"";
                if(n.Contains("grass")||n.Contains("leaf")||n.Contains("foliage")||n.Contains("mulch")||n.Contains("soil")||n.Contains("vergesurface")||n.Contains("woodland"))continue;
            }
            return false;
        }
        return true;
    }
    static bool Ground(Vector3 p,MeshCollider ground,out float height)
    {
        if(ground.Raycast(new Ray(new Vector3(p.x,3,p.z),Vector3.down),out var hit,5)){height=hit.point.y;return true;}height=0;return false;
    }
    static void Face(EnvironmentMesh builder,Material material,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Report report)
    {
        void Triangle(Vector3 p,Vector3 q,Vector3 r)
        {
            var cross=Vector3.Cross(q-p,r-p);if(cross.sqrMagnitude<1e-12f)return;var normal=cross/cross.magnitude;if(normal.y<0)normal=-normal;
            report.maximumSlope=Mathf.Max(report.maximumSlope,Mathf.Sqrt(normal.x*normal.x+normal.z*normal.z)/Mathf.Max(.001f,normal.y));
            builder.Face(material,normal,p,q,r);report.triangles++;
        }
        Triangle(a,b,c);Triangle(a,c,d);
    }
    static bool ValidProfile(Vector3[] points)=>points!=null&&points.All(Finite);
    static bool SafeSpan(Vector3[] a,Vector3[] b,Report report)
    {
        bool Triangle(Vector3 p,Vector3 q,Vector3 r)
        {
            var cross=Vector3.Cross(q-p,r-p);if(cross.sqrMagnitude<1e-12f)return true;
            float slope=Mathf.Sqrt(cross.x*cross.x+cross.z*cross.z)/Mathf.Max(.000001f,Mathf.Abs(cross.y));
            if(cross.y<=0||slope>1){report.maximumRejectedSlope=Mathf.Max(report.maximumRejectedSlope,slope);return false;}return true;
        }
        for(int ring=0;ring<2;ring++)if(!Triangle(a[ring],a[ring+1],b[ring+1])||!Triangle(a[ring],b[ring+1],b[ring]))return false;
        return true;
    }
    static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z);
    static string Zone(Vector3 p)=>p.z>88?"north":p.z< -90?"south":p.x< -30?"west":p.x>110?"east":"center";
    static Material Material(string name,Material source)
    {
        string path=Root+"/Materials/"+name+".mat";var saved=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!saved){saved=source?new Material(source):new Material(Shader.Find("Street/Surface"));AssetDatabase.CreateAsset(saved,path);}saved.enableInstancing=true;EditorUtility.SetDirty(saved);return saved;
    }
    static void RemoveGrassOverShoulder(MeshFilter[] filters,List<MeshCollider> shoulders,Report report)
    {
        foreach(var filter in filters)
        {
            var original=filter.sharedMesh;if(!Grass(filter))continue;
            var renderer=filter.GetComponent<Renderer>();if(!renderer||!shoulders.Any(s=>s.bounds.Intersects(renderer.bounds)))continue;
            var vertices=original.vertices;var kept=new List<int>[original.subMeshCount];int removed=0;
            bool Covered(Vector3 p)
            {
                foreach(var shoulder in shoulders)
                    if(shoulder.bounds.Contains(new Vector3(p.x,shoulder.bounds.center.y,p.z))&&shoulder.Raycast(new Ray(new Vector3(p.x,1,p.z),Vector3.down),out var hit,2)&&hit.normal.y>.65f)return true;
                return false;
            }
            for(int sub=0;sub<original.subMeshCount;sub++)
            {
                var indices=original.GetTriangles(sub);kept[sub]=new List<int>(indices.Length);
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=filter.transform.TransformPoint(vertices[indices[i]]);var b=filter.transform.TransformPoint(vertices[indices[i+1]]);var c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                    if(Covered((a+b+c)/3)||Covered(a)||Covered(b)||Covered(c)){removed++;continue;}
                    kept[sub].Add(indices[i]);kept[sub].Add(indices[i+1]);kept[sub].Add(indices[i+2]);
                }
            }
            if(removed==0)continue;
            var mesh=UnityEngine.Object.Instantiate(original);for(int sub=0;sub<kept.Length;sub++)mesh.SetTriangles(kept[sub],sub);mesh.RecalculateBounds();
            string path=Root+"/Meshes/Grass_"+GlobalObjectId.GetGlobalObjectIdSlow(filter).targetObjectId+".asset";
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}else{AssetDatabase.CreateAsset(mesh,path);saved=mesh;}
            filter.sharedMesh=saved;EditorUtility.SetDirty(filter);var lod=filter.GetComponentInParent<LODGroup>();if(lod)lod.RecalculateBounds();report.grassMeshes++;report.grassTrianglesRemoved+=removed;
        }
    }
    static bool Grass(MeshFilter filter)
    {
        if(!filter||!filter.sharedMesh)return false;string name=filter.sharedMesh.name;
        return name.Contains("StreetGrass_")||name.StartsWith("Grass_",StringComparison.Ordinal)||filter.name.Contains("StreetGrass_");
    }
}
