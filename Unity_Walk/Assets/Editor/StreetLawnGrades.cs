using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class StreetLawnGrades
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string Work = "QA_HDRP/lawn-grades";
    private const string AssetsPath = "Assets/Art/StreetRepair/LawnGrades";
    private const string RootName = "Поднятые газоны и бордюры района";
    private const string KerbPath = "Assets/Art/PaintedDistrict/Materials/5bfca59280e3c9044a6683ba01b05ab1_2100000_RoadKerb_0.mat";
    private const string TurfPath = "Assets/Art/AutumnGrass/Lawn ground.mat";

    // 1 original ground, 2 paved surfaces, 3 existing kerb, 4 preserved relief.
    private static int SurfaceKind(Material material, MeshFilter filter, bool ground)
    {
        if (ground) return 1;
        string n = material ? material.name.ToLowerInvariant() : "";
        string mesh = filter.sharedMesh.name.ToLowerInvariant();
        if (mesh.StartsWith("islandrelief05") || mesh.Contains("garagehouse") ||
            n == "roadvergesurface" || n == "vergeblueedge")
            return 4;
        if (IsKerb(material) || filter.name == "Бордюр · светлый бетон")
            return 3;
        if (n.Contains("arris") || n.Contains("soilaggregate") || n.Contains("shoulder"))
            return 0;
        if (n.Contains("asphalt") && !n.Contains("mark") ||
            material && material.shader && material.shader.name == "Street/Asphalt020S" ||
            n == "path" || n.Contains("runningbond") || n.Contains("paving") || n.Contains("paver") ||
            n.Contains("pavement") || n.Contains("sidewalk") ||
            n.Contains("concrete") || n.Contains("rubber") || n.Contains("playground") || n.Contains("tile") ||
            n.Contains("brick") || n.Contains("wood decking"))
            return 2;
        return 0;
    }

    private static bool IsKerb(Material material)
    {
        if (!material) return false;
        string n = material.name.ToLowerInvariant();
        return (n.Contains("kerb") || n.Contains("curb")) && !n.Contains("arris");
    }

    private static MeshCollider Ground()
    {
        return UnityEngine.Object.FindObjectsByType<MeshCollider>().Single(c => c.name == "Grass" && c.bounds.size.x > 1000);
    }

    public static void Export()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(Work);
        var ground = Ground();
        var inventory = new List<string>();
        var bounds = new Bounds(new Vector3(-80f, 0, -80f), new Vector3(1550f, 4f, 1550f));
        using var output = new BinaryWriter(File.Create(Work + "/surfaces.bin"));
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            if (!mesh || !mesh.isReadable || !filter.TryGetComponent<MeshRenderer>(out var renderer) ||
                !renderer.enabled || !renderer.bounds.Intersects(bounds)) continue;
            var materials = renderer.sharedMaterials;
            bool isGround = filter.gameObject == ground.gameObject;
            bool isLocalSurface = renderer.bounds.min.y < 1.05f && renderer.bounds.max.y < 2.05f;
            if (!isGround && !isLocalSurface) continue;
            var vertices = mesh.vertices;
            var world = Array.ConvertAll(vertices, filter.transform.TransformPoint);
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                var material = materials.Length == 0 ? null : materials[Mathf.Min(slot, materials.Length - 1)];
                int kind = SurfaceKind(material, filter, isGround);
                inventory.Add($"{kind}\t{filter.name}\t{material?.name}\t{mesh.name}\t{mesh.GetIndexCount(slot) / 3}\t{renderer.bounds}");
                if (kind == 0 || mesh.GetTopology(slot) != MeshTopology.Triangles) continue;
                var indices = mesh.GetTriangles(slot);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = world[indices[i]];
                    var b = world[indices[i + 1]];
                    var c = world[indices[i + 2]];
                    var normal = Vector3.Cross(b - a, c - a);
                    if (normal.y <= .000001f || normal.normalized.y < .14f) continue;
                    output.Write(kind);
                    foreach (var point in new[] { a, b, c })
                    {
                        output.Write(point.x);
                        output.Write(point.y);
                        output.Write(point.z);
                    }
                }
            }
        }
        foreach (var box in UnityEngine.Object.FindObjectsByType<BoxCollider>())
        {
            if (!box.enabled || box.isTrigger || box.bounds.size.y < 3.25f || box.bounds.size.x < 3f ||
                box.bounds.size.z < 3f || box.bounds.min.y > .5f) continue;
            var half = box.size * .5f;
            Vector3 Corner(float x, float z) => box.transform.TransformPoint(box.center + new Vector3(x * half.x, -half.y, z * half.z));
            var a = Corner(-1,-1); var b = Corner(-1,1); var c = Corner(1,1); var d = Corner(1,-1);
            foreach (var triangle in new[] { new[] { a, b, c }, new[] { a, c, d } })
            {
                output.Write(4);
                foreach (var point in triangle) { output.Write(point.x); output.Write(point.y); output.Write(point.z); }
            }
        }
        File.WriteAllLines(Work + "/inventory.tsv", inventory);
        Debug.Log($"LAWN_GRADES_EXPORTED scene={scene.name} slots={inventory.Count}");
    }
}
