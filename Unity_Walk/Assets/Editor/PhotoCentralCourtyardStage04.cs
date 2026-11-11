using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A deliberately bounded, provisional reconstruction of the photographed internal court.
// Apply operates on the OPEN Street scene. It never invokes a generator, opens/saves a scene or builds.
public static class PhotoCentralCourtyardStage04
{
    const string Root = "Assets/PhotoReconstruction/Stage04Courtyard";
    const string ObjectName = "Двор №7 · мощение и подпорные стенки по фото · stage04";
    const float PassageWidth = 3.2f, WallWidth = .24f, RaisedHeight = .31f;
    static readonly string[] Sources = {
        "https://realty.yandex.ru/offer/4006141392816993274/",
        "https://avatars.mds.yandex.net/get-realty-offers/15038303/2d86a840-819c-4ef2-a67e-6098b9d2a49e/app_large",
        "https://avatars.mds.yandex.net/get-realty-offers/14021747/b50d2391-f3f6-4cf7-b362-9896c4513b7f/app_large"
    };
    // Selected against photo_scene_plan.png and the three existing No.7 sections;
    // this is NOT the entire combined renderer Bounds of those sections.
    static readonly Vector2[] Court = {
        new Vector2(24,29),new Vector2(32.5f,28.4f),new Vector2(58.3f,28.4f),
        new Vector2(58.3f,47.1f),new Vector2(31.5f,47.1f),new Vector2(24,44.6f)
    };
    sealed class Cut { internal List<Vector2> polygon; internal Bounds bounds; internal string reason; }
    sealed class Island { internal List<Vector2> outer,inner; internal float TopHeight,baseHeight; }
    [Serializable] sealed class HeightEvidence
    {
        public bool approximate = true;
        public float wallRiseEstimate = RaisedHeight,minimumReservedPassage = PassageWidth;
        public float[] TopHeight;
        public int islandCount,protectedRouteSegments,protectedFootprints;
        public string note = "TopHeight is Unity world Y, not a geodetic altitude. Wall heights and contours are visual estimates; no measured site elevations.";
    }
    sealed class Support
    {
        internal MeshCollider collider; internal bool asphalt;
    }
    static readonly Dictionary<Vector2Int,float> heightCache = new Dictionary<Vector2Int,float>();
    static Support[] supports;
    static Collider[] physicalGuards;
    static Material paving,brick,cap,grass;

