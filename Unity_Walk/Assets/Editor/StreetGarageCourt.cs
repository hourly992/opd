using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static partial class StreetGarageLevelRepair
{
    private const string Art = "Assets/Art/StreetRepair";
    private const string Output = Art + "/SouthGarageCourt";
    private const string CourtName = "Двор 5/7 · подъём и два гаражных въезда";
    private const float Center = 27f, Width = 14.9f, Mouth = -50f, Front = -54.60f, Rear = -26.8f;
    private const float Rise = .85f, RiseLength = 6f, ReturnLength = 5f;
    private const float LaneOffset = 4.1f, HalfWallWidth = 2.46f, VestibuleEnd = 20.7f;
    private const float Left = Center - Width * .5f, Right = Center + Width * .5f;
    private static readonly Vector2[] Footprint =
    {
        new(Left, FrontAt(Left)), new(Right, FrontAt(Right)), new(Right, Rear), new(Left, Rear)
    };

    [Serializable]
    private sealed class Result
    {
        public float mouthY, mouthZ = Mouth, roadZ = Front, rearZ = Rear, rise = Rise;
        public float clearWidth = 4.4f, depth = 2.64f, rampLength = 18f;
        public float vestibuleEnd = VestibuleEnd;
        public string[] cutSurfaces, adjustedObjects;
        public int alteredTriangles, roofColliders, supportColliders;
        public GarageLawnMask.Result lawn;
        public Vector2[] footprint = Footprint;
    }

    [MenuItem("Street/Repair southern garage court")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Street.unity", OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(x => x.name == CourtName))
            throw new InvalidOperationException("The southern garage court already exists in the saved scene.");

        var references = UnityEngine.Object.FindObjectsByType<UndergroundParkingEntrance>();
        var template = references.Single(x => x.name.EndsWith("north-west", StringComparison.Ordinal));
        var originalBody = AssetDatabase.LoadAssetAtPath<Mesh>(Art + "/GarageEntrance.asset");
        var originalRoof = AssetDatabase.LoadAssetAtPath<Mesh>(Art + "/GarageBarrelCanopy.asset");
        var bodyMaterials = template.GetComponent<MeshRenderer>().sharedMaterials;
        var roofMaterial = template.transform.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
        var metal = LoadDarkMetal();
        if (!originalBody || !originalRoof || bodyMaterials.Length != 2 || !roofMaterial || !metal ||
            template.clearWidth != 4.4f || template.length != 18f || template.depth != 2.64f)
            throw new InvalidOperationException("Saved garage geometry or its HDRP materials do not match the template.");

        PrepareOutput();
        Physics.SyncTransforms();
        var ground = SelectGround(scene).ToArray();
        float OldHeight(float x, float z) => SampleHeight(ground, x, z);
        var mouthLevels = new[] { OldHeight(Center - LaneOffset, Mouth), OldHeight(Center, Mouth),
            OldHeight(Center + LaneOffset, Mouth) };
        Array.Sort(mouthLevels);
        float level = mouthLevels[1];
        var result = new Result { mouthY = level };
        var root = new GameObject(CourtName);
        var court = MakeDeck(level, OldHeight);
        SceneMesh(root.transform, "Подъём и асфальтовая площадка", court, new[] { bodyMaterials[0] }, true);
        Mesh bayBody = OpenGarageBody(originalBody);
        Mesh roofCollision = RoofShell();
        for (int side = 0; side < 2; side++)
        {
            string label = side == 0 ? "Западный" : "Восточный";
            float centerX = Center + (side == 0 ? -LaneOffset : LaneOffset);
            var entrance = SceneMesh(root.transform, label + " гаражный въезд", bayBody, bodyMaterials, true);
            entrance.transform.localPosition = new Vector3(centerX, level, Mouth);
            var metadata = entrance.AddComponent<UndergroundParkingEntrance>();
            metadata.address = "5/7 · южный двор";
            metadata.placementProvisional = true;
            metadata.length = template.length;
            metadata.clearWidth = template.clearWidth;
            metadata.depth = template.depth;
            metadata.maximumGrade = template.maximumGrade;
            metadata.floorAndWalls = entrance.GetComponent<MeshCollider>();
            metadata.evidence = "Профиль и арка повторяют существующую пару. Привязка согласована по снимкам и сохранённой геометрии двора.";
            var canopy = SceneMesh(entrance.transform, label + " арочный навес", originalRoof, new[] { roofMaterial }, false);
            canopy.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var roof = canopy.AddComponent<MeshCollider>();
            roof.sharedMesh = roofCollision;
            result.roofColliders++;
            result.supportColliders += AddFrame(entrance.transform, label, metal);
        }

        var adjusted = HideWastePad(level);
        adjusted.AddRange(SeatProps(root.transform, court));
        result.adjustedObjects = adjusted.ToArray();
        result.cutSurfaces = CutOldSurfaces(scene, ground, result);
        result.lawn = GarageLawnMask.Apply(scene, Footprint, Output);
        if (GarageLawnMask.Inspect(scene, Footprint).remainingInstances != 0)
            throw new InvalidOperationException("Native grass remains inside the repaired court.");
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Unable to save the repaired court scene.");
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("QA_HDRP/garage-site");
        File.WriteAllText("QA_HDRP/garage-site/repair.json", JsonUtility.ToJson(result, true));
        Debug.Log($"GARAGE_COURT_DONE y={level:F3} cut={result.cutSurfaces.Length} objects={adjusted.Count}");
    }

    public static void ApplyAndBuild()
    {
        Apply();
        BuildAfter();
    }

    private static void PrepareOutput()
    {
        if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(Art, "SouthGarageCourt");
    }

    private static Material LoadDarkMetal()
    {
        return AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Pilot/Materials/GraphiteMetal.mat");
    }

    // Join into the existing slightly rotated asphalt seam, two centimetres
    // inside its top so both sides meet at the actual saved road level.
    private static float FrontAt(float x) => -54.46068f - .017343f * (x - Center);

    private static Mesh MakeDeck(float level, Func<float, float, float> oldHeight)
    {
        // The cross-section carries the same walls and 0.85 m rise as the reference.
        // Sampling the existing boundary makes the transition local to the repaired apron.
        var xs = Divisions(Left, Right, .38f, Left + .85f, Right - .85f,
            Center - LaneOffset - HalfWallWidth, Center - LaneOffset + HalfWallWidth,
            Center + LaneOffset - HalfWallWidth, Center + LaneOffset + HalfWallWidth);
        var zs = Divisions(Front, Rear, .42f, Mouth, Mouth + RiseLength,
            Mouth + 18f, Mouth + VestibuleEnd, Rear - ReturnLength);
        var mesh = new CourtMesh(1);

        Vector3 Point(float x, float z)
        {
            float boundary = FrontAt(x);
            z = Mathf.Max(z, boundary);
            float roadMix = Mathf.InverseLerp(boundary, Mouth, z);
            float side = Mathf.Clamp01(Mathf.Min(x - Left, Right - x) / .85f);
            bool nearGarageWall = Mathf.Min(Mathf.Abs(x - Center + LaneOffset), Mathf.Abs(x - Center - LaneOffset)) <= HalfWallWidth + .001f;
            float backMix = Mathf.Clamp01((Rear - z) / ReturnLength);
            float line = Mathf.Clamp01((z - Mouth) / RiseLength);
            // All points on a garage mouth join its floor at precisely level.
            float baseMix = Mathf.Min(backMix, Mathf.SmoothStep(0, 1, roadMix)) * (nearGarageWall ? 1f : side);
            float height = Mathf.Lerp(oldHeight(x, z), level, baseMix);
            height += Rise * Mathf.Min(line, nearGarageWall ? 1 : side, backMix);
            return new Vector3(x, height, z);
        }

        for (int iz = 0; iz < zs.Count - 1; iz++)
        for (int ix = 0; ix < xs.Count - 1; ix++)
        {
            float xa = xs[ix], xb = xs[ix + 1], za = zs[iz], zb = zs[iz + 1];
            float xc = (xa + xb) * .5f, zc = (za + zb) * .5f;
            if (zc >= Mouth && zc < Mouth + 18f &&
                Mathf.Min(Mathf.Abs(xc - Center + LaneOffset), Mathf.Abs(xc - Center - LaneOffset)) < HalfWallWidth)
                continue;
            mesh.Quad(Point(xa, za), Point(xa, zb), Point(xb, zb), Point(xb, za), 0);
        }
        return Save(mesh.Build("South garage court asphalt"), "CourtAsphalt");
    }

    private static List<float> Divisions(float min, float max, float spacing, params float[] keys)
    {
        var required = keys.Append(min).Append(max).Where(x => x >= min && x <= max).Distinct().OrderBy(x => x).ToArray();
        var result = new List<float>();
        for (int i = 1; i < required.Length; i++)
        {
            float a = required[i - 1], b = required[i];
            int count = Mathf.CeilToInt((b - a) / spacing);
            for (int j = 0; j < count; j++) result.Add(Mathf.Lerp(a, b, (float)j / count));
        }
        result.Add(max);
        return result;
    }

    private static Mesh OpenGarageBody(Mesh original)
    {
        var data = new CourtMesh(2);
        var vertices = original.vertices;
        var colors = original.colors;
        for (int slot = 0; slot < original.subMeshCount; slot++)
        {
            var indices = original.GetIndices(slot);
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = vertices[indices[i]]; var b = vertices[indices[i+1]]; var c = vertices[indices[i+2]];
                // The template's rear shutter becomes the end wall of a short covered bay.
                if (a.z > 17.96f && b.z > 17.96f && c.z > 17.96f)
                {
                    var advance = Vector3.forward * (VestibuleEnd - 18f);
                    a += advance; b += advance; c += advance;
                }
                data.Tri(a, b, c, slot, colors.Length > 0 ? colors[indices[i]] : Color.white);
            }
        }
        data.Quad(new(-2.2f,-2.64f,18),new(-2.2f,-2.64f,VestibuleEnd),
            new(2.2f,-2.64f,VestibuleEnd),new(2.2f,-2.64f,18),0);
        for (int side = -1; side <= 1; side += 2)
            data.Box(new Vector3(side * 2.33f, -.915f, (18 + VestibuleEnd) * .5f),
                new Vector3(.26f, 3.45f, VestibuleEnd - 18), Quaternion.identity, 1);
        // Separate soffit and slab sit below the upper asphalt.
        data.Box(new Vector3(0,.205f,(18 + VestibuleEnd) * .5f),
            new Vector3(4.92f,.30f,VestibuleEnd - 18),Quaternion.identity,1);
        data.Box(new Vector3(0,.585f,18.06f),new Vector3(4.92f,.46f,.12f),Quaternion.identity,1);
        return Save(data.Build("Garage passage and access bay"), "GaragePassage");
    }

    private static Vector3 Arch(float angle, float z, float thickness = 0)
    {
        return new Vector3((2.44f + thickness) * Mathf.Cos(angle),
            1.01f + (1.55f + thickness) * Mathf.Sin(angle), z);
    }

    private static Mesh RoofShell()
    {
        var data = new CourtMesh(1);
        const float shell = .055f;
        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.PI * i / 12f, b = Mathf.PI * (i + 1) / 12f;
            data.Quad(Arch(a,6),Arch(b,6),Arch(b,18),Arch(a,18),0);
            data.Quad(Arch(b,6,-shell),Arch(a,6,-shell),Arch(a,18,-shell),Arch(b,18,-shell),0);
            data.Quad(Arch(a,6,-shell),Arch(b,6,-shell),Arch(b,6),Arch(a,6),0);
            data.Quad(Arch(a,18),Arch(b,18),Arch(b,18,-shell),Arch(a,18,-shell),0);
        }
        data.Quad(Arch(0,6),Arch(0,18),Arch(0,18,-shell),Arch(0,6,-shell),0);
        data.Quad(Arch(Mathf.PI,18),Arch(Mathf.PI,6),Arch(Mathf.PI,6,-shell),Arch(Mathf.PI,18,-shell),0);
        return Save(data.Build("Closed curved canopy collision shell"), "CanopyCollision");
    }

    private static int AddFrame(Transform parent, string label, Material material)
    {
        var data = new CourtMesh(1);
        float[] stations = {6.04f, 8f, 10f, 12f, 14f, 16f, 17.96f};
        int count = 0;
        foreach (float z in stations)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector3 a = Arch(Mathf.PI * i / 12, z), b = Arch(Mathf.PI * (i+1) / 12, z);
                data.Box((a+b)*.5f, new Vector3(.055f, .055f, (a-b).magnitude + .013f),
                    Quaternion.LookRotation(b-a, Vector3.forward), 0);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var position = new Vector3(side * 2.38f, 1.16f, z);
                var size = new Vector3(.082f, .36f, .083f);
                data.Box(position,size,Quaternion.identity,0);
                var support = new GameObject(label + " · стойка навеса");
                support.transform.SetParent(parent,false);
                support.transform.localPosition = position;
                support.AddComponent<BoxCollider>().size=size;
                count++;
            }
        }
        foreach (int side in new[] {-1,1})
            data.Box(new Vector3(side*2.425f,1.033f,12),new Vector3(.096f,.07f,12),Quaternion.identity,0);
        var frame = Save(data.Build("Canopy ribs and posts"), "CanopyFrame");
        SceneMesh(parent, label + " · каркас навеса", frame, new[] {material}, false);
        return count;
    }

    private static GameObject SceneMesh(Transform parent, string name, Mesh mesh, Material[] materials, bool collision)
    {
        var node = new GameObject(name);
        node.transform.SetParent(parent, false);
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        node.AddComponent<MeshRenderer>().sharedMaterials = materials;
        if (collision) node.AddComponent<MeshCollider>().sharedMesh = mesh;
        return node;
    }

    private static Mesh Save(Mesh mesh, string name)
    {
        string path = Output + "/" + name + ".asset";
        var previous = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!previous)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, previous);
        EditorUtility.SetDirty(previous);
        UnityEngine.Object.DestroyImmediate(mesh);
        return previous;
    }

    private sealed class CourtMesh
    {
        private readonly List<Vector3> vertices = new(), normals = new();
        private readonly List<Vector2> uv = new();
        private readonly List<Color> colors = new();
        private readonly List<int>[] triangles;

        public CourtMesh(int slots) => triangles = Enumerable.Range(0, slots).Select(_ => new List<int>()).ToArray();

        public void Tri(Vector3 a, Vector3 b, Vector3 c, int slot, Color? color = null)
        {
            Vector3 normal = Vector3.Cross(b-a,c-a);
            if (normal.sqrMagnitude < .0000000001f) return;
            normal.Normalize();
            foreach (var p in new[] {a,b,c})
            {
                triangles[slot].Add(vertices.Count);
                vertices.Add(p);
                normals.Add(normal);
                uv.Add(Mathf.Abs(normal.y) > .5f ? new Vector2(p.x,p.z) :
                    new Vector2(Mathf.Abs(normal.x) > .5f ? p.z : p.x,p.y));
                colors.Add(color ?? Color.white);
            }
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int slot)
        {
            Tri(a,b,c,slot);
            Tri(a,c,d,slot);
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rotation, int slot)
        {
            Vector3 V(int x, int y, int z) => center + rotation * Vector3.Scale(size*.5f, new Vector3(x,y,z));
            Quad(V(-1,1,-1),V(-1,1,1),V(1,1,1),V(1,1,-1),slot);
            Quad(V(-1,-1,1),V(-1,-1,-1),V(1,-1,-1),V(1,-1,1),slot);
            Quad(V(-1,-1,-1),V(-1,1,-1),V(1,1,-1),V(1,-1,-1),slot);
            Quad(V(1,-1,1),V(1,1,1),V(-1,1,1),V(-1,-1,1),slot);
            Quad(V(-1,-1,1),V(-1,1,1),V(-1,1,-1),V(-1,-1,-1),slot);
            Quad(V(1,-1,-1),V(1,1,-1),V(1,1,1),V(1,-1,1),slot);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name, subMeshCount = triangles.Length,
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0,uv);
            mesh.SetColors(colors);
            for (int k = 0; k < triangles.Length; k++) mesh.SetTriangles(triangles[k],k);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
