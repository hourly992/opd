using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictHouse13Canopy
{
    private const string Root = "Assets/PhotoReconstruction/Stage12East";
    private const string Group = "№13 · северный навес и стык бордюра";
    private const string Entrance = "Access · торцевой вход 10";
    private const float CurbCutZ = 98.985f;
    private static readonly string[] OldMaterials = { "GreenStandingSeam", "RoofEdge", "BalconyIron", "EntranceConcrete" };
    private static readonly int[] OldCanopyTriangles = { 338, 194, 146 };

    public static void Apply()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene.");
        if (GameObject.Find(Group))
        {
            BlendRampShoulders();
            return;
        }

        var building = UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(b => b.address == "13" &&
            b.GetComponentsInChildren<Transform>().Any(t => t.name == Entrance));
        var door = building.GetComponentsInChildren<Transform>().Single(t => t.name == Entrance);
        if (Vector3.Distance(door.position, new Vector3(166.8544f, 0, 98.192f)) > .02f || door.forward.z < .99f)
            throw new InvalidOperationException("The matched north entrance moved.");
        Vector3 wall = door.position - door.forward * 1.80f;
        var oldPosts = building.GetComponentsInChildren<BoxCollider>().Where(c => c.enabled &&
            c.name == "Торцевой навес · стойки" && Math.Abs(c.transform.TransformPoint(c.center).z - wall.z - 1.54f) < .025f &&
            Math.Abs(Math.Abs(c.transform.TransformPoint(c.center).x - wall.x) - 1.33f) < .025f).ToArray();
        if (oldPosts.Length != 2) throw new InvalidOperationException("Expected two original north canopy post colliders.");

        var lods = building.GetComponent<LODGroup>().GetLODs();
        if (lods.Length != 3) throw new InvalidOperationException("Expected three existing house LODs.");
        var filters = lods.Select(lod => lod.renderers.Single().GetComponent<MeshFilter>()).ToArray();
        var materials = lods[0].renderers.Single().sharedMaterials;
        Material Surface(string name) => materials.Single(m => m && IsMaterial(m, name));
        var roof = Surface("GreenStandingSeam");
        var flashing = Surface("RoofEdge");
        var metal = Surface("BalconyIron");
        var footing = Surface("EntranceConcrete");

        var curb = GameObject.Find("Низкие кромки скруглённых подходов");
        var curbFilter = curb.GetComponent<MeshFilter>();
        var curbCollider = curb.GetComponent<MeshCollider>();
        var landing = GameObject.Find("№13 · площадка северного входа").GetComponentsInChildren<MeshCollider>()
            .Single(c => c.sharedMesh.name == "House13NorthLanding");
        if (curbFilter.sharedMesh != curbCollider.sharedMesh)
            throw new InvalidOperationException("The saved curb render and collision meshes differ.");

        Physics.SyncTransforms();
        float supportZ = wall.z + 1.91f;
        float[] postXs = { wall.x - 2.64f, wall.x - 1.71f, wall.x + 1.65f, wall.x + 2.77f };
        var supports = postXs.Select(x => new Vector3(x, SupportY(x, supportZ), supportZ)).ToArray();
        var editedLods = new Mesh[lods.Length];
        var removed = new int[lods.Length];
        for (int i = 0; i < lods.Length; i++)
        {
            editedLods[i] = WithoutOldCanopy(filters[i], lods[i].renderers.Single(), wall, out removed[i]);
            if (removed[i] != OldCanopyTriangles[i])
                throw new InvalidOperationException($"Unexpected old canopy triangle count for {filters[i].name}: {removed[i]}.");
        }

        // The east curb begins beneath the platform. Cut only that concealed
        // end, while the support test protects the exposed part of the curb.
        var trimmed = PhotoCourtSeamRepairStage05.ClipOverPaving(curbFilter, landing,
            Rect.MinMaxRect(168.42f, 98.89f, 168.60f, CurbCutZ), out int clipped);
        if (!trimmed || clipped == 0)
            throw new InvalidOperationException("No supported old curb end found under the north landing.");
        int eased = EaseWestCurbTip(trimmed, curbFilter.transform);
        if (eased == 0) throw new InvalidOperationException("No exposed west curb tip found alongside the north ramp.");

        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var group = new GameObject(Group);
        var canopy = BuildCanopy(wall, supports, roof, flashing, metal, footing);
        canopy.transform.SetParent(group.transform, true);
        var canopyLod = canopy.AddComponent<LODGroup>();
        canopyLod.SetLODs(new[] { new LOD(.012f, new Renderer[] { canopy.GetComponent<MeshRenderer>() }) });
        canopyLod.RecalculateBounds();

        for (int i = 0; i < filters.Length; i++)
        {
            filters[i].sharedMesh = SaveMesh(editedLods[i], "House13Facade_LOD" + i);
            PrefabUtility.RecordPrefabInstancePropertyModifications(filters[i]);
        }
        foreach (var old in oldPosts)
        {
            old.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(old);
        }
        curbFilter.sharedMesh = curbCollider.sharedMesh = SaveMesh(trimmed, "House13NorthApproachCurb");
        CapEastCurb(curb.GetComponent<MeshRenderer>().sharedMaterial).transform.SetParent(group.transform, false);
        Physics.SyncTransforms();
        int clearOpenings = VerifyPostColliders(canopy, supports);

        var evidence = new GameObject("Фотоориентир · северный навес №13") { tag = "EditorOnly" };
        evidence.transform.SetParent(group.transform, false);
        var note = evidence.AddComponent<PhotoSurveyMarker>();
        note.category = "№13 · длинный односкатный навес";
        note.observation = "Адресный кадр показывает низкую зелёную кровлю вдоль торца и тонкие опоры. Форма и пропорции привязаны к существующему северному входу.";
        note.uncertainty = "Ширина, уклон, положение стоек и профиль кромки оценены по кадру и по размерам игровой площадки.";
        note.geometryProvisional = true;
        note.studyBounds = canopy.GetComponent<Renderer>().bounds;
        note.sourceUrls = new[] { "https://avatars.mds.yandex.net/get-altay/16569030/2a00000198543ee61cdd8958713a87e97a35/orig" };
        BlendRampShoulders();
        Debug.Log($"STAGE12_HOUSE13 replacedTriangles={string.Join("/", removed)} oldPosts={oldPosts.Length} " +
            $"curbTrimmed={clipped} westCurbVertices={eased} curbCap=1 newPosts={supports.Length} clearOpenings={clearOpenings}");
    }

    private static void BlendRampShoulders()
    {
        const string assetName = "House13NorthLandingShoulders";
        var floor = GameObject.Find("№13 · площадка северного входа").GetComponentInChildren<MeshCollider>();
        if (floor.sharedMesh.name == assetName) return;
        if (floor.sharedMesh.name != "House13NorthLanding")
            throw new InvalidOperationException("Expected the original north entrance ramp.");

        var original = floor.sharedMesh;
        var renderer = floor.GetComponent<MeshRenderer>();
        var materials = renderer.sharedMaterials;
        int pavingIndex = Array.FindIndex(materials, material => IsMaterial(material, "ResidentialAccessPaving"));
        if (pavingIndex < 0) throw new InvalidOperationException("Expected the red north entrance paving.");
        var paving = materials[pavingIndex];
        var vertices = original.vertices;
        var normals = original.normals;
        var deck = original.GetTriangles(pavingIndex).Select(i => vertices[i]).Distinct().OrderBy(v => v.z).ToArray();
        if (deck.Length != 4 || Mathf.Abs(deck[0].z - 98.99f) > .002f || Mathf.Abs(deck[3].z - 100.16f) > .002f)
            throw new InvalidOperationException("The saved entrance ramp outline changed.");
        var near = deck.Take(2).OrderBy(v => v.x).ToArray();
        var far = deck.Skip(2).OrderBy(v => v.x).ToArray();

        Physics.SyncTransforms();
        Vector3[] deckProbes = new[] { .15f, .45f, .75f, .95f }.Select(t =>
            Vector3.Lerp((near[0] + near[1]) * .5f, (far[0] + far[1]) * .5f, t)).ToArray();
        float Probe(Vector3 point)
        {
            if (!floor.Raycast(new Ray(point + Vector3.up * .30f, Vector3.down), out var hit, .50f) || hit.normal.y < .70f)
                throw new InvalidOperationException($"No walkable north entrance surface at {point}.");
            return hit.point.y;
        }
        var originalDeckHeights = deckProbes.Select(Probe).ToArray();
        var mesh = new EnvironmentMesh();
        int removed = 0;
        for (int sub = 0; sub < original.subMeshCount; sub++)
        {
            var triangles = original.GetTriangles(sub);
            for (int j = 0; j < triangles.Length; j += 3)
            {
                int i = triangles[j];
                Vector3 a = vertices[i], b = vertices[triangles[j + 1]], c = vertices[triangles[j + 2]];
                // The only pale vertical faces here run the full length of the sloped deck.
                if (sub != pavingIndex && Mathf.Min(a.z, b.z, c.z) >= near[0].z - .001f &&
                    Mathf.Max(a.z, b.z, c.z) >= far[0].z - .001f && Mathf.Abs(normals[i].y) < .01f)
                {
                    removed++;
                    continue;
                }
                mesh.Face(materials[sub], normals[i], a, b, c);
            }
        }
        if (removed != 4) throw new InvalidOperationException($"Expected four concrete ramp side triangles, found {removed}.");

        var shoulderProbes = new List<Vector3>();
        const int segments = 12;
        for (int side = 0; side < 2; side++)
        {
            bool west = side == 0;
            Vector3 lastInner = default, lastOuter = default;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 inner = Vector3.Lerp(near[side], far[side], t);
                float width = Mathf.Lerp(west ? .11f : .30f, west ? .31f : .38f, Mathf.SmoothStep(0, 1, t * 4)) *
                    Mathf.SmoothStep(1, 0, Mathf.InverseLerp(.63f, 1, t));
                Vector3 outer = inner + Vector3.right * (west ? -width : width);
                if (i < segments)
                {
                    var ray = new Ray(outer + Vector3.up * .30f, Vector3.down);
                    var supports = Physics.RaycastAll(ray, .55f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        .Where(h => h.collider != floor && h.normal.y > .82f && h.point.y <= inner.y + .008f).ToArray();
                    if (supports.Length == 0)
                        throw new InvalidOperationException($"No supporting pavement or soil beneath the north ramp shoulder at {outer}.");
                    float ground = supports.Max(h => h.point.y) + .002f;
                    float startY = west ? .08f : near[side].y;
                    outer.y = Mathf.Min(inner.y, Mathf.Lerp(startY, ground, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .23f, t))));
                    if (i > 1 && width > .09f) shoulderProbes.Add((inner + outer) * .5f);
                }

                if (i > 0)
                {
                    void Face(Vector3 a, Vector3 b, Vector3 c)
                    {
                        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                        mesh.Face(paving, normal.y < 0 ? -normal : normal, a, b, c);
                    }
                    Face(lastInner, lastOuter, inner);
                    if (i < segments) Face(lastOuter, outer, inner);
                }
                lastInner = inner;
                lastOuter = outer;
            }
        }

        var temp = mesh.Create(assetName, assetName, Root, true);
        floor.GetComponent<MeshFilter>().sharedMesh = floor.sharedMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(temp);
        Physics.SyncTransforms();
        for (int i = 0; i < deckProbes.Length; i++)
            if (Mathf.Abs(Probe(deckProbes[i]) - originalDeckHeights[i]) > .001f)
                throw new InvalidOperationException("The walkable center of the north entrance ramp changed.");
        foreach (var point in shoulderProbes)
            if (Mathf.Abs(Probe(point) - point.y) > .004f)
                throw new InvalidOperationException("The north ramp shoulder mesh and collision disagree.");
        Debug.Log($"STAGE12_HOUSE13_RAMP concreteCheeksRemoved={removed} unchangedDeckProbes={deckProbes.Length} shoulderProbes={shoulderProbes.Count}");
    }

    private static bool IsMaterial(Material material, string name) =>
        material.name == name || material.name.StartsWith(name + "_", StringComparison.Ordinal) ||
        material.name == name + "(Clone)";

    private static Mesh WithoutOldCanopy(MeshFilter filter, Renderer renderer, Vector3 wall, out int removed)
    {
        var original = filter.sharedMesh;
        var mesh = UnityEngine.Object.Instantiate(original);
        var vertices = original.vertices.Select(filter.transform.TransformPoint).ToArray();
        bool InCanopy(Vector3 p, string part)
        {
            Vector3 local = p - wall;
            if (Math.Abs(local.x) >= 1.51f || local.z < -.023f || local.z > 1.74f || local.y < -.008f || local.y >= 2.65f)
                return false;
            if (part == "EntranceConcrete") return local.z >= 1.45f && local.z <= 1.63f && local.y <= .153f;
            if (part == "BalconyIron") return local.z > 1.01f;
            return local.y > 1.99f;
        }

        var materials = renderer.sharedMaterials;
        removed = 0;
        for (int s = 0; s < original.subMeshCount; s++)
        {
            if (!materials[s]) continue;
            string part = OldMaterials.FirstOrDefault(name => IsMaterial(materials[s], name));
            if (part == null) continue;
            var indices = original.GetTriangles(s);
            var kept = new List<int>(indices.Length);
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (InCanopy(vertices[a], part) && InCanopy(vertices[b], part) && InCanopy(vertices[c], part))
                {
                    removed++;
                    continue;
                }
                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
            }
            mesh.SetTriangles(kept, s, false);
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float SupportY(float x, float z)
    {
        var ray = new Ray(new Vector3(x, .45f, z), Vector3.down);
        var floors = Physics.RaycastAll(ray, .80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            .Where(h => h.normal.y > .82f).Select(h => h.point.y).ToArray();
        if (floors.Length == 0) throw new InvalidOperationException($"Canopy support misses the ground at {x:F2}, {z:F2}.");
        return floors.Max();
    }

    private static GameObject BuildCanopy(Vector3 wall, Vector3[] supports, Material roof, Material flashing,
        Material metal, Material footing)
    {
        float left = wall.x - 2.85f, right = wall.x + 2.98f;
        float back = wall.z + .025f, front = wall.z + 2.05f;
        const float backHeight = 2.43f, frontHeight = 2.23f, thickness = .032f;
        float Height(float z) => wall.y + Mathf.Lerp(backHeight, frontHeight, Mathf.InverseLerp(back, front, z));
        Vector3 Point(float x, float z, float lift = 0) => new Vector3(x, Height(z) + lift, z);
        var mesh = new EnvironmentMesh();
        Vector3 a = Point(left, back), b = Point(right, back), c = Point(right, front), d = Point(left, front);
        Vector3 normal = Vector3.Cross(d - a, b - a).normalized;
        Vector3 drop = Vector3.down * thickness;
        mesh.Face(roof, normal, a, b, c, d);
        mesh.Face(flashing, -normal, a + drop, b + drop, c + drop, d + drop);
        mesh.Face(flashing, Vector3.back, a, b, b + drop, a + drop);
        mesh.Face(flashing, Vector3.forward, c, d, d + drop, c + drop);
        mesh.Face(flashing, Vector3.left, d, a, a + drop, d + drop);
        mesh.Face(flashing, Vector3.right, b, c, c + drop, b + drop);
        for (float x = left + .12f; x < right - .04f; x += .32f)
        {
            mesh.Face(roof, normal, Point(x, back, .011f), Point(x + .024f, back, .011f),
                Point(x + .024f, front, .011f), Point(x, front, .011f));
            mesh.Face(roof, Vector3.left, Point(x, back), Point(x, front), Point(x, front, .011f), Point(x, back, .011f));
            mesh.Face(roof, Vector3.right, Point(x + .024f, front), Point(x + .024f, back),
                Point(x + .024f, back, .011f), Point(x + .024f, front, .011f));
        }
        float supportZ = supports[0].z;
        float beamY = Height(supportZ) - .071f;
        mesh.Box(flashing, new Vector3((left + right) * .5f, beamY, supportZ), new Vector3(right - left - .07f, .078f, .075f));
        mesh.Box(flashing, new Vector3((left + right) * .5f, Height(back) - .053f, back), new Vector3(right - left, .068f, .073f));
        foreach (var p in supports)
        {
            mesh.Box(metal, new Vector3(p.x, (p.y + beamY) * .5f, p.z), new Vector3(.053f, beamY - p.y, .053f));
            mesh.Box(footing, p + Vector3.up * .011f, new Vector3(.105f, .027f, .105f));
        }
        var canopy = mesh.Create("House13NorthCanopy", "Длинный металлический навес на стойках", Root, true);
        foreach (var p in supports)
            EnvironmentMesh.Collider(canopy, new Vector3(p.x, (p.y + beamY) * .5f, p.z), new Vector3(.057f, beamY - p.y, .057f));
        return canopy;
    }

    private static int EaseWestCurbTip(Mesh mesh, Transform transform)
    {
        var vertices = mesh.vertices;
        var start = new Vector2(166.8625f, 99.595f);
        var end = new Vector2(167.1495f, 99.9685f);
        var axis = (end - start).normalized;
        float length = Vector2.Distance(start, end);
        int changed = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = transform.TransformPoint(vertices[i]);
            var p = new Vector2(v.x, v.z) - start;
            float station = Vector2.Dot(p, axis);
            float side = Math.Abs(p.x * axis.y - p.y * axis.x);
            if (side > .06f || station < -.02f || station > length - .005f || v.y > .028f || v.y <= -.015f) continue;
            v.y = Mathf.Lerp(-.015f, v.y, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.018f, length - .012f, station)));
            vertices[i] = transform.InverseTransformPoint(v);
            changed++;
        }
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        return changed;
    }

    private static GameObject CapEastCurb(Material material)
    {
        // Intersect the three longitudinal edges of the saved east curb
        // course with the same plane used to trim the buried end.
        Vector3 Slice(Vector3 from, Vector3 to) => Vector3.Lerp(from, to, Mathf.InverseLerp(from.z, to.z, CurbCutZ));
        var inner = Slice(new Vector3(168.458f, .037f, 98.968f), new Vector3(168.745f, .041f, 99.342f));
        var crown = Slice(new Vector3(168.523f, .037f, 98.916f), new Vector3(168.810f, .041f, 99.290f));
        var bevel = Slice(new Vector3(168.532f, .029f, 98.909f), new Vector3(168.820f, .033f, 99.283f));
        var mesh = new EnvironmentMesh();
        mesh.Face(material, Vector3.back, inner, crown, bevel, new Vector3(bevel.x, -.015f, CurbCutZ),
            new Vector3(inner.x, -.015f, CurbCutZ));
        var cap = mesh.Create("House13NorthApproachCurbCap", "Закрытый торец бордюра под площадкой", Root);
        cap.AddComponent<MeshCollider>().sharedMesh = cap.GetComponent<MeshFilter>().sharedMesh;
        return cap;
    }

    private static int VerifyPostColliders(GameObject canopy, Vector3[] supports)
    {
        var posts = canopy.GetComponents<BoxCollider>();
        for (int i = 0; i < supports.Length; i++)
        {
            var p = supports[i] + Vector3.up * .9f + Vector3.forward * .18f;
            if (!posts[i].Raycast(new Ray(p, Vector3.back), out _, .36f))
                throw new InvalidOperationException("A visible north canopy post has no matching collision.");
        }
        for (int i = 1; i < supports.Length; i++)
        {
            var p = (supports[i] + supports[i - 1]) * .5f + Vector3.up * .9f + Vector3.forward * .18f;
            var through = new Ray(p, Vector3.back);
            if (posts.Any(post => post.Raycast(through, out _, .36f)))
                throw new InvalidOperationException("A canopy collider obstructs the space between two north canopy posts.");
        }
        return supports.Length - 1;
    }

    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        mesh.name = name;
        string path = Root + "/Meshes/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!existing)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        return existing;
    }
}