    [MenuItem("Street/Photo reconstruction/Apply stage 04 central courtyard to open scene")]
    public static void Apply()
    {
        if (Application.isPlaying || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open Street.unity before applying the local court. This tool never opens a scene.");
        if (GameObject.Find(ObjectName))
            throw new InvalidOperationException("Stage04 central courtyard already exists. Undo that application before changing its layout.");
        Physics.SyncTransforms(); heightCache.Clear();
        var buildings = UnityEngine.Object.FindObjectsByType<StreetBuilding>();
        var poses = buildings.ToDictionary(b=>b,b=>(b.transform.position,b.transform.rotation,b.transform.localScale));
        if (buildings.Count(b=>b.address=="7") < 3)
            throw new InvalidOperationException("Expected three existing No.7 sections; stop rather than placing an unbound court.");
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        if (!access || access.routes == null)
            throw new InvalidOperationException("The existing residential route metadata is required to protect entrance approaches.");

        var bounds = BoundsOf(Court); bounds.Expand(2);
        var cuts = BuildingAndEntranceCuts(buildings,bounds);
        physicalGuards = UnityEngine.Object.FindObjectsByType<Collider>().Where(c=>c.enabled && !c.isTrigger
            && ProjectedIntersects(c.bounds,bounds) && IsPhysicalGuard(c)).ToArray();
        supports = UnityEngine.Object.FindObjectsByType<MeshCollider>().Where(c=>c.enabled && c.sharedMesh
            && ProjectedIntersects(c.bounds,bounds) && !IsUnderBuilding(c.transform)).Select(c=>new Support {
                collider=c,asphalt=IsVehicleSurface(c)
            }).ToArray();
        if (supports.Length==0) throw new InvalidOperationException("No saved ground/surface colliders available in the court.");

        // Existing vehicle surfaces are exclusions, never new grey pedestrian surfacing.
        var vehicleCuts = VehicleCuts(bounds);
        var pavement = new List<List<Vector2>> { Court.ToList() };
        foreach (var cut in cuts.Concat(vehicleCuts)) pavement = SubtractAll(pavement,cut.polygon);
        pavement = pavement.Where(p=>Area(p)>1).ToList();
        if (pavement.Count==0) throw new InvalidOperationException("No safe bounded court pavement after building/driveway exclusions.");

        int protectedRoutes = 0;
        var islandCuts = new List<Cut>(cuts); islandCuts.AddRange(vehicleCuts);
        foreach (var route in access.routes)
        {
            if (route.points==null || route.points.Length<2) continue;
            var points = Simplify(route.points,.16f);
            for (int i=1;i<points.Count;i++)
            {
                Vector2 a=XZ(points[i-1]),b=XZ(points[i]);
                if ((b-a).sqrMagnitude<.01f) continue;
                var corridor=Corridor(a,b,PassageWidth*.5f);
                if (!ProjectedIntersects(BoundsOf(corridor),bounds)) continue;
                islandCuts.Add(NewCut(corridor,"existing residential route, minimum 3.2 m")); protectedRoutes++;
            }
        }
        // A connected cross passage remains even if route metadata changes on a future edit.
        islandCuts.Add(NewCut(Rect(38.5f,27,41.7f,49),"north/south clear passage"));
        islandCuts.Add(NewCut(Rect(23,36.7f,61,40.0f),"east/west clear passage"));
        var seeds = new[] {
            Chamfer(27.0f,30.0f,37.0f,35.5f,.65f),
            Chamfer(43.3f,30.0f,55.2f,35.5f,.65f),
            Chamfer(28.0f,41.5f,54.7f,46.2f,.65f)
        };
        var islands = new List<Island>();
        foreach (var seed in seeds)
        {
            var pieces=new List<List<Vector2>> { seed };
            foreach (var cut in islandCuts) pieces=SubtractAll(pieces,cut.polygon);
            // Convex subtraction can produce several pieces. Keep one coherent substantial
            // island per seed rather than scattered postage-stamp lawns around every probe.
            foreach (var candidate in pieces.OrderByDescending(Area))
            {
                var poly=Clean(candidate);
                if (Area(poly)<7 || poly.Count<3 || poly.Any(v=>!Inside(v,Court))) continue;
                var inner=Inset(poly,WallWidth);
                if (inner==null || Area(inner)<4 || !ClearIsland(poly)) continue;
                var h=poly.Select(HeightAt).ToArray();
                if (h.Max()-h.Min()>.13f) continue; // never bridge a step or garage ramp
                islands.Add(new Island { outer=poly,inner=inner,baseHeight=h.Min(),TopHeight=h.Max()+RaisedHeight });
                break;
            }
        }
        if (islands.Count<2)
            throw new InvalidOperationException("Fewer than two safely connected lawn islands fit. No geometry was changed; inspect routes/colliders before widening the boundary.");

        foreach (string sub in new[]{"Meshes","Materials"}) Directory.CreateDirectory(Root+"/"+sub);
        AssetDatabase.Refresh(); Materials();
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Photo stage04 central courtyard");
        var root=new GameObject(ObjectName); Undo.RegisterCreatedObjectUndo(root,"Add photographed court");
        var sourceFloors=UnityEngine.Object.FindObjectsByType<MeshFilter>().Where(f=>f.sharedMesh&&f.GetComponent<MeshCollider>()&&!IsUnderBuilding(f.transform)).ToArray();
        var emittedCells=new List<List<Vector2>>();
        var mesh=new EnvironmentMesh(); int cells=0;
        foreach (var piece in pavement)
        {
            // Small exact-clipped cells match saved connector heights; no giant sloping quad,
            // stair flattening or stair-shaped grid outline is introduced at the outer edge.
            var bb=BoundsOf(piece);
            for (float x=Mathf.Floor(bb.min.x);x<bb.max.x;x+=1)
                for (float z=Mathf.Floor(bb.min.z);z<bb.max.z;z+=1)
                {
                    var cell=Intersect(piece,Rect(x,z,x+1,z+1));
                    if (cell.Count<3 || Area(cell)<.001f) continue;
                    Vector2 centre=Centroid(cell);
                    if (VehicleAt(centre) || GuardAt(centre)) continue;
                    mesh.Face(paving,Vector3.up,cell.Select(v=>At(v,HeightAt(v))).ToArray());cells++;emittedCells.Add(cell);
                }
            for (int i=0;i<piece.Count;i++)
            {
                Vector2 a=piece[i],b=piece[(i+1)%piece.Count];
                var normal=new Vector3(b.y-a.y,0,a.x-b.x).normalized;
                // Slab sides extend below the support surface, never floating paper edges.
                mesh.Face(paving,normal,At(a,HeightAt(a)),At(b,HeightAt(b)),At(b,HeightAt(b)-.12f),At(a,HeightAt(a)-.12f));
            }
        }
        var pavingObject=Create(mesh,"Stage04CourtPaving","Серое прямоугольное мощение · внутренний двор",root.transform);
        var provenance=pavingObject.AddComponent<SavedPavedSurface>();provenance.existingNetworkDestination=true;provenance.purpose="Photographed inner courtyard pedestrian network";
        var newSurfaces=new List<MeshCollider> { pavingObject.GetComponent<MeshCollider>() };
        for (int i=0;i<islands.Count;i++)
        {
            var islandMesh=new EnvironmentMesh(); AddIsland(islandMesh,islands[i]);
            Create(islandMesh,"Stage04RaisedIsland_"+i,"Газонный остров "+(i+1)+" · подпорная кладка",root.transform);
        }
        RemoveCoveredHardscape(sourceFloors,emittedCells);
        int grassTriangles=RemoveGrassOverPaving(pavement);
        // Paving is registered with the existing route system; route points, endpoints and
        // benches stay exactly where they were. Raised lawns are NOT pedestrian destinations.
        Undo.RecordObject(access,"Register court pedestrian surface");
        access.surfaces=(access.surfaces??Array.Empty<MeshCollider>()).Concat(newSurfaces).Distinct().ToArray();
        EditorUtility.SetDirty(access);
        AddEvidence(root.transform,islands,protectedRoutes,cuts.Count+vehicleCuts.Count);
        Physics.SyncTransforms();
        foreach (var b in buildings)
            if (b.transform.position!=poses[b].Item1 || b.transform.rotation!=poses[b].Item2 || b.transform.localScale!=poses[b].Item3)
                throw new InvalidOperationException("A building transform changed unexpectedly: "+b.name);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); Undo.CollapseUndoOperations(group);
        Debug.Log("PHOTO_STAGE04_COURT islands="+islands.Count+" pavingCells="+cells+" protectedRouteSegments="+protectedRoutes+
            " removedGrassTriangles="+grassTriangles+" wallRiseEstimate="+RaisedHeight+" source=YandexRealtyNo7; scene NOT saved; no map inputs");
    }

