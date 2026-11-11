using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;

public static class StreetBuilder
{
    [Serializable] public class Point { public float x,z; public Vector3 V => new Vector3(x,0,z); }
    [Serializable] public class Road { public long id; public string name,kind; public float width; public bool main; public Point[] points; }
    [Serializable] public class Building { public long id; public string address,street,kind; public int levels; public Point[] points; }
    [Serializable] public class Data { public Road[] roads; public Building[] buildings; }
    private static Data data;
    private static Dictionary<string,Material> materials;
    private static Dictionary<string,Buffer> buffers;
    private static Transform mapRoot;
    private static System.Random random;
    private const string Generated="Assets/StreetGenerated";
    private static Road main;

    private class Buffer
    {
        public List<Vector3> v=new List<Vector3>(), n=new List<Vector3>();
        public List<int> t=new List<int>();
        public bool windows;
        public List<Vector4> uv=new List<Vector4>();
        public void Face(Vector3 normal, params Vector3[] points)
        {
            int start=v.Count;
            v.AddRange(points);
            if(windows)uv.AddRange(EnvironmentMesh.WindowCoordinates(points,normal));
            for(int i=0;i<points.Length;i++) n.Add(normal);
            bool reverse=Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),normal)<0;
            for(int i=1;i<points.Length-1;i++)
            {
                t.Add(start); t.Add(start+(reverse?i+1:i)); t.Add(start+(reverse?i:i+1));
            }
        }
        public Mesh Mesh(string name)
        {
            var m=new Mesh { name=name, indexFormat=IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.tangents=EnvironmentMesh.Tangents(n);if(windows)m.SetUVs(0,uv); m.SetTriangles(t,0); m.RecalculateBounds(); return m;
        }
    }

    [MenuItem("Street/Build saved scene and Windows game")]
    public static void Build() => BuildGeneratedScene();

    [MenuItem("Street/Build saved scene and Linux game")]
    public static void BuildLinux()
    {
        const BuildTarget target = BuildTarget.StandaloneLinux64;
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            throw new InvalidOperationException("Установите Linux Build Support (Mono) для этой версии Unity.");
        if (EditorUserBuildSettings.activeBuildTarget != target)
            throw new InvalidOperationException("Переключите активный Build Profile на Linux или запустите Unity с -buildTarget linux64.");

        BuildSavedScene(target, "Build/StreetLinux", "DuninaGorkavicha.x86_64");
    }

    // Historical method name retained for existing editor callers. This builds the saved
    // scene only; it never runs the archived map generator or reads its source data.
    internal static void BuildGeneratedScene() =>
        BuildSavedScene(BuildTarget.StandaloneWindows64, "Build/StreetWindows", "DuninaGorkavicha.exe");

    private static void BuildSavedScene(BuildTarget target, string outputDirectory, string executable)
    {
        PrepareSavedStreetSceneForBuild();
        PlayerSettings.companyName="OPD";
        PlayerSettings.productName="Дунина-Горкавича — прогулка";
        PlayerSettings.defaultScreenWidth=1600; PlayerSettings.defaultScreenHeight=900;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed; PlayerSettings.resizableWindow=true;
        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Street.unity" },
            target = target,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = Path.Combine(directory, executable),
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Street build for {target} failed: {report.summary.result}");
        Debug.Log("STREET_BUILD_SUCCEEDED " + directory);
    }

    private static void PrepareSavedStreetSceneForBuild()
    {
        const string scenePath="Assets/Scenes/Street.unity";
        if(!File.Exists(scenePath))throw new FileNotFoundException("Сохранённая сцена Street не найдена.",scenePath);
        var active=EditorSceneManager.GetActiveScene();
        if(active.path==scenePath)
        {
            if(active.isDirty&&!EditorSceneManager.SaveScene(active,scenePath))
                throw new IOException("Не удалось сохранить изменения сцены Street перед сборкой.");
        }
        else
        {
            for(int i=0;i<EditorSceneManager.sceneCount;i++)
                if(EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Сборка остановлена: в другой открытой сцене есть несохранённые изменения. Сохраните её перед сборкой Street.");
            EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
        }
        AssetDatabase.SaveAssets();
    }

    // Kept as an explicit failure for old automation rather than silently replacing the
    // hand-edited scene. No menu exposes this obsolete operation.
    public static void Generate()
    {
        throw new InvalidOperationException("Полная генерация улицы из архивных геоданных отключена. Редактируйте существующую Street.unity и используйте Street / Build saved scene and Windows game.");
    }

    private static void GenerateLegacyFromArchivedSource()
    {
        Directory.CreateDirectory(Generated); AssetDatabase.Refresh();
        StreetSurfaceLibrary.Initialize();
        PlayerSettings.colorSpace=ColorSpace.Linear;
        data=JsonUtility.FromJson<Data>(File.ReadAllText("Assets/StreetSource/street.json",Encoding.UTF8));
        main=data.roads.First(r=>r.main); random=new System.Random(735);
        materials=new Dictionary<string,Material>(); buffers=new Dictionary<string,Buffer>();
        Mat("Grass",new Color(.43f,.43f,.355f),4);
        Mat("Asphalt",new Color(.25f,.26f,.265f),2);
        Mat("Path",new Color(.43f,.40f,.33f),3);
        Mat("Pavement",new Color(.43f,.34f,.30f),3);
        Mat("Curb",new Color(.67f,.67f,.63f));
        Mat("White",new Color(.90f,.89f,.81f));
        Mat("RoadPaint",new Color(.84f,.83f,.76f));
        Mat("Brick0",new Color(.64f,.39f,.27f),1);
        Mat("Brick1",new Color(.72f,.63f,.47f),1);
        Mat("Brick2",new Color(.67f,.55f,.43f),1);
        Mat("Roof",new Color(.19f,.28f,.25f),0,.32f,.35f);
        Mat("Glass",new Color(.22f,.34f,.41f),0,.65f,.25f);
        Mat("VehicleGlass",new Color(.22f,.34f,.41f),0,.85f,.1f);
        Mat("Frame",new Color(.78f,.80f,.76f));
        Mat("Foundation",new Color(.38f,.40f,.36f));
        Mat("Metal",new Color(.20f,.24f,.25f),0,.3f,.6f);
        Mat("LampSteel",new Color(.42f,.44f,.45f),0,.48f,.7f);
        Mat("Wood",new Color(.47f,.31f,.17f));
        Mat("Leaf0",new Color(.22f,.34f,.15f),4);
        Mat("Leaf1",new Color(.28f,.40f,.18f),4);
        Mat("Pine",new Color(.16f,.29f,.22f),4);
        Mat("Blue",new Color(.07f,.24f,.51f));
        Mat("Tire",new Color(.07f,.08f,.08f));
        Mat("Car0",new Color(.24f,.33f,.42f),0,.6f,.45f);
        Mat("Car1",new Color(.69f,.67f,.61f),0,.6f,.45f);
        Mat("Car2",new Color(.37f,.19f,.17f),0,.6f,.45f);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        mapRoot=new GameObject("Улица Дунина-Горкавича · географическая основа OSM").transform;
        Box("Grass",new Vector3(-80,-.16f,-80),new Vector3(1550,.3f,1550));
        StreetRoadBuilder.Initialize(data);
        foreach(var road in data.roads) MakeRoad(road);
        ReferenceBuildingBuilder.Initialize(mapRoot,data);
        BackgroundOpeningBuilder.Initialize(mapRoot);
        foreach(var b in data.buildings) if(b.id!=114788180&&!ReferenceBuildingBuilder.TryCreate(b)) MakeBuilding(b);
        var house=ImportHouse();
        SummerVegetationBuilder.Initialize();
        StreetVehicleBuilder.Initialize();
        Decorate();
        PilotBlockBuilder.Generate(mapRoot,house);
        StreetDetailBuilder.Generate(mapRoot);
        Flush();
        BackgroundOpeningBuilder.Flush();
        PlaygroundBuilder.Generate(mapRoot);
        ParkingEnvironmentBuilder.Generate(mapRoot);
        ResidentialAccessBuilder.Generate(mapRoot);
        var accessCheck=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        if(accessCheck.blockedEntrances.Length>0)throw new InvalidOperationException("Blocked entrance approaches: "+string.Join("; ",accessCheck.blockedEntrances));
        GeographicLandscapeBuilder.Generate(mapRoot);
        StreetLandscapeBuilder.Generate(mapRoot);
        MonumentReferenceBuilder.Generate(mapRoot);
        Lighting();
        CreatePlayer();
        ValidateScene();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),"Assets/Scenes/Street.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Street.unity",true)};
        AssetDatabase.SaveAssets();
        Debug.Log("STREET_GENERATED roads="+data.roads.Length+" buildings="+data.buildings.Length);
    }

    private static Material Mat(string key,Color color,float kind=0,float smooth=.08f,float metal=0)
    {
        string path=Generated+"/"+key+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) { m=new Material(Shader.Find("Street/Surface")); AssetDatabase.CreateAsset(m,path); }
        m.shader=Shader.Find("Street/Surface");
        m.SetColor("_Color",color);m.SetFloat("_Kind",kind);m.SetFloat("_Smoothness",smooth);m.SetFloat("_Metallic",metal);
        StreetSurfaceLibrary.ApplyNamed(m,key);
        m.enableInstancing=true;EditorUtility.SetDirty(m);materials[key]=m;return m;
    }
    private static Buffer B(string key) { if(!buffers.ContainsKey(key))buffers[key]=new Buffer { windows=key=="Glass" };return buffers[key]; }
    private static void Box(string key,Vector3 c,Vector3 size,Quaternion q=default(Quaternion))
    {
        if(q==default(Quaternion))q=Quaternion.identity;
        Vector3 x=q*Vector3.right*size.x*.5f,y=q*Vector3.up*size.y*.5f,z=q*Vector3.forward*size.z*.5f;
        var b=B(key);
        b.Face(q*Vector3.up,c-x+y-z,c-x+y+z,c+x+y+z,c+x+y-z);
        b.Face(q*Vector3.down,c-x-y-z,c+x-y-z,c+x-y+z,c-x-y+z);
        b.Face(q*Vector3.right,c+x-y-z,c+x+y-z,c+x+y+z,c+x-y+z);
        b.Face(q*Vector3.left,c-x-y-z,c-x-y+z,c-x+y+z,c-x+y-z);
        b.Face(q*Vector3.forward,c-x-y+z,c+x-y+z,c+x+y+z,c-x+y+z);
        b.Face(q*Vector3.back,c-x-y-z,c-x+y-z,c+x+y-z,c+x-y-z);
    }
    private static void Pole(string key,Vector3 a,Vector3 c,float r,int sides=8,float topRadius=-1)
    {
        if(topRadius<0)topRadius=r;
        Vector3 d=(c-a).normalized, u=Vector3.Cross(d,Vector3.forward).normalized;
        if(u.sqrMagnitude<.1f)u=Vector3.Cross(d,Vector3.up).normalized;
        Vector3 v=Vector3.Cross(d,u).normalized;
        var bottom=new Vector3[sides];var top=new Vector3[sides];
        for(int i=0;i<sides;i++) {
            Vector3 ring=u*Mathf.Cos(i*Mathf.PI*2/sides)+v*Mathf.Sin(i*Mathf.PI*2/sides);
            bottom[i]=a+ring*r;top[i]=c+ring*topRadius;
        }
        B(key).Face(-d,bottom); if(topRadius>0)B(key).Face(d,top);
        for(int i=0;i<sides;i++) {int j=(i+1)%sides;Vector3 n=Vector3.Cross(top[i]-bottom[i],bottom[j]-bottom[i]).normalized;
            Vector3 radial=(bottom[i]+bottom[j])*.5f-a;if(Vector3.Dot(n,radial)<0)n=-n;
            B(key).Face(n,bottom[i],bottom[j],top[j],top[i]);}
    }
    private static void Strip(Vector3[] pts,float left,float right,float height,string key)
    {
        var a=new Vector3[pts.Length];var b=new Vector3[pts.Length];
        for(int i=0;i<pts.Length;i++) {
            Vector3 prev=(pts[i]-pts[Math.Max(0,i-1)]).normalized,next=(pts[Math.Min(pts.Length-1,i+1)]-pts[i]).normalized;
            if(i==0)prev=next;if(i==pts.Length-1)next=prev;
            Vector3 n1=new Vector3(prev.z,0,-prev.x),n2=new Vector3(next.z,0,-next.x);
            Vector3 n=(n1+n2).normalized;float factor=Mathf.Min(2,1/Mathf.Max(.5f,Vector3.Dot(n,n2)));
            a[i]=pts[i]+n*left*factor+Vector3.up*height;b[i]=pts[i]+n*right*factor+Vector3.up*height;
        }
        for(int i=0;i<pts.Length-1;i++)B(key).Face(Vector3.up,a[i],b[i],b[i+1],a[i+1]);
    }
    private static void MakeRoad(Road road)
    {
        if(road.main) { StreetRoadBuilder.Generate(mapRoot); return; }
        var p=road.points.Select(x=>x.V).ToArray();
        bool path=road.kind=="footway"||road.kind=="path"||road.kind=="pedestrian";
        if(path) {
            for(int i=1;i<p.Length;i++) {
                if(SharesMainSidewalk(p[i-1],p[i]))continue;
                int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(p[i-1],p[i])/.5f));
                for(int part=0;part<steps;part++) {
                    Vector3 from=Vector3.Lerp(p[i-1],p[i],(float)part/steps),to=Vector3.Lerp(p[i-1],p[i],(float)(part+1)/steps);
                    if(StreetRoadBuilder.IsPaved((from+to)*.5f))continue;
                    Strip(new[]{from,to},-road.width*.5f,road.width*.5f,.09f,"Path");
                }
            }
        }
        else Strip(p,-road.width*.5f,road.width*.5f,.035f,"Asphalt");
    }
    private static bool SharesMainSidewalk(Vector3 a,Vector3 b)
    {
        Vector3 p=(a+b)*.5f;
        Vector3 direction=(b-a).normalized;
        for(int i=1;i<main.points.Length;i++) {
            Vector3 from=main.points[i-1].V,d=main.points[i].V-from;
            float t=Mathf.Clamp01(Vector3.Dot(p-from,d)/d.sqrMagnitude);
            if(Vector3.Distance(p,from+d*t)<8&&Mathf.Abs(Vector3.Dot(direction,d.normalized))>.95f)return true;
        }
        return false;
    }
    private static float Length(Vector3[] p) {float d=0;for(int i=1;i<p.Length;i++)d+=Vector3.Distance(p[i-1],p[i]);return d;}
    private static void Sample(Vector3[] p,float d,out Vector3 pos,out Vector3 tangent)
    {
        for(int i=1;i<p.Length;i++) {float l=Vector3.Distance(p[i-1],p[i]);if(d<=l||i==p.Length-1) {tangent=(p[i]-p[i-1]).normalized;pos=Vector3.Lerp(p[i-1],p[i],Mathf.Clamp01(d/l));return;}d-=l;}
        pos=p[0];tangent=Vector3.forward;
    }
    private static float NearRoad(Vector3 pos)
    {
        float closest=10000;
        foreach(var r in data.roads)for(int i=1;i<r.points.Length;i++) {
            Vector3 a=r.points[i-1].V,d=r.points[i].V-a;
            float t=Mathf.Clamp01(Vector3.Dot(pos-a,d)/Mathf.Max(.001f,d.sqrMagnitude));
            closest=Mathf.Min(closest,Vector3.Distance(pos,a+d*t)-r.width*.5f);
        }
        return closest;
    }
    private static bool InBuilding(Vector3 pos)
    {
        foreach(var building in data.buildings) {
            bool inside=false;var p=building.points;
            for(int i=0,j=p.Length-1;i<p.Length;j=i++)
                if((p[i].z>pos.z)!=(p[j].z>pos.z) && pos.x<(p[j].x-p[i].x)*(pos.z-p[i].z)/(p[j].z-p[i].z)+p[i].x)inside=!inside;
            if(inside)return true;
        }return false;
    }

    private static void MakeBuilding(Building building)
    {
        var p=building.points.Select(v=>v.V).ToArray();
        float area=0;for(int i=0;i<p.Length;i++){var a=p[i];var b=p[(i+1)%p.Length];area+=a.x*b.z-b.x*a.z;}
        if(Mathf.Abs(area)<10)return;
        bool garage=building.kind.Contains("garage")||building.kind=="shed"||building.kind=="roof";
        int floors=garage?1:building.levels;float height=garage?3:1+floors*3.1f;
        BackgroundOpeningBuilder.Append(building,p,height,floors,materials["Brick"+(int)(building.id%3)],
            materials["Foundation"],materials["Frame"],materials["Glass"],materials["Metal"],materials["Roof"],garage);
    }
    internal static List<int[]> Triangulate(Vector3[] p)
    {
        float area=0;for(int i=0;i<p.Length;i++)area+=p[i].x*p[(i+1)%p.Length].z-p[(i+1)%p.Length].x*p[i].z;
        var ids=Enumerable.Range(0,p.Length).ToList();if(area<0)ids.Reverse();var result=new List<int[]>();
        float Cross(Vector3 a,Vector3 b,Vector3 c)=>(b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
        for(int guard=0;ids.Count>2&&guard<p.Length*p.Length;guard++) {
            bool found=false;
            for(int i=0;i<ids.Count;i++) {
                int a=ids[(i+ids.Count-1)%ids.Count],b=ids[i],c=ids[(i+1)%ids.Count];
                if(Cross(p[a],p[b],p[c])<=.0001f)continue;
                bool contains=ids.Any(k=>k!=a&&k!=b&&k!=c&&Cross(p[a],p[b],p[k])>=0&&Cross(p[b],p[c],p[k])>=0&&Cross(p[c],p[a],p[k])>=0);
                if(contains)continue;result.Add(new[]{a,b,c});ids.RemoveAt(i);found=true;break;
            }if(!found)break;
        }return result;
    }

    private static GameObject ImportHouse()
    {
        var root=new GameObject("Дом №5 · подробная модель по фотографиям");root.transform.SetParent(mapRoot);
        using(var reader=new BinaryReader(File.OpenRead("Assets/StreetSource/house5.bytes"))) {
            reader.ReadInt32();int count=reader.ReadInt32();
            for(int i=0;i<count;i++) {
                string name=Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                Color color=new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                int nv=reader.ReadInt32(),nt=reader.ReadInt32();
                var vertices=new Vector3[nv];var normals=new Vector3[nv];var indices=new int[nt];
                for(int j=0;j<nv;j++){vertices[j]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());normals[j]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());}
                for(int j=0;j<nt;j++)indices[j]=reader.ReadInt32();
                House5OpeningRepair.Repair(name,ref vertices,ref normals,ref indices);
                bool roof=name.StartsWith("Кровля",StringComparison.Ordinal);
                if(roof) {
                    // Blender's open roof sheets can face either way. Explicit back faces give
                    // Standard lighting the correct normals on both sides, without changing the source.
                    vertices=vertices.Concat(vertices).ToArray();
                    normals=normals.Concat(normals.Select(n=>-n)).ToArray();
                    var twoSided=new int[nt*2];Array.Copy(indices,twoSided,nt);
                    for(int j=0;j<nt;j+=3){twoSided[nt+j]=indices[j]+nv;twoSided[nt+j+1]=indices[j+2]+nv;twoSided[nt+j+2]=indices[j+1]+nv;}
                    indices=twoSided;
                }
                var mesh=new Mesh { name=name,indexFormat=IndexFormat.UInt32,vertices=vertices,normals=normals,tangents=EnvironmentMesh.Tangents(normals),triangles=indices };mesh.RecalculateBounds();
                if(name.Contains("Стекло"))EnvironmentMesh.MapWindowPlanes(mesh);
                mesh=SaveMesh(mesh,"House5_"+i);
                bool glass=name.Contains("Стекло");float kind=name.Contains("Кирпич")?1:glass?9:0;
                // Blender node colours in the binary export are linear; Unity colour properties
                // are authored in sRGB. Preserve source reflectance in the linear renderer.
                var material=Mat("House5_"+i,color.gamma,kind,glass?.87f:roof?.30f:.14f,glass?.32f:roof?.35f:0);
                StreetSurfaceLibrary.ApplyHouse(material,name);
                if(material.HasProperty("_Cull"))material.SetFloat("_Cull",glass?0:2);
                var part=new GameObject(name);part.transform.SetParent(root.transform);part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterial=material;
            }
        }
        // Matching stepped body volumes; the outside remains walkable around the entrances.
        foreach(var info in new[]{new Vector3(-6.825f,6f,13f),new Vector3(6.825f,9f,-13f)}) {
            var c=root.AddComponent<BoxCollider>();c.center=info;c.size=new Vector3(22.5f,info.y*2,26f);
        }
        var connector=root.AddComponent<BoxCollider>();connector.center=new Vector3(-11.25f,5.575f,.8f);connector.size=new Vector3(13.65f,11.15f,1.6f);
        PilotBlockBuilder.PrepareHouse(root);
        return root;
    }

    private static void Decorate()
    {
        var points=main.points.Select(v=>v.V).ToArray();float length=Length(points);
        for(float d=12;d<length-12;d+=26) {
            Sample(points,d,out var pos,out var t);Vector3 n=new Vector3(t.z,0,-t.x);
            Vector3 pole=pos+n*((((int)(d/26))%2==0?1:-1)*4.05f);
            Pole("LampSteel",pole+Vector3.up*.18f,pole+Vector3.up*7.8f,.09f,12,.055f);
            foreach(int side in new[]{-1,1}) {
                Vector3 a=pole+Vector3.up*7.45f,b=pole+Vector3.up*7.90f+t*(side*.35f),
                    c=pole+Vector3.up*8.10f+t*(side*.85f),end=pole+Vector3.up*8.13f+t*(side*1.4f);
                Pole("LampSteel",a,b,.045f,10,.039f);Pole("LampSteel",b,c,.039f,10,.033f);Pole("LampSteel",c,end,.033f,10,.028f);
                Box("White",end,new Vector3(.25f,.10f,.60f),Quaternion.LookRotation(t));
            }
        }
        // Woods are placed by GeographicLandscapeBuilder inside original OSM boundaries.
        // The former roadside sequence and random city-wide trees had no geographic evidence.
        // Parking polygons and vehicles are reconstructed from OSM/photo evidence after roads.
        for(float d=235;d<600;d+=210) {
            Sample(points,d,out var p,out var t);Vector3 n=new Vector3(t.z,0,-t.x);var at=p+n*6f;
            Sign(at+Vector3.up*2.5f,-n,"ул. Дунина-Горкавича",3.9f,.63f);
            Pole("Metal",at,at+Vector3.up*2.65f,.055f);
            // Bench with slatted seat and back beside the sidewalk.
            var q=Quaternion.LookRotation(t);
            for(int j=0;j<4;j++)Box("Wood",at+t*4+Vector3.up*.58f+n*(j*.115f),new Vector3(.1f,.075f,1.7f),q);
            for(int j=0;j<3;j++)Box("Wood",at+t*4+Vector3.up*(.85f+j*.13f)+n*.48f,new Vector3(.07f,.10f,1.7f),q);
            for(int j=-1;j<=1;j+=2)Box("Metal",at+t*(4+j*.65f)+Vector3.up*.32f+n*.22f,new Vector3(.46f,.55f,.08f),q);
        }
    }
    private static bool InPilot(Vector3 p) => p.x>-38&&p.x<33&&p.z>-49&&p.z<31;
    internal static IEnumerable<(Vector3 position,Vector3 forward,float distance)> FocalRoadSamples(float step,bool wholeStreet=false)
    {
        var points=main.points.Select(v=>v.V).ToArray();float length=Length(points);
        for(float d=8;d<length-8;d+=step) {
            Sample(points,d,out var position,out var forward);
            if(wholeStreet||position.sqrMagnitude<145*145&&position.z>-115&&position.z<70)yield return(position,forward,d);
        }
    }
    internal static bool CanPlant(Vector3 p,float roadClearance)
    {
        if(PlaygroundBuilder.Contains(p,roadClearance))return false;
        if(StreetRoadBuilder.IsPaved(p))return false;
        if(InBuilding(p))return false;
        foreach(var road in data.roads)for(int i=1;i<road.points.Length;i++) {
            Vector3 a=road.points[i-1].V,b=road.points[i].V,d=b-a;
            bool path=road.kind=="footway"||road.kind=="path"||road.kind=="pedestrian";
            if(path&&SharesMainSidewalk(a,b))continue;
            var nearest=a+d*Mathf.Clamp01(Vector3.Dot(p-a,d)/Mathf.Max(.001f,d.sqrMagnitude));
            if(Vector3.Distance(p,nearest)-road.width*.5f<roadClearance)return false;
        }
        foreach(var building in data.buildings)for(int i=0;i<building.points.Length;i++) {
            Vector3 a=building.points[i].V,b=building.points[(i+1)%building.points.Length].V,d=b-a;
            var nearest=a+d*Mathf.Clamp01(Vector3.Dot(p-a,d)/Mathf.Max(.001f,d.sqrMagnitude));
            if(Vector3.Distance(p,nearest)<2.1f)return false;
        }
        return true;
    }
    private static void Sign(Vector3 position,Vector3 towardRoad,string text,float width,float height)
    {
        var root=new GameObject("Указатель: "+text);root.transform.SetParent(mapRoot);root.transform.position=position;
        root.transform.rotation=Quaternion.LookRotation(-towardRoad);
        Box("Blue",position,new Vector3(width,height,.08f),root.transform.rotation);
        var letters=new GameObject("Надпись");letters.transform.SetParent(root.transform,false);letters.transform.localPosition=new Vector3(0,0,-.052f);
        var label=letters.AddComponent<TextMesh>();label.text=text;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;
        label.font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/PTSans.ttf");label.fontSize=70;label.characterSize=.045f;
        label.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;
    }

    private static Mesh SaveMesh(Mesh mesh,string name)
    {
        mesh.name=name;
        string path=Generated+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);return existing;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    private static void Flush()
    {
        foreach(var pair in buffers) {
            if(pair.Value.v.Count==0)continue;
            var mesh=SaveMesh(pair.Value.Mesh(pair.Key),"Map_"+pair.Key);
            var o=new GameObject(pair.Key);o.transform.SetParent(mapRoot);
            o.AddComponent<MeshFilter>().sharedMesh=mesh;o.AddComponent<MeshRenderer>().sharedMaterial=materials[pair.Key];
            if(pair.Key=="Grass") {var collider=o.AddComponent<BoxCollider>();collider.center=mesh.bounds.center;collider.size=mesh.bounds.size;}
            else if(new[]{"Asphalt","Pavement","Path","Brick0","Brick1","Brick2"}.Contains(pair.Key))o.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
    }
    private static void Lighting()
    {
        var sun=new GameObject("Летнее солнце · тёплый направленный свет").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.35f;sun.color=new Color(1,.98f,.95f);
        sun.transform.rotation=Quaternion.Euler(38,-32,0);sun.shadows=LightShadows.Soft;sun.shadowStrength=1;
        // Verified with the surface shadow diagnostic: lower values produced concentric self-shadow acne.
        sun.shadowBias=.065f;sun.shadowNormalBias=.25f;sun.shadowCustomResolution=4096;sun.shadowNearPlane=.3f;
        RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Trilight;
        // Daylight reaches north-facing walls and recessed entrances from the whole sky.
        // Preserve sun shadows while keeping masonry, glass and door profiles readable inside them.
        RenderSettings.ambientSkyColor=new Color(.58f,.64f,.70f);RenderSettings.ambientEquatorColor=new Color(.47f,.51f,.55f);RenderSettings.ambientGroundColor=new Color(.31f,.33f,.28f);
        RenderSettings.ambientIntensity=1;RenderSettings.reflectionIntensity=.9f;
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.00085f;RenderSettings.fogColor=new Color(.67f,.76f,.85f);
        string path=Generated+"/Sky.mat";var sky=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!sky){sky=new Material(Shader.Find("Skybox/Panoramic"));AssetDatabase.CreateAsset(sky,path);}
        sky.shader=Shader.Find("Skybox/Panoramic");RenderSettings.skybox=sky;
        sky.SetTexture("_MainTex",StreetSurfaceLibrary.Texture("kloofendal_48d_partly_cloudy_puresky","Sky","hdr"));
        sky.SetFloat("_Exposure",.82f);sky.SetFloat("_Rotation",110);sky.SetColor("_Tint",new Color(.5f,.5f,.5f));
        var skyProbe=new GameObject("Отражения неба · вся карта").AddComponent<ReflectionProbe>();
        skyProbe.transform.position=new Vector3(-40,20,-80);skyProbe.size=new Vector3(1600,400,1600);skyProbe.resolution=128;
        skyProbe.mode=ReflectionProbeMode.Realtime;skyProbe.refreshMode=ReflectionProbeRefreshMode.OnAwake;
        skyProbe.timeSlicingMode=ReflectionProbeTimeSlicingMode.IndividualFaces;skyProbe.cullingMask=0;skyProbe.importance=0;
        var points=main.points.Select(p=>p.V).ToArray();float length=Length(points);
        for(int i=0;i<4;i++) {
            Sample(points,length*(i+.5f)/4,out var p,out var tangent);
            var probe=new GameObject("Локальные отражения улицы · "+i).AddComponent<ReflectionProbe>();
            probe.transform.position=p+Vector3.up*5;probe.size=new Vector3(175,50,200);probe.resolution=128;
            probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.IndividualFaces;probe.boxProjection=true;probe.importance=1;probe.blendDistance=18;
        }
        QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowDistance=240;QualitySettings.shadowResolution=ShadowResolution.VeryHigh;
        QualitySettings.shadowProjection=ShadowProjection.StableFit;
        QualitySettings.shadowCascades=4;
        QualitySettings.shadowCascade4Split=new Vector3(.045f,.145f,.38f);
        QualitySettings.antiAliasing=4;QualitySettings.vSyncCount=1;
        QualitySettings.anisotropicFiltering=AnisotropicFiltering.ForceEnable;
        QualitySettings.lodBias=1.35f;
    }
    private static void CreatePlayer()
    {
        var player=new GameObject("Игрок · прогулка от первого лица");player.tag="Player";
        player.transform.position=new Vector3(-29.6f,1.25f,13);
        player.transform.rotation=Quaternion.LookRotation(new Vector3(1,0,-.06f));
        var capsule=player.AddComponent<CharacterController>();capsule.height=1.8f;capsule.radius=.30f;capsule.stepOffset=.3f;
        var view=new GameObject("Main Camera");view.tag="MainCamera";view.transform.SetParent(player.transform,false);view.transform.localPosition=Vector3.up*.72f;
        var camera=view.AddComponent<Camera>();camera.fieldOfView=55;camera.nearClipPlane=.07f;camera.farClipPlane=1500;
        camera.allowHDR=true;
        var presentation=view.AddComponent<StreetPresentation>();
        presentation.shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/StreetPresentation.shader");
        presentation.occlusionShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/StreetOcclusion.shader");
        presentation.sunShaftShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/StreetSunShafts.shader");
        presentation.sunShafts=.42f;
        presentation.exposure=1.02f;presentation.saturation=.96f;
        presentation.contactShadows=.45f;presentation.contactRadius=.45f;
        presentation.bloom=.10f;presentation.sharpness=.12f;
        view.AddComponent<AudioListener>();player.AddComponent<FirstPersonWalk>().SetCamera(view.transform);
        player.AddComponent<StreetSession>().font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/PTSans.ttf");
    }
    private static void ValidateScene()
    {
        foreach(var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
            foreach(var material in renderer.sharedMaterials)
                if(!material||!material.shader||ShaderUtil.ShaderHasError(material.shader))throw new Exception("Missing or invalid material: "+renderer.name);
        if(!GameObject.FindGameObjectWithTag("Player"))throw new Exception("No player");
        var presentation=UnityEngine.Object.FindAnyObjectByType<StreetPresentation>();
        if(!presentation||!presentation.shader||ShaderUtil.ShaderHasError(presentation.shader))throw new Exception("Invalid presentation shader");
        if(!presentation.occlusionShader||ShaderUtil.ShaderHasError(presentation.occlusionShader))throw new Exception("Invalid contact shadow shader");
        if(!presentation.sunShaftShader||ShaderUtil.ShaderHasError(presentation.sunShaftShader))throw new Exception("Invalid sun shaft shader");
        if(!RenderSettings.skybox||ShaderUtil.ShaderHasError(RenderSettings.skybox.shader))throw new Exception("Invalid sky shader");
        ReferenceBuildingBuilder.Validate();
        var roads=UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
        if(!roads||roads.profileSections!=7||roads.mappedCrossings!=4||roads.surfaces.Length!=3||roads.surfaces.Any(c=>!c||!c.sharedMesh))
            throw new Exception("Incomplete reference road network");
        var block=UnityEngine.Object.FindAnyObjectByType<PilotBlock>();
        if(!block||block.entrances.Length!=5||block.containers.Length!=3||block.inspectionViews.Length!=4)
            throw new Exception("Incomplete pilot block references");
    }
}
