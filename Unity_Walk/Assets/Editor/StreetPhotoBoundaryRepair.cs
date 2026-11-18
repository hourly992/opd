using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class StreetPhotoBoundaryRepair
{
    private const string PathRoot = "Assets/Art/StreetRepair";
    internal const string Output = PathRoot + "/PhotoBoundaryCourtyard";
    internal const string NewRootName = "Двор у гаражей · стыки и границы по снимкам";
    private const string CourtRootName = "Двор 5/7 · подъём и два гаражных въезда";
    private const float DeckY = .84f, WingY = .855f, Start = -44f, RoofEnd = -32f;
    private const float InnerWest = 25.36f, OuterWest = 20.44f;
    private const float InnerEast = 28.64f, OuterEast = 33.56f;
    private const string ReportPath = "QA_HDRP/boundary-final";

    [Serializable]
    private sealed class GarageResult
    {
        public int garageEntryMeshesReassigned, trailingVerticesRecessed, backedWings;
        public Vector3 lampPosition;
        public float originalDeckY = DeckY;
        public string savedAsphaltMesh;
        public bool originalAsphaltUnchanged, bothHouseContactsValid;
    }

    [MenuItem("Street/Repair photographed garage courtyard")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Street.unity", OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(x => x.name == NewRootName))
            throw new InvalidOperationException("The scoped courtyard repair is already installed.");
        if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(PathRoot, "PhotoBoundaryCourtyard");
        var root = new GameObject(NewRootName);
        var result = ApplyGarage(scene, root.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the repaired courtyard.");
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(ReportPath);
        File.WriteAllText(ReportPath + "/garage-edit.json", JsonUtility.ToJson(result, true));
        Debug.Log("PHOTO_BOUNDARY_SAVED " + JsonUtility.ToJson(result));
    }


    [MenuItem("Street/Build photographed courtyard Windows game")]
    public static void Build()
    {
        const string path = "Build/StreetWindowsHDRPPhotoCourt/DuninaGorkavicha.exe";
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] {"Assets/Scenes/Street.unity","Assets/Scenes/Street_LeafStudy.unity"},
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = Path.GetFullPath(path)
        });
        if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Player build failed.");
        Debug.Log("PHOTO_BOUNDARY_BUILD " + path);
    }

    internal static void CloseOuterAprons(Transform root)
    {
        var entries = UnityEngine.Object.FindObjectsByType<UndergroundParkingEntrance>()
            .Where(e=>e.name.EndsWith("гаражный въезд",StringComparison.Ordinal)).ToArray();
        if (entries.Length != 2) throw new InvalidOperationException("Expected the two southern garage entrances.");
        var concrete = entries[0].GetComponent<MeshRenderer>().sharedMaterials[1];
        foreach (bool east in new[] {false,true})
        {
            float minX=east?33.32f:18.055f, maxX=east?36.07f:20.68f;
            const float toe=.56f, top=1.395f;
            float front=Start-.012f, back=Start+.17f;
            var form=new MeshForm(1);
            Vector3 P(float x,float y,float z)=>new(x,y,z);
            form.Quad(P(minX,top,front),P(minX,top,back),P(maxX,top,back),P(maxX,top,front),0);
            form.Quad(P(minX,toe,front),P(minX,top,front),P(maxX,top,front),P(maxX,toe,front),0);
            form.Quad(P(maxX,toe,back),P(maxX,top,back),P(minX,top,back),P(minX,toe,back),0);
            form.Quad(P(minX,toe,back),P(minX,top,back),P(minX,top,front),P(minX,toe,front),0);
            form.Quad(P(maxX,toe,front),P(maxX,top,front),P(maxX,top,back),P(maxX,toe,back),0);
            string name=east?"Восточный парапет от обхода":"Западный парапет от обхода";
            var mesh=Persist(form.Build(name),east?"EastApronParapet":"WestApronParapet");
            var node=new GameObject(name);
            node.transform.SetParent(root,false);
            node.AddComponent<MeshFilter>().sharedMesh=mesh;
            node.AddComponent<MeshRenderer>().sharedMaterial=concrete;
            node.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
    }

    private static GarageResult ApplyGarage(Scene scene, Transform root)
    {
        var existing = scene.GetRootGameObjects().Single(x => x.name == CourtRootName);
        var deck = existing.transform.Find("Подъём и асфальтовая площадка");
        var deckFilter = deck.GetComponent<MeshFilter>();
        var deckCollider = deck.GetComponent<MeshCollider>();
        var deckMesh = deckFilter.sharedMesh;
        var asphalt = deck.GetComponent<MeshRenderer>().sharedMaterial;
        string deckPath = AssetDatabase.GetAssetPath(deckMesh);
        string fullDeckPath = Path.GetFullPath(deckPath);
        string expectedAsphalt = Hash(fullDeckPath);
        var entries = existing.GetComponentsInChildren<UndergroundParkingEntrance>();
        if (entries.Length != 2 || deckCollider.sharedMesh != deckMesh)
            throw new InvalidOperationException("Missing reference garage and asphalt assets in the saved scene.");
        var concrete = entries[0].GetComponent<MeshRenderer>().sharedMaterials[1];
        var turf = FindLawn();
        var result = new GarageResult {savedAsphaltMesh=deckPath};

        var original = entries[0].GetComponent<MeshFilter>().sharedMesh;
        Mesh revised = UnityEngine.Object.Instantiate(original);
        revised.name = "Garage pocket walls below flush rear pavement";
        var vertices = revised.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            // The sloped outer apron used to expose the last shutter/wall above
            // the deck. Keep the ramps, open mouths and roof attachments intact.
            if (vertices[i].z <= 18.14f || vertices[i].y <= .808f) continue;
            vertices[i].y = .808f;
            result.trailingVerticesRecessed++;
        }
        if (result.trailingVerticesRecessed == 0) throw new InvalidOperationException("The expected protruding garage wall was not found.");
        revised.vertices = vertices;
        revised.RecalculateNormals();
        revised.RecalculateTangents();
        revised.RecalculateBounds();
        revised = Persist(revised, "GarageRecessedPocketWalls");
        foreach (var entry in entries)
        {
            var filter = entry.GetComponent<MeshFilter>();
            if (filter.sharedMesh != original || entry.floorAndWalls.sharedMesh != original)
                throw new InvalidOperationException("The two garage bodies no longer use the same reference mesh.");
            filter.sharedMesh = revised;
            entry.floorAndWalls.sharedMesh = revised;
            result.garageEntryMeshesReassigned++;
        }

        Physics.SyncTransforms();
        var ground = UnityEngine.Object.FindObjectsByType<MeshCollider>()
            .Where(c => c.enabled && c.gameObject.scene == scene && c.bounds.max.y < .25f &&
                c.bounds.min.y > -.20f && c.bounds.Intersects(new Bounds(new Vector3(27,0,-35),new Vector3(26,2,25)))).ToArray();
        float Ground(float x,float z)
        {
            var ray=new Ray(new Vector3(x,2,z),Vector3.down);
            float y=-.01f;
            foreach (var c in ground)
                if (c.Raycast(ray,out var hit,2.3f) && hit.normal.y>.40f) y=Mathf.Max(y,hit.point.y);
            return y-.035f;
        }

        // At the back of each canopy, carry the upper pavement over the
        // short garage pocket. A level verge on the outside meets the plinth.
        // The space between the two wings is the existing central ascent.
        var west = new Wing(false, -25.98f, 18.055f, 17.15f, OuterWest, InnerWest, 19.46f);
        var east = new Wing(true, -25.39f, 36.07f, 36.95f, OuterEast, InnerEast, 34.53f);
        foreach (var wing in new[] {west,east})
        {
            CreateWing(root,wing,asphalt,turf,concrete,Ground);
            result.backedWings++;
        }
        var lamp = UnityEngine.Object.FindObjectsByType<MeshFilter>().Single(f=>f.sharedMesh &&
            f.sharedMesh.name=="CourtyardLamp" && Mathf.Abs(f.transform.position.x-19.995f)<.05f &&
            Mathf.Abs(f.transform.position.z+34)<.05f).transform;
        lamp.position = new Vector3(lamp.position.x,WingY,lamp.position.z);
        result.lampPosition = lamp.position;
        Physics.SyncTransforms();
        result.bothHouseContactsValid = CheckHouseContact("Дом №5",new Vector3(17.49f,.65f,-25.98f)) &&
            CheckHouseContact("Building_114788164",new Vector3(36.35f,.65f,-25.40f));
        result.originalAsphaltUnchanged = deckFilter.sharedMesh == deckMesh && deckCollider.sharedMesh == deckMesh &&
            Hash(fullDeckPath) == expectedAsphalt;
        if (!result.originalAsphaltUnchanged) throw new InvalidOperationException("The existing central road was modified.");
        return result;
    }

    private static bool CheckHouseContact(string token,Vector3 point)
    {
        foreach (var c in Physics.OverlapSphere(point,.16f))
            for(var t=c.transform;t;t=t.parent)
                if(t.name.Contains(token,StringComparison.Ordinal)) return true;
        return false;
    }

    private static Material FindLawn()
    {
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(PathRoot+"/SouthGarageCourt/Retained_01_CourtRetained_148822049.asset");
        var filter=UnityEngine.Object.FindObjectsByType<MeshFilter>().Single(f=>mesh && f.sharedMesh==mesh);
        var material=filter.GetComponent<MeshRenderer>().sharedMaterials
            .FirstOrDefault(m=>m && (m.name.Contains("Lawn",StringComparison.OrdinalIgnoreCase) ||
                m.name.Contains("Grass",StringComparison.OrdinalIgnoreCase)));
        if (!material) throw new InvalidOperationException("The photographed court lawn material is missing.");
        return material;
    }

    private readonly struct Wing
    {
        public readonly bool east;
        public readonly float rear, outer, tieX, edge, inner, lawnSeam;
        public Wing(bool east,float rear,float outer,float tieX,float edge,float inner,float lawnSeam)
        {
            this.east=east;this.rear=rear;this.outer=outer;this.tieX=tieX;
            this.edge=edge;this.inner=inner;this.lawnSeam=lawnSeam;
        }
    }

    private static void CreateWing(Transform root,Wing wing,Material asphalt,Material lawn,Material concrete,Func<float,float,float> ground)
    {
        string name=wing.east?"Восточная верхняя площадка":"Западная верхняя площадка";
        var form=new MeshForm(3);
        const float shoulderEnd=-29f;
        float zRear=wing.rear;
        Vector3 T(float x,float z) => new(x,WingY,z);
        Vector3 B(float x,float z) => new(x,ground(x,z),z);

        void Top(float x0,float x1,float z0,float z1,int slot)
        {
            float a=Mathf.Min(x0,x1),b=Mathf.Max(x0,x1);
            form.Quad(T(a,z0),T(a,z1),T(b,z1),T(b,z0),slot);
        }
        void Face(Vector3 a,Vector3 b)
        {
            var c=B(b.x,b.z);var d=B(a.x,a.z);
            form.Quad(a,b,c,d,2);
        }
        // High verge and pavement preserve the old centre lane. Split the
        // L-shaped outlines at the canopy tail, where the garage is covered.
        Top(wing.outer,wing.lawnSeam,Start,shoulderEnd,1);
        Top(wing.lawnSeam,wing.edge,Start,RoofEnd,0);
        Top(wing.lawnSeam,wing.inner,RoofEnd,zRear,0);
        var cornerA=T(wing.outer,shoulderEnd);
        var cornerB=T(wing.tieX,zRear);
        if (wing.east)
            form.Quad(T(wing.lawnSeam,shoulderEnd),T(wing.lawnSeam,zRear),cornerB,cornerA,1);
        else
            form.Quad(cornerA,cornerB,T(wing.lawnSeam,zRear),T(wing.lawnSeam,shoulderEnd),1);
        var boundary=new List<Vector3> {
            T(wing.outer,Start),T(wing.edge,Start),T(wing.edge,RoofEnd),T(wing.inner,RoofEnd),
            T(wing.inner,zRear),T(wing.tieX,zRear),T(wing.outer,shoulderEnd)
        };
        if (wing.east) boundary.Reverse();
        for(int i=0;i<boundary.Count;i++)
            Face(boundary[i],boundary[(i+1)%boundary.Count]);

        var mesh=Persist(form.Build(name),wing.east?"EastGarageTerrace":"WestGarageTerrace");
        var node=new GameObject(name);
        node.transform.SetParent(root,false);
        node.AddComponent<MeshFilter>().sharedMesh=mesh;
        node.AddComponent<MeshRenderer>().sharedMaterials=new[]{asphalt,lawn,concrete};
        node.AddComponent<MeshCollider>().sharedMesh=mesh;
    }

    internal static Mesh Persist(Mesh mesh,string file)
    {
        string path=Output+"/"+file+".asset";
        var previous=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(!previous) {AssetDatabase.CreateAsset(mesh,path);return mesh;}
        EditorUtility.CopySerialized(mesh,previous);
        EditorUtility.SetDirty(previous);
        UnityEngine.Object.DestroyImmediate(mesh);
        return previous;
    }

    private static string Hash(string path)
    {
        using var file=File.OpenRead(path);
        using var sha=System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(file));
    }

    private sealed class MeshForm
    {
        private readonly List<Vector3> vertices=new(),normals=new();
        private readonly List<Vector2> uv=new();
        private readonly List<Color> colors=new();
        private readonly List<int>[] triangles;
        public MeshForm(int slots)=>triangles=Enumerable.Range(0,slots).Select(_=>new List<int>()).ToArray();

        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int slot)
        {
            Tri(a,b,c,slot);Tri(a,c,d,slot);
        }
        private void Tri(Vector3 a,Vector3 b,Vector3 c,int slot)
        {
            Vector3 n=Vector3.Cross(b-a,c-a);
            if(n.sqrMagnitude<.00000001f)return;
            n.Normalize();
            foreach(var p in new[]{a,b,c})
            {
                triangles[slot].Add(vertices.Count);
                vertices.Add(p);normals.Add(n);
                uv.Add(n.y>.65f ? new Vector2(p.x,p.z) : new Vector2(p.x+p.z,p.y));
                float t=slot==1 ? Mathf.Lerp(.91f,1f,Mathf.PerlinNoise(p.x*.44f,p.z*.44f)) : 1f;
                colors.Add(new Color(t,t,t,1));
            }
        }
        public Mesh Build(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetColors(colors);
            mesh.subMeshCount=triangles.Length;
            for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i],i);
            mesh.RecalculateTangents();mesh.RecalculateBounds();
            return mesh;
        }
    }
}