    static List<Cut> BuildingAndEntranceCuts(StreetBuilding[] buildings,Bounds study)
    {
        var cuts=new List<Cut>();
        foreach (var c in UnityEngine.Object.FindObjectsByType<BoxCollider>())
        {
            if (!c.enabled || c.isTrigger || !ProjectedIntersects(c.bounds,study)) continue;
            if (IsUnderBuilding(c.transform) && c.bounds.size.y>2 && c.bounds.min.y<1)
            {
                var corners=new List<Vector2>();
                for (int x=-1;x<=1;x+=2) for (int z=-1;z<=1;z+=2)
                    corners.Add(XZ(c.transform.TransformPoint(c.center+new Vector3(x*c.size.x*.5f,0,z*c.size.z*.5f))));
                cuts.Add(NewCut(Expand(Hull(corners),.55f),"building BoxCollider footprint"));
            }
        }
        // Some imported No.5 colliders are mesh colliders rather than BoxColliders.
        foreach (var c in UnityEngine.Object.FindObjectsByType<MeshCollider>())
            if (c.enabled && IsUnderBuilding(c.transform) && c.bounds.size.y>2 && c.bounds.min.y<1 && ProjectedIntersects(c.bounds,study))
                cuts.Add(NewCut(Rect(c.bounds.min.x-.55f,c.bounds.min.z-.55f,c.bounds.max.x+.55f,c.bounds.max.z+.55f),"imported building collider conservative bounds"));
        foreach (var b in buildings)
            foreach (var t in b.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("Access · ",StringComparison.Ordinal))
                {
                    var p=XZ(t.position); var forward=XZ(t.forward).normalized;
                    var approach=Corridor(p-forward*.6f,p+forward*4.0f,2.0f);
                    if (ProjectedIntersects(BoundsOf(approach),study)) cuts.Add(NewCut(approach,"existing entrance/step clearance"));
                }
        var pilot=UnityEngine.Object.FindAnyObjectByType<PilotBlock>();
        if (pilot && pilot.entrances!=null) foreach (var t in pilot.entrances.Where(t=>t))
        {
            var p=XZ(t.position);var approach=Corridor(p-XZ(t.forward)*3,p+XZ(t.forward)*3,2.2f);
            if(ProjectedIntersects(BoundsOf(approach),study))cuts.Add(NewCut(approach,"No.5 existing landing clearance"));
        }
        return cuts;
    }

