using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// A constrained, explicitly approximate finish of free soil. This never reads map input,
// changes a building/entrance/pavement transform, saves a scene, or starts a build.
public static class PhotoDistrictTerrainStage04
{
    const string Root="Assets/PhotoReconstruction/Stage04Terrain";
    const string AppliedName="Грунт района · приблизительные отметки stage04";
    const float GroundStep=5, GuardStep=2.5f, MaxDelta=.22f;
    static readonly Rect Work=Rect.MinMaxRect(-155,-235,230,195);
    // The photographic raised islands are built by PhotoCentralCourtyardStage04.
    // Do not turn their surrounding courtyard into a guessed broad hill.
    static readonly Rect CentralReserved=Rect.MinMaxRect(19,23,64,52);
    static readonly RaycastHit[] Hits=new RaycastHit[128];

    [Serializable] sealed class Zone
    {
        public string name; public Vector2 center,radius; public float amplitude;
        public Zone(string n,float x,float z,float rx,float rz,float a){name=n;center=new Vector2(x,z);radius=new Vector2(rx,rz);amplitude=a;}
    }
    static readonly Zone[] Zones={
        new Zone("Север · дома 1–4",-100,138,45,45,.12f),
        new Zone("Запад · дом 6",-75,5,50,70,.18f),
        new Zone("Центр 5/7 · общий холм не создаётся",26,43,60,60,0),
        new Zone("Восток · дома 9/11/13",155,50,70,90,.22f),
        new Zone("Юг · дома 8/10/15",38,-145,95,70,.20f)
    };
    sealed class Surface
    {
        public int[] ends; public bool[] hard;
        public bool IsHard(int triangle){for(int i=0;i<ends.Length;i++)if(triangle<ends[i])return hard[i];return false;}
    }
    [Serializable] sealed class Report
    {
        public string note;
        public Bounds groundBounds; public Rect workBounds,centralReserved;
        public float groundStep,guardStep,baseline,maxDelta,maxSlope,maxGrassDelta,maxProtectedRise;
        public int groundVertices,groundTriangles,buildingFootprints,hardscapeColliders,protectedProps,
            protectedSamples,physicsQueries,clampedGroundVertices,grassMeshes,grassVertices,physicalProtectedChecks,protectedPropChecks;
        public bool finite,buildingsUnmoved,hardscapeUnmoved;
        public Zone[] zones;
    }
    sealed class Grid
    {
        public Bounds bounds; public float baseline,dx,dz; public int nx,nz; public float[] delta;
        public float Delta(float x,float z)
        {
            if(x<bounds.min.x||x>bounds.max.x||z<bounds.min.z||z>bounds.max.z)return 0;
            float fx=(x-bounds.min.x)/dx,fz=(z-bounds.min.z)/dz;
            int i=Mathf.Clamp(Mathf.FloorToInt(fx),0,nx-2),j=Mathf.Clamp(Mathf.FloorToInt(fz),0,nz-2);
            float u=Mathf.Clamp01(fx-i),v=Mathf.Clamp01(fz-j);
            float a=delta[j*nx+i],b=delta[j*nx+i+1],c=delta[(j+1)*nx+i],d=delta[(j+1)*nx+i+1];
            // Sample the actual two triangles, rather than a different bilinear surface.
            return u+v<=1?a+(b-a)*u+(c-a)*v:d+(c-d)*(1-u)+(b-d)*(1-v);
        }
    }

