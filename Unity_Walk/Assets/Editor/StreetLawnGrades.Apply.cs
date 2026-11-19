using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class StreetLawnGrades
{
    private const int TileMetres = 64;
    private const float RaisedHeight = .216f;
    private static readonly Vector2[] Court = {
        new(-7,-45), new(16,-45), new(16,-56), new(37,-56), new(37,-24.6f), new(24.6f,-24.6f),
        new(24.6f,4.2f), new(10.8f,4.2f), new(10.8f,30.2f), new(-7,30.2f)
    };

    [Serializable]
    private sealed class EditReport
    {
        public int tiles, lawnTriangles, curbTriangles, repaintedRenderers, detailTiles, bakedGrassMeshes, translatedGrassMeshes, kerbCells;
        public int checkedLawnTops, collisionErrors;
        public float maxGroundError;
        public Vector3 entryBottom, entryTop;
    }

    private sealed class SurfaceMesh
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<int> indices = new();
        private readonly List<Vector2> texcoords = new();
        private readonly Dictionary<Vector3, int> points = new();

        internal void Triangle(Vector3 a, Vector3 b, Vector3 c, bool up)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-11f) return;
            if (up && n.y < 0) (b,c) = (c,b);
            indices.Add(Vertex(a, up)); indices.Add(Vertex(b, up)); indices.Add(Vertex(c, up));
        }

        // Side UVs repeat the concrete texture along the edge and up the face.
        private int Vertex(Vector3 p, bool up)
        {
            if (up && points.TryGetValue(p, out int index)) return index;
            int vertex = vertices.Count;
            vertices.Add(p);
            texcoords.Add(up ? new Vector2(p.x, p.z) : new Vector2(p.x + p.z, p.y));
            if (up) points.Add(p, vertex);
            return vertex;
        }

        internal Mesh Finish(string name)
        {
            if (indices.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, texcoords);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    private sealed class TiledGround
    {
        internal readonly Dictionary<Vector2Int, MeshCollider> Lawns = new(), Kerbs = new();
        internal readonly MeshCollider Original;

        internal TiledGround(MeshCollider original) => Original = original;

        internal static Vector2Int Key(float x, float z) => new(Mathf.FloorToInt(x / TileMetres), Mathf.FloorToInt(z / TileMetres));

        internal static bool Sample(MeshCollider collider, float x, float z, out float height)
        {
            if (collider.Raycast(new Ray(new Vector3(x, 2.8f, z), Vector3.down), out var hit, 3.7f) && hit.normal.y > .4f)
            {
                height = hit.point.y;
                return true;
            }
            height = 0f;
            return false;
        }

        internal bool Lawn(float x, float z, out float y)
        {
            if (Lawns.TryGetValue(Key(x,z), out var collider) && Sample(collider,x,z,out y)) return true;
            y = 0;
            return false;
        }

        internal bool Kerb(float x, float z)
        {
            return Kerbs.TryGetValue(Key(x,z), out var collider) && Sample(collider,x,z,out _);
        }

        internal float Old(float x, float z) => Sample(Original,x,z,out float y) ? y : -.01f;
    }

    private static float CourtDistance(Vector2 point)
    {
        float distance = float.PositiveInfinity;
        for (int i = 0; i < Court.Length; i++)
        {
            Vector2 a = Court[i], delta = Court[(i + 1) % Court.Length] - a;
            float along = Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
            distance = Mathf.Min(distance, (point - a - delta * along).sqrMagnitude);
        }
        return Mathf.Sqrt(distance);
    }

    private static MeshCollider Install(Mesh mesh, Material material, Transform root, string name, string assetName)
    {
        if (!mesh) return null;
        var node = new GameObject(name);
        node.transform.SetParent(root, false);
        mesh = Save(mesh, assetName);
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        node.AddComponent<MeshRenderer>().sharedMaterial = material;
        var collider = node.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        return collider;
    }

    private static Mesh Save(Mesh mesh, string assetName)
    {
        string path = $"{AssetsPath}/{assetName}.asset";
        var previous = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!previous)
        {
            AssetDatabase.CreateAsset(mesh,path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh,previous);
        DestroyMesh(mesh);
        EditorUtility.SetDirty(previous);
        return previous;
    }

    private static void DestroyMesh(Mesh mesh) => UnityEngine.Object.DestroyImmediate(mesh);

    [MenuItem("Street/Ground/Apply district lawn grades")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(x => x.name == RootName))
            throw new InvalidOperationException("The saved scene already contains the district lawn update.");
        if (!AssetDatabase.IsValidFolder(AssetsPath))
            AssetDatabase.CreateFolder("Assets/Art/StreetRepair", "LawnGrades");
        var turf = AssetDatabase.LoadAssetAtPath<Material>(TurfPath);
        var concrete = AssetDatabase.LoadAssetAtPath<Material>(KerbPath);
        if (!turf || !concrete) throw new InvalidOperationException("The saved grass and white kerb materials are required.");
        var terrain = new TiledGround(Ground());
        var root = new GameObject(RootName);
        var stats = new EditReport();
        var probes = new List<(MeshCollider collider, Vector3 point)>();
        var gradeHeights = new Dictionary<Vector2, float>();
        Physics.SyncTransforms();

        float Grade(float x,float z)
        {
            var key = new Vector2(x,z);
            if (gradeHeights.TryGetValue(key, out float y)) return y;
            float old = terrain.Old(x,z);
            float level = Mathf.Max(RaisedHeight, old + .115f);
            // The district-wide raise ends gradually at the preserved yard.
            if (x >= -8.5f && x <= 38.5f && z >= -57.5f && z <= 31.7f)
            {
                float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01(CourtDistance(key) / 1.3f));
                level = Mathf.Lerp(old + .001f, level, blend);
            }
            gradeHeights.Add(key,level);
            return level;
        }

        using (var reader = new BinaryReader(File.OpenRead(Work + "/graded-footprints.bin")))
        {
            if (reader.ReadInt32() != 1) throw new InvalidDataException("Invalid lawn footprint version.");
            int tileCount = reader.ReadInt32();
            Vector3 Position()
            {
                float x = reader.ReadSingle(), z = reader.ReadSingle();
                return new Vector3(x, Grade(x,z), z);
            }
            for (int tile = 0; tile < tileCount; tile++)
            {
                var key = new Vector2Int(reader.ReadInt32(), reader.ReadInt32());
                int grassCount = reader.ReadInt32(), curbCount = reader.ReadInt32(), faceCount = reader.ReadInt32();
                var lawn = new SurfaceMesh();
                var curb = new SurfaceMesh();
                Vector3 probe = default;
                for (int i = 0; i < grassCount; i++)
                {
                    var a = Position(); var b = Position(); var c = Position();
                    lawn.Triangle(a,b,c,true);
                    if (i == grassCount / 2) probe = (a + b + c) / 3f;
                }
                for (int i = 0; i < curbCount; i++) curb.Triangle(Position(),Position(),Position(),true);
                for (int i = 0; i < faceCount; i++)
                {
                    var a = Position(); var b = Position();
                    var baseA = new Vector3(a.x,terrain.Old(a.x,a.z) - .06f,a.z);
                    var baseB = new Vector3(b.x,terrain.Old(b.x,b.z) - .06f,b.z);
                    curb.Triangle(a,baseA,baseB,false);
                    curb.Triangle(a,baseB,b,false);
                    curb.Triangle(a,baseB,baseA,false);
                    curb.Triangle(a,b,baseB,false);
                }
                string label = $"{key.x}_{key.y}";
                var grass = Install(lawn.Finish("Elevated lawn " + label),turf,root.transform,"Газон " + label,"Lawn_" + label);
                if (grass)
                {
                    terrain.Lawns.Add(key,grass);
                    probes.Add((grass,probe));
                }
                var concreteEdge = Install(curb.Finish("White curb " + label),concrete,root.transform,"Белый бордюр " + label,"Kerb_" + label);
                if (concreteEdge) terrain.Kerbs.Add(key,concreteEdge);
                stats.tiles++;
                stats.lawnTriangles += grassCount;
                stats.curbTriangles += curbCount;
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length || stats.tiles < 20 || stats.curbTriangles == 0)
                throw new InvalidDataException("The map-wide footprint data is incomplete.");
        }

        Physics.SyncTransforms();
        foreach (var (collider,point) in probes)
        {
            if (!TiledGround.Sample(collider,point.x,point.z,out float y))
            {
                stats.collisionErrors++;
                continue;
            }
            stats.checkedLawnTops++;
            stats.maxGroundError = Mathf.Max(stats.maxGroundError,Mathf.Abs(point.y - y));
        }
        if (stats.collisionErrors > 4 || stats.maxGroundError > .025f)
            throw new InvalidOperationException("The new lawn colliders do not match the mesh tops.");

        RepaintKerbs(concrete,stats);
        LiftNativeGrass(terrain,stats);
        LiftBakedGrass(terrain,stats);
        ConnectHouse5Walk(root.transform,stats);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the lawn and entrance scene.");
        File.WriteAllText(Work + "/grades-edit.json",JsonUtility.ToJson(stats,true));
        Debug.Log("LAWN_GRADES_SAVED " + JsonUtility.ToJson(stats));
    }

    private static void RepaintKerbs(Material ceramic, EditReport stats)
    {
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == ceramic || !IsKerb(materials[i])) continue;
                materials[i] = ceramic;
                changed = true;
            }
            if (!changed) continue;
            renderer.sharedMaterials = materials;
            stats.repaintedRenderers++;
        }
    }

    private static void LiftNativeGrass(TiledGround terrain, EditReport stats)
    {
        foreach (var tile in UnityEngine.Object.FindObjectsByType<Terrain>())
        {
            var source = tile.terrainData;
            if (!source || tile.drawHeightmap || tile.GetComponent<TerrainCollider>() || source.detailPrototypes.Length == 0)
                continue;
            var position = tile.transform.position;
            float stepX = source.heightmapScale.x, stepZ = source.heightmapScale.z;
            int resolution = source.heightmapResolution;
            float[,] grade = source.GetHeights(0,0,resolution,resolution);
            bool changed = false;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                if (!terrain.Lawn(position.x + x * stepX,position.z + z * stepZ,out float y)) continue;
                float normalized = Mathf.Clamp01((y - .006f - position.y) / source.size.y);
                if (normalized <= grade[z,x]) continue;
                grade[z,x] = normalized;
                changed = true;
            }
            if (!changed) continue;
            string path = $"{AssetsPath}/Grass_{tile.name}.asset";
            if (!File.Exists(path) && !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),path))
                throw new IOException("Could not preserve grass detail data.");
            var revised = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            revised.SetHeights(0,0,grade);
            CopyClearKerb(source, revised, terrain, position, stats);
            tile.terrainData = revised;
            EditorUtility.SetDirty(revised);
            stats.detailTiles++;
        }
    }

    private static void CopyClearKerb(TerrainData source, TerrainData revised, TiledGround terrain, Vector3 origin, EditReport stats)
    {
        int width = source.detailWidth, height = source.detailHeight;
        float dx = source.size.x / width, dz = source.size.z / height;
        for (int layer = 0; layer < source.detailPrototypes.Length; layer++)
        {
            int[,] values = source.GetDetailLayer(0,0,width,height,layer);
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                if (values[z,x] == 0) continue;
                float worldX = origin.x + (x + .5f) * dx, worldZ = origin.z + (z + .5f) * dz;
                if (!terrain.Kerb(worldX,worldZ)) continue;
                values[z,x] = 0;
                stats.kerbCells++;
            }
            revised.SetDetailLayer(0,0,layer,values);
        }
    }

    private static void LiftBakedGrass(TiledGround terrain, EditReport stats)
    {
        var lods = new HashSet<LODGroup>();
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            Mesh mesh = filter.sharedMesh;
            if (!mesh || !mesh.isReadable || !mesh.name.Contains("AutumnBlades",StringComparison.Ordinal) ||
                mesh.name.StartsWith("RaisedLawn_",StringComparison.Ordinal))
                continue;
            var vertices = mesh.vertices;
            bool changed = false;
            float low = float.PositiveInfinity, high = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = filter.transform.TransformPoint(vertices[i]);
                float offset = terrain.Lawn(p.x,p.z,out float y) ? Mathf.Max(0f,y - terrain.Old(p.x,p.z)) : 0f;
                low = Mathf.Min(low,offset);
                high = Mathf.Max(high,offset);
                if (offset < .002f) continue;
                p.y += offset;
                vertices[i] = filter.transform.InverseTransformPoint(p);
                changed = true;
            }
            if (!changed) continue;
            if (filter.TryGetComponent<MeshRenderer>(out var renderer))
            {
                var group = renderer.GetComponentInParent<LODGroup>();
                if (group) lods.Add(group);
            }
            if (high - low <= .004f)
            {
                filter.transform.position += Vector3.up * ((high + low) * .5f);
                stats.translatedGrassMeshes++;
                continue;
            }
            var copy = UnityEngine.Object.Instantiate(mesh);
            copy.vertices = vertices;
            copy.RecalculateBounds();
            string source = AssetDatabase.GetAssetPath(mesh);
            string guid = AssetDatabase.AssetPathToGUID(source);
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Missing source grass GUID.");
            filter.sharedMesh = Save(copy,"Lifted_" + guid);
            stats.bakedGrassMeshes++;
        }
        foreach (var group in lods) group.RecalculateBounds();
    }

    public static void Build()
    {
        const string path = "Build/StreetWindowsHDRPLawnGrades/DuninaGorkavicha.exe";
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath, "Assets/Scenes/Street_LeafStudy.unity" },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = Path.GetFullPath(path)
        });
        if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Lawn grades player build failed.");
        Debug.Log("LAWN_GRADES_BUILD " + path);
    }
}