    static List<Cut> VehicleCuts(Bounds study)
    {
        var cuts=new List<Cut>();
        foreach (var f in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if(!f.sharedMesh || IsUnderBuilding(f.transform))continue;
            var renderer=f.GetComponent<MeshRenderer>();if(!renderer || !ProjectedIntersects(renderer.bounds,study))continue;
            var vertices=f.sharedMesh.vertices; var mats=renderer.sharedMaterials;
            for(int sub=0;sub<f.sharedMesh.subMeshCount && sub<mats.Length;sub++)
            {
                if(!VehicleMaterial(mats[sub],f.sharedMesh.name))continue;
                var triangles=f.sharedMesh.GetTriangles(sub);
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=f.transform.TransformPoint(vertices[triangles[i]]);var b=f.transform.TransformPoint(vertices[triangles[i+1]]);var c=f.transform.TransformPoint(vertices[triangles[i+2]]);
                    if(Vector3.Cross(b-a,c-a).normalized.y<.8f || Mathf.Max(a.y,Mathf.Max(b.y,c.y))>.4f)continue;
                    var poly=Hull(new[]{XZ(a),XZ(b),XZ(c)});
                    if(poly.Count==3 && Area(poly)>.0001f && ProjectedIntersects(BoundsOf(poly),study))
                        cuts.Add(NewCut(poly,"existing vehicle surface triangle"));
                }
            }
        }
        return cuts;
    }
    static bool VehicleMaterial(Material m,string meshName)
    {
        if(!m)return false; string n=m.name;
        // The existing entrance aprons are pedestrian access, despite their asphalt material.
        if(n.Contains("ResidentialEntrance") || n.Contains("Footpath"))return false;
        return meshName=="ReferenceRoad" || meshName.StartsWith("ParkingSurface_",StringComparison.Ordinal)
            || n.Contains("ServiceAsphalt") || n=="Asphalt" || n=="RoadAsphalt" || n=="ReferenceAsphalt";
    }
    static bool IsVehicleSurface(MeshCollider c)
    {
        var r=c.GetComponent<Renderer>();return r && r.sharedMaterials.Any(m=>VehicleMaterial(m,c.sharedMesh.name));
    }
    static bool IsUnderBuilding(Transform t)
    {
        for(var p=t;p;p=p.parent)
            if(p.GetComponent<StreetBuilding>() || p.name.StartsWith("Дом №5",StringComparison.Ordinal))return true;
        return false;
    }
    static bool IsPhysicalGuard(Collider c)
    {
        if(IsUnderBuilding(c.transform))return true;
        string n=c.name.ToLowerInvariant();
        return c.bounds.max.y>.16f && c.bounds.min.y<1.0f && (n.Contains("ступ")||n.Contains("крыл")||n.Contains("stair")||n.Contains("ramp")||n.Contains("гараж"));
    }
    static bool GuardAt(Vector2 p)
    {
        var centre=At(p,.18f);
        foreach(var c in physicalGuards)
        {
            if(p.x<c.bounds.min.x || p.x>c.bounds.max.x || p.y<c.bounds.min.z || p.y>c.bounds.max.z)continue;
            // ClosestPoint on a non-convex imported building MeshCollider is unsupported.
            // Box footprints were subtracted already; a downward ray also catches low steps.
            if(c is BoxCollider || c is SphereCollider || c is CapsuleCollider || c is MeshCollider mc && mc.convex)
            {
                if((c.ClosestPoint(centre)-centre).sqrMagnitude<.0001f)return true;
            }
            if(c.Raycast(new Ray(At(p,1.4f),Vector3.down),out var hit,2) && hit.point.y>.10f && hit.point.y<1.4f)return true;
        }
        return false;
    }
    static bool VehicleAt(Vector2 p)
    {
        var ray=new Ray(At(p,1),Vector3.down);
        foreach(var s in supports)
        {
            if(!s.asphalt || !s.collider.Raycast(ray,out var hit,2) || hit.point.y>=.4f)continue;
            var renderer=s.collider.GetComponent<Renderer>();if(!renderer)continue;
            var materials=renderer.sharedMaterials;int end=0;
            for(int sub=0;sub<s.collider.sharedMesh.subMeshCount;sub++)
            {
                end+=(int)s.collider.sharedMesh.GetIndexCount(sub)/3;
                if(hit.triangleIndex>=end)continue;
                if(sub<materials.Length && VehicleMaterial(materials[sub],s.collider.sharedMesh.name))return true;
                break;
            }
        }
        return false;
    }
    static float HeightAt(Vector2 p)
    {
        var key=new Vector2Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.y*1000));
        if(heightCache.TryGetValue(key,out var h))return h;
        h=.026f;
        var ray=new Ray(At(p,1),Vector3.down);
        foreach(var s in supports)
            if(s.collider.Raycast(ray,out var hit,2) && hit.normal.y>.82f && hit.point.y>=-.15f && hit.point.y<=.25f)
                h=Mathf.Max(h,hit.point.y+.004f);
        heightCache[key]=h;return h;
    }
    static bool ClearIsland(List<Vector2> poly)
    {
        var b=BoundsOf(poly);
        for(float x=b.min.x;x<=b.max.x;x+=.5f)for(float z=b.min.z;z<=b.max.z;z+=.5f)
        {
            var p=new Vector2(x,z);if(Inside(p,poly) && (GuardAt(p)||VehicleAt(p)))return false;
        }
        for(int i=0;i<poly.Count;i++)
        {
            var a=poly[i];var q=poly[(i+1)%poly.Count];int steps=Mathf.CeilToInt(Vector2.Distance(a,q)/.25f);
            for(int j=0;j<=steps;j++){var p=Vector2.Lerp(a,q,(float)j/Mathf.Max(1,steps));if(GuardAt(p)||VehicleAt(p))return false;}
        }
        return true;
    }
    static void AddIsland(EnvironmentMesh mesh,Island island)
    {
        var outside=island.outer; var inside=island.inner; float top=island.TopHeight,grassHeight=top-.025f;
        var bevel=Inset(outside,.025f);
        if(bevel==null)throw new InvalidOperationException("Invalid island bevel");
        for(int i=0;i<outside.Count;i++)
        {
            int j=(i+1)%outside.Count;var a=outside[i];var b=outside[j];
            Vector3 normal=new Vector3(b.y-a.y,0,a.x-b.x).normalized;
            float bottomA=HeightAt(a)-.12f,bottomB=HeightAt(b)-.12f;
            mesh.Face(brick,normal,At(a,bottomA),At(b,bottomB),At(b,top-.045f),At(a,top-.045f));
            mesh.Face(cap,(normal+Vector3.up).normalized,At(a,top-.045f),At(b,top-.045f),At(bevel[j],top),At(bevel[i],top));
            mesh.Face(cap,Vector3.up,At(bevel[i],top),At(bevel[j],top),At(inside[j],top),At(inside[i],top));
            mesh.Face(brick,-normal,At(inside[i],top),At(inside[j],top),At(inside[j],grassHeight),At(inside[i],grassHeight));
        }
        mesh.Face(grass,Vector3.up,inside.Select(v=>At(v,grassHeight)).ToArray());
        mesh.Face(brick,Vector3.down,outside.Select(v=>At(v,HeightAt(v)-.12f)).ToArray());
    }
    static GameObject Create(EnvironmentMesh mesh,string assetName,string objectName,Transform parent)
    {
        var go=mesh.Create(assetName,objectName,Root);go.transform.SetParent(parent,false);
        go.AddComponent<MeshCollider>().sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccluderStatic);
        return go;
    }
    static int RemoveGrassOverPaving(List<List<Vector2>> pavement)
    {
        int removed=0;
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            var original=filter.sharedMesh;
            if(!original || !original.name.StartsWith("StreetGrass_",StringComparison.Ordinal))continue;
            var renderer=filter.GetComponent<Renderer>();if(!renderer || !ProjectedIntersects(renderer.bounds,BoundsOf(Court)))continue;
            var vertices=original.vertices;var kept=new List<int[]>();int localRemoved=0;
            for(int sub=0;sub<original.subMeshCount;sub++)
            {
                var indices=original.GetTriangles(sub);var next=new List<int>();
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=XZ(filter.transform.TransformPoint(vertices[indices[i]]));
                    var b=XZ(filter.transform.TransformPoint(vertices[indices[i+1]]));
                    var c=XZ(filter.transform.TransformPoint(vertices[indices[i+2]]));
                    // Remove only triangles wholly INSIDE a new paved polygon. Existing
                    // blades outside the selected court, and near its cut boundaries, stay.
                    if(pavement.Any(p=>Inside(a,p) && Inside(b,p) && Inside(c,p)))localRemoved++;
                    else {next.Add(indices[i]);next.Add(indices[i+1]);next.Add(indices[i+2]);}
                }
                kept.Add(next.ToArray());
            }
            if(localRemoved==0)continue;
            var mesh=UnityEngine.Object.Instantiate(original);mesh.name="Stage04Retained_"+original.name;
            for(int sub=0;sub<kept.Count;sub++)mesh.SetTriangles(kept[sub],sub);
            string path=Root+"/Meshes/"+mesh.name+".asset";
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);mesh=saved;}
            else AssetDatabase.CreateAsset(mesh,path);
            Undo.RecordObject(filter,"Remove grass covered by court paving");filter.sharedMesh=mesh;EditorUtility.SetDirty(filter);
            var collider=filter.GetComponent<MeshCollider>();
            if(collider){Undo.RecordObject(collider,"Update retained grass mesh");collider.sharedMesh=mesh;}
            removed+=localRemoved;
        }
        return removed;
    }
    internal static void RemoveCoveredHardscape(MeshFilter[] sources,List<List<Vector2>> cells,string assetRoot=null,
        Predicate<MeshFilter> extraFloor=null)
    {
        var study=BoundsOf(cells.SelectMany(c=>c));var cutBounds=cells.Select(BoundsOf).ToArray();int trimmed=0,changed=0;
        foreach(var filter in sources)
        {
            var renderer=filter.GetComponent<MeshRenderer>();var original=filter.sharedMesh;
            if(!renderer||!ProjectedIntersects(renderer.bounds,study))continue;
            var materials=renderer.sharedMaterials;var oldVertices=original.vertices;var world=oldVertices.Select(filter.transform.TransformPoint).ToArray();
            var oldNormals=original.normals;var oldTangents=original.tangents;var oldColours=original.colors;
            var oldUV=new List<Vector4>();original.GetUVs(0,oldUV);
            var vertices=oldVertices.ToList();var normals=oldNormals.ToList();var tangents=oldTangents.ToList();var colours=oldColours.ToList();var uv=oldUV.ToList();
            var submeshes=new List<int[]>();int localTrim=0;
            for(int sub=0;sub<original.subMeshCount;sub++)
            {
                var indices=original.GetTriangles(sub);var kept=new List<int>();var material=sub<materials.Length?materials[sub]:null;
                bool floor=material&&(material.name.Contains("Asphalt")||material.HasProperty("_SurfaceClass")&&material.GetFloat("_SurfaceClass")>5.5f);
                floor|=extraFloor?.Invoke(filter)==true;
                for(int at=0;at<indices.Length;at+=3)
                {
                    int ia=indices[at],ib=indices[at+1],ic=indices[at+2];var a=world[ia];var b=world[ib];var c=world[ic];
                    var poly=new List<Vector2>{XZ(a),XZ(b),XZ(c)};float area=Area(poly);
                    if(!floor||area<.000001f||Vector3.Cross(b-a,c-a).normalized.y<.82f||Mathf.Max(a.y,Mathf.Max(b.y,c.y))>.26f||!ProjectedIntersects(BoundsOf(poly),study))
                    {kept.Add(ia);kept.Add(ib);kept.Add(ic);continue;}
                    var pieces=new List<List<Vector2>>{Clean(poly)};var triangleBounds=BoundsOf(poly);
                    for(int k=0;k<cells.Count&&pieces.Count>0;k++)if(ProjectedIntersects(triangleBounds,cutBounds[k]))pieces=SubtractAll(pieces,cells[k]);
                    if(Mathf.Abs(pieces.Sum(Area)-area)<.000005f){kept.Add(ia);kept.Add(ib);kept.Add(ic);continue;}
                    localTrim++;
                    int Append(Vector2 p)
                    {
                        var ab=XZ(b-a);var ac=XZ(c-a);var ap=p-XZ(a);float determinant=ab.x*ac.y-ab.y*ac.x;
                        float v=(ap.x*ac.y-ap.y*ac.x)/determinant,w=(ab.x*ap.y-ab.y*ap.x)/determinant,u=1-v-w;
                        int id=vertices.Count;vertices.Add(oldVertices[ia]*u+oldVertices[ib]*v+oldVertices[ic]*w);
                        if(oldNormals.Length==oldVertices.Length)normals.Add((oldNormals[ia]*u+oldNormals[ib]*v+oldNormals[ic]*w).normalized);
                        if(oldTangents.Length==oldVertices.Length)tangents.Add(oldTangents[ia]*u+oldTangents[ib]*v+oldTangents[ic]*w);
                        if(oldColours.Length==oldVertices.Length)colours.Add(oldColours[ia]*u+oldColours[ib]*v+oldColours[ic]*w);
                        if(oldUV.Count==oldVertices.Length)uv.Add(oldUV[ia]*u+oldUV[ib]*v+oldUV[ic]*w);
                        return id;
                    }
                    foreach(var piece in pieces)
                    {
                        if(piece.Count<3||Area(piece)<.0000001f)continue;int first=Append(piece[0]);
                        for(int k=1;k<piece.Count-1;k++)
                        {
                            int p=Append(piece[k]),q=Append(piece[k+1]);
                            if(Vector3.Dot(Vector3.Cross(vertices[p]-vertices[first],vertices[q]-vertices[first]),Vector3.Cross(oldVertices[ib]-oldVertices[ia],oldVertices[ic]-oldVertices[ia]))<0){int swap=p;p=q;q=swap;}
                            kept.Add(first);kept.Add(p);kept.Add(q);
                        }
                    }
                }
                submeshes.Add(kept.ToArray());
            }
            if(localTrim==0)continue;
            var mesh=UnityEngine.Object.Instantiate(original);mesh.name="Stage04CourtRetained_"+original.name;
            if(vertices.Count>65535)mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            if(normals.Count==vertices.Count)mesh.SetNormals(normals);
            if(tangents.Count==vertices.Count)mesh.SetTangents(tangents);
            if(colours.Count==vertices.Count)mesh.SetColors(colours);
            if(uv.Count==vertices.Count)mesh.SetUVs(0,uv);
            for(int sub=0;sub<submeshes.Count;sub++)mesh.SetTriangles(submeshes[sub],sub);mesh.RecalculateBounds();
            string path=(assetRoot??Root)+"/Meshes/CourtRetained_"+GlobalObjectId.GetGlobalObjectIdSlow(filter).targetObjectId+".asset";
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);mesh=saved;}else AssetDatabase.CreateAsset(mesh,path);
            filter.sharedMesh=mesh;var collider=filter.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
            EditorUtility.SetDirty(filter);EditorUtility.SetDirty(collider);trimmed+=localTrim;changed++;
        }
        Debug.Log("PHOTO_STAGE04_COURT oldTopTrianglesClipped="+trimmed+" clonedFloorMeshes="+changed+" no stacked walkable tops");
    }
    static Material Own(string name,string asset,float meters,Color tint,float bump)
    {
        string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Street/TexturedSurface"));AssetDatabase.CreateAsset(m,path);}
        StreetSurfaceLibrary.Apply(m,asset,meters,tint,bump);m.name=name;m.enableInstancing=true;
        m.SetFloat("_RoughnessScale",1);m.SetFloat("_RoughnessBias",.06f);m.SetFloat("_OcclusionStrength",.7f);
        EditorUtility.SetDirty(m);return m;
    }
    static void Materials()
    {
        paving=Own("CourtGreyRectangularPavers","brick_pavement_02",1.8f,new Color(.62f,.64f,.62f),.35f);
        paving.SetFloat("_Colorize",1);paving.SetFloat("_SurfaceClass",6);paving.SetFloat("_PaverPattern",1);
        paving.SetFloat("_PaverGreyFraction",0);paving.SetFloat("_PaverJointMeters",.004f);paving.SetFloat("_Weathering",.13f);paving.SetFloat("_Parallax",.002f);
        brick=Own("DarkRedRetainingBrick","red_bricks_04",1.0f,new Color(.52f,.33f,.30f),.45f);
        brick.SetFloat("_Colorize",.65f);brick.SetFloat("_Weathering",.15f);brick.SetFloat("_Parallax",.004f);
        cap=Own("RedBrownCoping","concrete_wall_006",1.0f,new Color(.32f,.21f,.19f),.25f);
        cap.SetFloat("_Colorize",1);cap.SetFloat("_SurfaceClass",0);cap.SetFloat("_Parallax",.001f);
        grass=Own("CourtMaintainedGrass","grass_ground",2.0f,new Color(.65f,.81f,.51f),.6f);
        grass.SetFloat("_Weathering",.06f);grass.SetFloat("_Parallax",.005f);
    }
    static void AddEvidence(Transform parent,List<Island> islands,int routes,int exclusions)
    {
        var go=new GameObject("EditorOnly · приблизительные контуры и TopHeight, не геодезическая съёмка");
        go.tag="EditorOnly";go.transform.SetParent(parent,false);
        var marker=go.AddComponent<PhotoSurveyMarker>();marker.category="central court raised lawns and grey pavers";
        marker.studyBounds=new Bounds(new Vector3(41,0,37.75f),new Vector3(34.4f,1.4f,18.7f));
        marker.sourceUrls=Sources;marker.geometryProvisional=true;
        marker.observation="Летние фотографии внутреннего двора №7: серое прямоугольное мощение, приподнятые газоны, тёмно-красные подпорные стенки. "+
            JsonUtility.ToJson(new HeightEvidence { TopHeight=islands.Select(i=>i.TopHeight).ToArray(),islandCount=islands.Count,
                protectedRouteSegments=routes,protectedFootprints=exclusions });
        marker.uncertainty="Публикация объявления 2026-09-09; дата съёмки неизвестна. Контуры, ширины и подъём стенок ~0.31 м оценочные. "+
            "TopHeight — Y Unity, не абсолютная высота местности. Положение зданий, лестницы, парковки, маршруты сохранены; общий холм района по этим фото не восстановлен.";
    }

    static Vector2 XZ(Vector3 v)=>new Vector2(v.x,v.z);
    static Vector3 At(Vector2 p,float y)=>new Vector3(p.x,y,p.y);
    static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
    static float Area(IList<Vector2> p)=>Mathf.Abs(SignedArea(p));
    static float SignedArea(IList<Vector2> points)
    {
        if (points.Count < 3) return 0;
        Vector2 origin = points[0];
        float twiceArea = 0;
        for (int i = 1; i + 1 < points.Count; i++)
            twiceArea += Cross(points[i] - origin, points[i + 1] - origin);
        return twiceArea * .5f;
    }
    static Vector2 Centroid(IList<Vector2> p)=>p.Aggregate(Vector2.zero,(a,b)=>a+b)/p.Count;
    static Bounds BoundsOf(IEnumerable<Vector2> points){var p=points.ToArray();var b=new Bounds(At(p[0],0),Vector3.zero);foreach(var v in p)b.Encapsulate(At(v,0));b.Expand(new Vector3(0,4,0));return b;}
    static bool ProjectedIntersects(Bounds a,Bounds b)=>a.max.x>=b.min.x && a.min.x<=b.max.x && a.max.z>=b.min.z && a.min.z<=b.max.z;
    static Cut NewCut(List<Vector2> p,string reason)=>new Cut { polygon=p,bounds=BoundsOf(p),reason=reason };
    static List<Vector2> Rect(float x0,float z0,float x1,float z1)=>new List<Vector2>{new Vector2(x0,z0),new Vector2(x1,z0),new Vector2(x1,z1),new Vector2(x0,z1)};
    static List<Vector2> Chamfer(float x0,float z0,float x1,float z1,float r)=>new List<Vector2>{
        new Vector2(x0+r,z0),new Vector2(x1-r,z0),new Vector2(x1,z0+r),new Vector2(x1,z1-r),
        new Vector2(x1-r,z1),new Vector2(x0+r,z1),new Vector2(x0,z1-r),new Vector2(x0,z0+r)};
    static bool Inside(Vector2 p,IList<Vector2> polygon)
    {
        for(int i=0;i<polygon.Count;i++)if(Cross(polygon[(i+1)%polygon.Count]-polygon[i],p-polygon[i])<-.001f)return false;
        return true;
    }
    static List<Vector2> Clip(List<Vector2> source,Vector2 a,Vector2 b,bool inside=true)
    {
        var result=new List<Vector2>();if(source.Count==0)return result;float side=inside?1:-1;
        Vector2 previous=source[source.Count-1];float dp=Cross(b-a,previous-a)*side;
        foreach(var current in source)
        {
            float dc=Cross(b-a,current-a)*side;
            if((dc>=0)!=(dp>=0))result.Add(Vector2.LerpUnclamped(previous,current,dp/(dp-dc)));
            if(dc>=0)result.Add(current);previous=current;dp=dc;
        }
        return Clean(result);
    }
    static List<Vector2> Intersect(List<Vector2> source,List<Vector2> clip)
    {
        var p=source;for(int i=0;i<clip.Count && p.Count>=3;i++)p=Clip(p,clip[i],clip[(i+1)%clip.Count]);return p;
    }
    static List<List<Vector2>> SubtractAll(List<List<Vector2>> source,List<Vector2> cut)
    {
        var result=new List<List<Vector2>>();var cutBounds=BoundsOf(cut);
        foreach(var poly in source)
        {
            if(!ProjectedIntersects(BoundsOf(poly),cutBounds) || Area(Intersect(poly,cut))<.00001f){result.Add(poly);continue;}
            var remainder=poly;
            for(int i=0;i<cut.Count && remainder.Count>=3;i++)
            {
                var outside=Clip(remainder,cut[i],cut[(i+1)%cut.Count],false);
                if(outside.Count>=3 && Area(outside)>.00001f)result.Add(outside);
                remainder=Clip(remainder,cut[i],cut[(i+1)%cut.Count]);
            }
        }
        return result;
    }
    static List<Vector2> Clean(List<Vector2> p)
    {
        var r=new List<Vector2>();foreach(var v in p)if(r.Count==0 || (v-r[r.Count-1]).sqrMagnitude>.0000001f)r.Add(v);
        if(r.Count>1 && (r[0]-r[r.Count-1]).sqrMagnitude<.0000001f)r.RemoveAt(r.Count-1);
        bool changed=true;
        while(changed && r.Count>3)
        {
            changed=false;for(int i=0;i<r.Count;i++)
            {
                var a=r[(i+r.Count-1)%r.Count];var b=r[i];var c=r[(i+1)%r.Count];
                if(Mathf.Abs(Cross(b-a,c-b))<.00001f){r.RemoveAt(i);changed=true;break;}
            }
        }
        if(r.Count>2 && SignedArea(r)<0)r.Reverse();return r;
    }
    static List<Vector2> Hull(IEnumerable<Vector2> source)
    {
        var sorted=source.Distinct().OrderBy(p=>p.x).ThenBy(p=>p.y).ToArray();if(sorted.Length<3)return sorted.ToList();
        var h=new List<Vector2>();foreach(var p in sorted){while(h.Count>=2 && Cross(h[h.Count-1]-h[h.Count-2],p-h[h.Count-1])<=0)h.RemoveAt(h.Count-1);h.Add(p);}
        int count=h.Count;for(int i=sorted.Length-2;i>=0;i--){var p=sorted[i];while(h.Count>count && Cross(h[h.Count-1]-h[h.Count-2],p-h[h.Count-1])<=0)h.RemoveAt(h.Count-1);h.Add(p);}
        h.RemoveAt(h.Count-1);return Clean(h);
    }
    static List<Vector2> Offset(List<Vector2> poly,float distance)
    {
        var result=new List<Vector2>();
        for(int i=0;i<poly.Count;i++)
        {
            var a=poly[(i+poly.Count-1)%poly.Count];var b=poly[i];var c=poly[(i+1)%poly.Count];
            Vector2 u=(b-a).normalized,v=(c-b).normalized;
            Vector2 nu=new Vector2(-u.y,u.x),nv=new Vector2(-v.y,v.x);
            var sum=(nu+nv).normalized;float denominator=Vector2.Dot(sum,nv);
            if(denominator<.15f)return null;
            result.Add(b+sum*(distance/denominator));
        }
        if(Area(result)<.01f || result.Any(v=>distance>0 && !Inside(v,poly)))return null;
        for(int i=0;i<result.Count;i++)if(Vector2.Distance(result[i],result[(i+1)%result.Count])<.08f)return null;
        return result;
    }
    static List<Vector2> Inset(List<Vector2> p,float d)=>Offset(p,d);
    static List<Vector2> Expand(List<Vector2> p,float d)=>Offset(p,-d)??p;
    static List<Vector2> Corridor(Vector2 a,Vector2 b,float halfWidth)
    {
        var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*halfWidth;
        // Square caps protect endpoints as well as the segment centreline.
        a-=d*halfWidth;b+=d*halfWidth;return Clean(new List<Vector2>{a-n,b-n,b+n,a+n});
    }
    static List<Vector3> Simplify(Vector3[] p,float tolerance)
    {
        var result=new List<Vector3>();
        void Segment(int first,int last)
        {
            float distance=0;int split=-1;var a=XZ(p[first]);var d=XZ(p[last])-a;
            for(int i=first+1;i<last;i++)
            {
                var v=XZ(p[i])-a;float t=d.sqrMagnitude>.0001f?Mathf.Clamp01(Vector2.Dot(v,d)/d.sqrMagnitude):0;
                float error=(v-d*t).sqrMagnitude;if(error>distance){distance=error;split=i;}
            }
            if(split>=0 && distance>tolerance*tolerance){Segment(first,split);Segment(split,last);}else result.Add(p[first]);
        }
        Segment(0,p.Length-1);result.Add(p[p.Length-1]);return result;
    }
}