    public static void Apply()
    {
        if(EditorSceneManager.GetActiveScene().path!="Assets/Scenes/Street.unity")throw new InvalidOperationException("Open the saved Street scene before applying terrain stage04.");
        if(GameObject.Find(AppliedName)){Debug.Log("PHOTO_TERRAIN04 already applied; no repeated grass deformation.");return;}
        Physics.SyncTransforms();
        var buildings=UnityEngine.Object.FindObjectsByType<StreetBuilding>();
        var poses=buildings.ToDictionary(b=>b,b=>(b.transform.position,b.transform.rotation,b.transform.localScale));
        var floor=UnityEngine.Object.FindObjectsByType<MeshFilter>().Single(f=>f.name=="Grass"&&f.GetComponent<Renderer>()&&f.GetComponent<Renderer>().bounds.size.x>1000);
        var renderer=floor.GetComponent<MeshRenderer>();var oldBounds=renderer.bounds;float baseline=oldBounds.max.y;
        if(Mathf.Abs(baseline+.01f)>.025f)throw new InvalidOperationException("The inspected ground baseline changed; refusing to guess a replacement level: "+baseline);
        var surfaces=new Dictionary<Collider,Surface>();
        foreach(var mc in UnityEngine.Object.FindObjectsByType<MeshCollider>())
        {
            if(mc.gameObject==floor.gameObject||!mc.enabled||!mc.sharedMesh||mc.isTrigger)continue;
            var mr=mc.GetComponent<MeshRenderer>();if(!mr)continue;
            var mesh=mc.sharedMesh;var materials=mr.sharedMaterials;int end=0;
            var data=new Surface{ends=new int[mesh.subMeshCount],hard=new bool[mesh.subMeshCount]};
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                end+=(int)mesh.GetIndexCount(sub)/3;data.ends[sub]=end;
                data.hard[sub]=sub<materials.Length&&HardMaterial(materials[sub],mesh.name);
            }
            if(data.hard.Any(v=>v))surfaces.Add(mc,data);
        }
        var bodyBounds=UnityEngine.Object.FindObjectsByType<BoxCollider>().Where(c=>c.gameObject!=floor.gameObject&&c.enabled&&!c.isTrigger&&c.bounds.size.y>2&&c.bounds.min.y<baseline+.6f)
            .Select(c=>ExpandXZ(c.bounds,2)).ToList();
        var house=UnityEngine.Object.FindObjectsByType<Transform>().FirstOrDefault(t=>t.name.StartsWith("Дом №5",StringComparison.Ordinal));
        if(house)
        {
            var rs=house.GetComponentsInChildren<Renderer>();if(rs.Length>0){var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);bodyBounds.Add(ExpandXZ(b,2));}
        }
        if(surfaces.Count==0||bodyBounds.Count==0)throw new InvalidOperationException("Hardscape/building safety masks are missing; no terrain was changed.");
        var props=new List<Bounds>();
        foreach(var c in UnityEngine.Object.FindObjectsByType<Collider>())
        {
            if(!c.enabled||c.isTrigger||c.gameObject==floor.gameObject||surfaces.ContainsKey(c)||c.GetComponentInParent<StreetBuilding>()||c is CharacterController)continue;
            if(house&&c.transform.IsChildOf(house)||Natural(c.transform))continue;
            var b=c.bounds;
            if(b.min.y<baseline+.6f&&b.max.y>baseline+.1f&&b.size.x<18&&b.size.z<18)props.Add(ExpandXZ(b,.65f));
        }
        int guardNX=Mathf.RoundToInt(Work.width/GuardStep)+1,guardNZ=Mathf.RoundToInt(Work.height/GuardStep)+1;
        var protect=new bool[guardNX*guardNZ];var distance=new float[protect.Length];int queries=0,protectedCount=0;
        var offsets=new[]{Vector2.zero,new Vector2(.6f,0),new Vector2(-.6f,0),new Vector2(0,.6f),new Vector2(0,-.6f)};
        for(int z=0;z<guardNZ;z++)for(int x=0;x<guardNX;x++)
        {
            var p=new Vector3(Work.xMin+x*GuardStep,baseline,Work.yMin+z*GuardStep);
            bool blocked=CentralReserved.Contains(new Vector2(p.x,p.z))||bodyBounds.Any(b=>ContainsXZ(b,p))||props.Any(b=>ContainsXZ(b,p));
            if(!blocked)
                foreach(var offset in offsets)
                {
                    queries++;int count=Physics.RaycastNonAlloc(new Vector3(p.x+offset.x,baseline+3,p.z+offset.y),Vector3.down,Hits,4,~0,QueryTriggerInteraction.Ignore);
                    if(count==Hits.Length)throw new InvalidOperationException("Terrain protection ray overflow; no changes committed.");
                    for(int i=0;i<count;i++)
                        if(Hits[i].normal.y>.5f&&surfaces.TryGetValue(Hits[i].collider,out var s)&&s.IsHard(Hits[i].triangleIndex)){blocked=true;break;}
                    if(blocked)break;
                }
            int index=z*guardNX+x;protect[index]=blocked;distance[index]=blocked?0:10000;if(blocked)protectedCount++;
        }
        // Two-pass chamfer distance, using metres rather than a stochastic/noisy field.
        float diagonal=GuardStep*Mathf.Sqrt(2);
        for(int z=0;z<guardNZ;z++)for(int x=0;x<guardNX;x++)
        {
            int i=z*guardNX+x;
            if(x>0)distance[i]=Mathf.Min(distance[i],distance[i-1]+GuardStep);
            if(z>0)distance[i]=Mathf.Min(distance[i],distance[i-guardNX]+GuardStep);
            if(z>0&&x>0)distance[i]=Mathf.Min(distance[i],distance[i-guardNX-1]+diagonal);
            if(z>0&&x+1<guardNX)distance[i]=Mathf.Min(distance[i],distance[i-guardNX+1]+diagonal);
        }
        for(int z=guardNZ-1;z>=0;z--)for(int x=guardNX-1;x>=0;x--)
        {
            int i=z*guardNX+x;
            if(x+1<guardNX)distance[i]=Mathf.Min(distance[i],distance[i+1]+GuardStep);
            if(z+1<guardNZ)distance[i]=Mathf.Min(distance[i],distance[i+guardNX]+GuardStep);
            if(z+1<guardNZ&&x>0)distance[i]=Mathf.Min(distance[i],distance[i+guardNX-1]+diagonal);
            if(z+1<guardNZ&&x+1<guardNX)distance[i]=Mathf.Min(distance[i],distance[i+guardNX+1]+diagonal);
        }
        float SafetyDistance(float x,float z)
        {
            int i=Mathf.Clamp(Mathf.RoundToInt((x-Work.xMin)/GuardStep),0,guardNX-1);
            int j=Mathf.Clamp(Mathf.RoundToInt((z-Work.yMin)/GuardStep),0,guardNZ-1);
            return distance[j*guardNX+i];
        }
        var grid=new Grid{bounds=oldBounds,baseline=baseline,nx=Mathf.RoundToInt(oldBounds.size.x/GroundStep)+1,nz=Mathf.RoundToInt(oldBounds.size.z/GroundStep)+1};
        grid.dx=oldBounds.size.x/(grid.nx-1);grid.dz=oldBounds.size.z/(grid.nz-1);grid.delta=new float[grid.nx*grid.nz];
        for(int z=0;z<grid.nz;z++)for(int x=0;x<grid.nx;x++)
        {
            float px=oldBounds.min.x+x*grid.dx,pz=oldBounds.min.z+z*grid.dz;
            if(!Work.Contains(new Vector2(px,pz)))continue;
            float raw=0;
            foreach(var zone in Zones)
            {
                float ax=(px-zone.center.x)/zone.radius.x,az=(pz-zone.center.y)/zone.radius.y;
                float q=Mathf.Max(0,1-ax*ax-az*az);raw+=zone.amplitude*q*q;
            }
            float edge=Mathf.Min(px-Work.xMin,Mathf.Min(Work.xMax-px,Mathf.Min(pz-Work.yMin,Work.yMax-pz)));
            grid.delta[z*grid.nx+x]=Mathf.Min(MaxDelta,raw)*Mathf.SmoothStep(0,1,Mathf.Clamp01(SafetyDistance(px,pz)/8))
                *Mathf.SmoothStep(0,1,Mathf.Clamp01(edge/15));
        }
        int clamped=0;
        // A 5m mesh can interpolate across a narrower protected path between its vertices.
        // Flatten its entire cell at every protected 2.5m sample, not just the sample itself.
        for(int z=0;z<guardNZ;z++)for(int x=0;x<guardNX;x++)
            if(protect[z*guardNX+x])
            {
                float px=Work.xMin+x*GuardStep,pz=Work.yMin+z*GuardStep;
                int i=Mathf.Clamp(Mathf.FloorToInt((px-oldBounds.min.x)/grid.dx),0,grid.nx-2);
                int j=Mathf.Clamp(Mathf.FloorToInt((pz-oldBounds.min.z)/grid.dz),0,grid.nz-2);
                foreach(int index in new[]{j*grid.nx+i,j*grid.nx+i+1,(j+1)*grid.nx+i,(j+1)*grid.nx+i+1})
                    if(grid.delta[index]>0){grid.delta[index]=0;clamped++;}
            }
        // Tiny lamp/bench footprints can fall between 2.5m guard samples. Clamp every
        // 5m cell intersecting their full footprint, and the body boxes, explicitly.
        foreach(var bound in bodyBounds.Concat(props))
        {
            if(bound.max.x<Work.xMin||bound.min.x>Work.xMax||bound.max.z<Work.yMin||bound.min.z>Work.yMax)continue;
            int left=Mathf.Clamp(Mathf.FloorToInt((bound.min.x-oldBounds.min.x)/grid.dx),0,grid.nx-2);
            int right=Mathf.Clamp(Mathf.FloorToInt((bound.max.x-oldBounds.min.x)/grid.dx)+1,1,grid.nx-1);
            int bottom=Mathf.Clamp(Mathf.FloorToInt((bound.min.z-oldBounds.min.z)/grid.dz),0,grid.nz-2);
            int top=Mathf.Clamp(Mathf.FloorToInt((bound.max.z-oldBounds.min.z)/grid.dz)+1,1,grid.nz-1);
            for(int j=bottom;j<=top;j++)for(int i=left;i<=right;i++)if(grid.delta[j*grid.nx+i]>0){grid.delta[j*grid.nx+i]=0;clamped++;}
        }
        var world=new Vector3[grid.delta.Length];var vertices=new Vector3[world.Length];var normals=new Vector3[world.Length];var tangents=new Vector4[world.Length];
        var normalToLocal=floor.transform.localToWorldMatrix.transpose;
        for(int z=0;z<grid.nz;z++)for(int x=0;x<grid.nx;x++)
        {
            int i=z*grid.nx+x;world[i]=new Vector3(oldBounds.min.x+x*grid.dx,baseline+grid.delta[i],oldBounds.min.z+z*grid.dz);
            vertices[i]=floor.transform.InverseTransformPoint(world[i]);
            float a=grid.delta[z*grid.nx+Mathf.Max(0,x-1)],b=grid.delta[z*grid.nx+Mathf.Min(grid.nx-1,x+1)];
            float c=grid.delta[Mathf.Max(0,z-1)*grid.nx+x],d=grid.delta[Mathf.Min(grid.nz-1,z+1)*grid.nx+x];
            var normal=new Vector3(-(b-a)/(grid.dx*(x==0||x==grid.nx-1?1:2)),1,-(d-c)/(grid.dz*(z==0||z==grid.nz-1?1:2))).normalized;
            normals[i]=normalToLocal.MultiplyVector(normal).normalized;
            var tangent=floor.transform.InverseTransformVector(Vector3.ProjectOnPlane(Vector3.right,normal).normalized).normalized;
            tangents[i]=new Vector4(tangent.x,tangent.y,tangent.z,1);
        }
        int[] indices=new int[(grid.nx-1)*(grid.nz-1)*6];int at=0;float maxSlope=0;
        for(int z=0;z<grid.nz-1;z++)for(int x=0;x<grid.nx-1;x++)
        {
            int a=z*grid.nx+x,b=a+1,c=a+grid.nx,d=c+1;
            indices[at++]=a;indices[at++]=c;indices[at++]=b;indices[at++]=b;indices[at++]=c;indices[at++]=d;
            foreach(var n in new[]{Vector3.Cross(world[c]-world[a],world[b]-world[a]).normalized,Vector3.Cross(world[c]-world[b],world[d]-world[b]).normalized})
                maxSlope=Mathf.Max(maxSlope,Mathf.Sqrt(n.x*n.x+n.z*n.z)/Mathf.Max(.0001f,n.y));
        }
        if(maxSlope>.08f||world.Any(p=>!Finite(p)))throw new InvalidOperationException("Approximate ground violates slope/finite constraints: "+maxSlope);
        Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.Refresh();
        var groundMesh=new Mesh{name="PhotoDistrictGround04",indexFormat=IndexFormat.UInt32};
        groundMesh.vertices=vertices;groundMesh.normals=normals;groundMesh.tangents=tangents;groundMesh.triangles=indices;groundMesh.RecalculateBounds();
        groundMesh=SaveMesh(groundMesh,"DistrictGround.asset");floor.sharedMesh=groundMesh;
        foreach(var old in floor.GetComponents<Collider>())UnityEngine.Object.DestroyImmediate(old);
        var groundCollider=floor.gameObject.AddComponent<MeshCollider>();groundCollider.sharedMesh=groundMesh;
        Physics.SyncTransforms();int physicalChecks=0,propChecks=0;float maxProtectedRise=0;
        for(int z=0;z<guardNZ;z++)for(int x=0;x<guardNX;x++)
            if(protect[z*guardNX+x])
            {
                var point=new Vector3(Work.xMin+x*GuardStep,baseline,Work.yMin+z*GuardStep);physicalChecks++;
                if(!groundCollider.Raycast(new Ray(point+Vector3.up,Vector3.down),out var hit,1.1f))throw new InvalidOperationException("Terrain hole at protected sample "+point);
                maxProtectedRise=Mathf.Max(maxProtectedRise,hit.point.y-baseline);
            }
        foreach(var bound in props)
        {
            if(!Work.Contains(new Vector2(bound.center.x,bound.center.z)))continue;
            var point=new Vector3(bound.center.x,baseline,bound.center.z);propChecks++;
            if(!groundCollider.Raycast(new Ray(point+Vector3.up,Vector3.down),out var hit,1.1f))throw new InvalidOperationException("Terrain hole under a fixed prop: "+point);
            maxProtectedRise=Mathf.Max(maxProtectedRise,hit.point.y-baseline);
        }
        if(maxProtectedRise>.001f)throw new InvalidOperationException("Terrain rose inside a protected feature: "+maxProtectedRise);
        int grassMeshes=0,grassVertices=0;float maxGrassDelta=0;var lods=new HashSet<LODGroup>();
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if(!filter.sharedMesh||!filter.sharedMesh.name.StartsWith("StreetGrass_",StringComparison.Ordinal))continue;
            var grassRenderer=filter.GetComponent<Renderer>();
            if(grassRenderer&&(grassRenderer.bounds.max.x<Work.xMin||grassRenderer.bounds.min.x>Work.xMax||grassRenderer.bounds.max.z<Work.yMin||grassRenderer.bounds.min.z>Work.yMax))continue;
            var original=filter.sharedMesh;var positions=original.vertices;bool changed=false;
            for(int i=0;i<positions.Length;i++)
            {
                var p=filter.transform.TransformPoint(positions[i]);float delta=grid.Delta(p.x,p.z);
                if(delta<=.000001f)continue;
                positions[i]+=filter.transform.InverseTransformVector(Vector3.up*delta);changed=true;grassVertices++;maxGrassDelta=Mathf.Max(maxGrassDelta,delta);
            }
            if(!changed)continue;
            var mesh=UnityEngine.Object.Instantiate(original);mesh.name=original.name+"_Terrain04";mesh.vertices=positions;mesh.RecalculateBounds();
            var id=GlobalObjectId.GetGlobalObjectIdSlow(filter);filter.sharedMesh=SaveMesh(mesh,"Grass_"+id.targetObjectId+".asset");grassMeshes++;
            var group=filter.GetComponentInParent<LODGroup>();if(group)lods.Add(group);EditorUtility.SetDirty(filter);
        }
        foreach(var lod in lods)lod.RecalculateBounds();
        foreach(var b in buildings)if(b.transform.position!=poses[b].Item1||b.transform.rotation!=poses[b].Item2||b.transform.localScale!=poses[b].Item3)throw new InvalidOperationException("Terrain changed a building pose.");
        var metadata=new GameObject(AppliedName);metadata.tag="EditorOnly";
        foreach(var zone in Zones)
        {
            var go=new GameObject(zone.name);go.tag="EditorOnly";go.transform.SetParent(metadata.transform,false);go.transform.position=new Vector3(zone.center.x,baseline,zone.center.y);
            var marker=go.AddComponent<PhotoSurveyMarker>();marker.category="approximate free soil terrain";marker.geometryProvisional=true;
            marker.observation="Свободный грунт с сохранением всех зданий, входов и существующих покрытий. Центральные поднятые острова делаются отдельным узлом по фотографиям.";
            marker.uncertainty="Амплитуда "+zone.amplitude.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"м — проектная оценка, не обмер. Общий центральный холм и DSM-уклон не создаются.";
            marker.sourceUrls=new[]{"https://gelio.livejournal.com/248828.html"};marker.studyBounds=new Bounds(go.transform.position,new Vector3(zone.radius.x*2,.5f,zone.radius.y*2));
        }
        Directory.CreateDirectory("street_qa/terrain_stage04");
        File.WriteAllText("street_qa/terrain_stage04/district_terrain.json",JsonUtility.ToJson(new Report{
            note="Deterministic approximate free-soil relief only. No broad central hill, no map/DSM input. All surveyed-looking amplitudes are explicitly estimates. Existing hardscape and building poses are unchanged; protected ground cells remain at the original baseline. Raised central islands belong to a separate photo stage.",
            groundBounds=oldBounds,workBounds=Work,centralReserved=CentralReserved,groundStep=GroundStep,guardStep=GuardStep,baseline=baseline,maxDelta=grid.delta.Max(),maxSlope=maxSlope,
            maxGrassDelta=maxGrassDelta,maxProtectedRise=maxProtectedRise,groundVertices=vertices.Length,groundTriangles=indices.Length/3,buildingFootprints=bodyBounds.Count,
            hardscapeColliders=surfaces.Count,protectedProps=props.Count,protectedSamples=protectedCount,physicsQueries=queries,clampedGroundVertices=clamped,grassMeshes=grassMeshes,
            grassVertices=grassVertices,physicalProtectedChecks=physicalChecks,protectedPropChecks=propChecks,finite=true,buildingsUnmoved=true,hardscapeUnmoved=true,zones=Zones},true));
        EditorUtility.SetDirty(floor);EditorUtility.SetDirty(groundCollider);EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("PHOTO_TERRAIN04 groundTriangles="+indices.Length/3+" protected="+protectedCount+" maxSlope="+maxSlope+" maxDelta="+grid.delta.Max()+" grassMeshes="+grassMeshes+" noBroadCentralHill=True");
    }

    static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z);
    static bool ContainsXZ(Bounds b,Vector3 p)=>p.x>=b.min.x&&p.x<=b.max.x&&p.z>=b.min.z&&p.z<=b.max.z;
    static Bounds ExpandXZ(Bounds b,float amount){b.Expand(new Vector3(amount*2,0,amount*2));return b;}
    static bool Natural(Transform t)
    {
        for(var p=t;p;p=p.parent)
        {
            string n=p.name.ToLowerInvariant();
            if(n.Contains("дерев")||n.Contains("куст")||n.Contains("трава")||n.Contains("лес ")||n.Contains("woodland")||n.Contains("broadleaf")||n.Contains("spruce")||n.Contains("shrub"))return true;
        }
        return false;
    }
    static bool HardMaterial(Material material,string meshName)
    {
        if(!material)return false;string n=material.name.ToLowerInvariant();
        if(n.Contains("grass")||n.Contains("vergesurface")||n.Contains("mulch")||n.Contains("soil")||n.Contains("woodland"))return false;
        float kind=material.HasProperty("_SurfaceClass")?material.GetFloat("_SurfaceClass"):0;
        if(kind>.5f&&kind<1.5f)return false;
        return meshName.StartsWith("Stage03Surface_",StringComparison.Ordinal)||n.Contains("asphalt")||n.Contains("pav")||n.Contains("concrete")||n.Contains("kerb")||n.Contains("curb")
            ||n.Contains("roadpaint")||n.Contains("footpath")||n=="path"||n=="residentialaccessedge"||meshName.Contains("Photo11LowThreshold")
            ||meshName=="ReferenceRoad"||meshName.Contains("ReferenceKerbs")||meshName=="Map_Asphalt"||meshName=="Map_Path"||meshName=="Map_Pavement";
    }
    static Mesh SaveMesh(Mesh mesh,string file)
    {
        string path=Root+"/Meshes/"+file;var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);return saved;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
}
