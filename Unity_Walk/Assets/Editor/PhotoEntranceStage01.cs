using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// A bounded edit of the saved scene. It does not invoke any map generator.
public static class PhotoEntranceStage01
{
    private static string Root = "Assets/PhotoReconstruction/Stage01";
    private static string AppliedName = "Photo entrance 11 · threshold stage01";
    private static Vector3 Entrance = new Vector3(125.4542f, 0, 5.86925f);
    private static Vector3 Forward = Vector3.left;
    private static Vector3 Right = Vector3.forward;
    private static bool CourtMode;
    private const float LowerBy = .48f;

    [Serializable] private sealed class Shift
    {
        public string lod, material;
        public int vertices;
        public Bounds boundsBefore;
    }
    [Serializable] private sealed class Report
    {
        public string target, note;
        public string[] referencePhotos;
        public Vector3 entrance, forward, sourceEntranceApprox;
        public int removedStepAndLandingTriangles, movedVertices, removedStepColliders;
        public float platformTop, approachHeight;
        public Shift[] shiftedComponents;
    }
    private sealed class Plan
    {
        internal MeshFilter filter;
        internal Mesh mesh;
        internal int removed, moved;
    }
    private sealed class Sets
    {
        private readonly int[] parents;
        internal Sets(int count) { parents = Enumerable.Range(0, count).ToArray(); }
        internal int RootOf(int index) { while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; } return index; }
        internal void Join(int a, int b) { int x = RootOf(a), y = RootOf(b); if (x != y) parents[y] = x; }
    }

    public static void ApplyCourt7()
    {
        try
        {
            CourtMode=true;Root="Assets/PhotoReconstruction/Stage04Courtyard";
            AppliedName="Photo courtyard7 · low entrance threshold";
            Entrance=new Vector3(61.091938f,0,38.305f);Forward=Vector3.left;Right=Vector3.forward;
            Apply();
            var access=UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
            var collider=GameObject.Find(AppliedName).GetComponentInChildren<MeshCollider>();
            access.surfaces=access.surfaces.Concat(new[]{collider}).Distinct().ToArray();
        }
        finally
        {
            CourtMode=false;Root="Assets/PhotoReconstruction/Stage01";AppliedName="Photo entrance 11 · threshold stage01";
            Entrance=new Vector3(125.4542f,0,5.86925f);Forward=Vector3.left;Right=Vector3.forward;
        }
    }

    public static void Apply()
    {
        if (GameObject.Find(AppliedName)) { Debug.Log("PHOTO_ENTRANCE_11 already applied; no second translation"); return; }
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the existing Street scene before applying the photo entrance correction.");
        var building = UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(b => CourtMode
            ? b.address=="7"&&Vector3.Distance(b.transform.position,new Vector3(75.7405f,0,38.305f))<.1f
            : b.osmId == 114788197 && b.address == "11");
        var access = building.GetComponentsInChildren<Transform>().Single(t => t.name == (CourtMode?"Access · подход к крыльцу 1":"Access · подход к крыльцу 5"));
        if (Vector3.Distance(access.position, Entrance) > .03f || Vector3.Dot(access.forward, Forward) < .999f)
            throw new InvalidOperationException("The saved entrance pose differs from the inspected scene; refusing an unaligned patch.");
        Physics.SyncTransforms();
        var colliders = building.GetComponentsInChildren<BoxCollider>().Where(IsOldStepCollider).ToArray();
        if (colliders.Length != 4) throw new InvalidOperationException("Expected exactly four old step colliders in the selected bay; found " + colliders.Length);
        var shifts = new List<Shift>();
        var renderers = building.GetComponent<LODGroup>().GetLODs().SelectMany(l => l.renderers).Distinct().ToArray();
        if (renderers.Length != 3) throw new InvalidOperationException("The selected building must retain its three LOD renderers.");
        var plans = renderers.Select((r, i) => Prepare(r, i, shifts)).ToArray();
        if (plans.Any(p => p.removed < 48 || p.moved == 0))
            throw new InvalidOperationException("The bounded selection did not find the expected steps and complete inner door in every LOD.");
        var concrete = AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Buildings/Materials/EntranceConcrete.mat");
        if (!concrete) throw new InvalidOperationException("The existing PBR entrance concrete material is missing.");
        float approachHeight = ExistingAsphaltHeight();
        Directory.CreateDirectory(Root + "/Meshes"); AssetDatabase.Refresh();
        foreach (var plan in plans)
        {
            string path = Root + "/Meshes/" + plan.mesh.name + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved) { EditorUtility.CopySerialized(plan.mesh, saved); UnityEngine.Object.DestroyImmediate(plan.mesh); }
            else { saved = plan.mesh; AssetDatabase.CreateAsset(saved, path); }
            plan.filter.sharedMesh = saved; EditorUtility.SetDirty(plan.filter);
        }
        foreach (var collider in colliders) UnityEngine.Object.DestroyImmediate(collider);
        var stage = new GameObject(AppliedName);
        stage.transform.SetParent(building.transform.parent, true);
        BuildLanding(stage.transform, concrete, approachHeight);
        var view = new GameObject(CourtMode?"Opening QA · door courtyard7 threshold":"Opening QA · photo11threshold").transform;
        view.SetParent(building.transform, true);
        view.position = Entrance + Forward * 4 + Right * 2.5f + Vector3.up * 1.65f;
        view.rotation = Quaternion.LookRotation(Entrance - Forward * .7f + Vector3.up * 1.05f - view.position);
        var uncertainty = new GameObject(CourtMode?"Approximate alignment · low courtyard door from summer photos; threshold height estimated, portal architecture unchanged":"Approximate alignment · photos show low shop threshold; storefront and offset 1.32m remain unverified");
        uncertainty.tag = "EditorOnly"; uncertainty.transform.SetParent(stage.transform, false);
        uncertainty.transform.position = Entrance;
        var report = new Report {
            target = building.name + "/" + access.name,
            note = CourtMode?"Low internal-court threshold supported by summer No7 photos. One courtyard-facing model bay is corrected; exact threshold height and dimensions are estimates. Existing architectural portal is still approximate, no independent exact door coordinate was measured. Building poses unchanged.":"Photo-supported correction of one low entrance transition only. The shop photo coordinate is approximate and lies about 1.32m laterally from the model door. Existing portal/storefront architecture is not presented as an exact shop reconstruction. Platform dimensions and heights are estimates; buildings and other entrances are unchanged.",
            referencePhotos = CourtMode?new[]{"tools/photo-terrain-research/stage04/yreal7_10.jpg","tools/photo-terrain-research/stage04/yreal7_11.jpg"}:new[] { "tools/photo-entrance-research/h11_226187181184_1.jpg", "tools/photo-entrance-research/h11_91706029280_5.jpg" },
            entrance = Entrance, forward = Forward, sourceEntranceApprox = CourtMode?Entrance:new Vector3(128.1339f, 0, 7.1913f),
            removedStepAndLandingTriangles = plans.Sum(p => p.removed), movedVertices = plans.Sum(p => p.moved),
            removedStepColliders = colliders.Length, platformTop = .16f, approachHeight = approachHeight,
            shiftedComponents = shifts.ToArray()
        };
        Directory.CreateDirectory("street_qa");
        File.WriteAllText(CourtMode?"street_qa/photo_court7_entrance_stage04.json":"street_qa/photo_entrance_stage01_report.json", JsonUtility.ToJson(report, true));
        Physics.SyncTransforms();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("PHOTO_ENTRANCE_11 removedTriangles=" + report.removedStepAndLandingTriangles + " movedVertices=" + report.movedVertices
            + " removedColliders=" + report.removedStepColliders + " approachHeight=" + approachHeight);
    }

    private static Plan Prepare(Renderer renderer, int lod, List<Shift> shifts)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        string prefix=CourtMode?"Court7Entrance":"Photo11Entrance";
        if (!filter || !filter.sharedMesh || filter.sharedMesh.name.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Missing original mesh or an incomplete prior patch; inspect the scene before retrying.");
        var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh); mesh.name = prefix+"_LOD" + lod;
        var vertices = mesh.vertices;
        var world = vertices.Select(filter.transform.TransformPoint).ToArray();
        var move = new HashSet<int>();
        var materials = renderer.sharedMaterials;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            string material = sub < materials.Length && materials[sub] ? materials[sub].name : "";
            bool concrete = material == "EntranceConcrete";
            if (!concrete && !InnerMaterial(material)) continue;
            var triangles = mesh.GetTriangles(sub); var sets = new Sets(vertices.Length);
            var used = new HashSet<int>(); var welded = new Dictionary<Vector3Int, int>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                sets.Join(triangles[t], triangles[t + 1]); sets.Join(triangles[t], triangles[t + 2]);
                for (int j = 0; j < 3; j++)
                {
                    int index = triangles[t + j]; used.Add(index);
                    var p = world[index]; var key = new Vector3Int(Mathf.RoundToInt(p.x * 1000), Mathf.RoundToInt(p.y * 1000), Mathf.RoundToInt(p.z * 1000));
                    if (welded.TryGetValue(key, out int other)) sets.Join(index, other); else welded.Add(key, index);
                }
            }
            foreach (var component in used.GroupBy(sets.RootOf))
            {
                var indices = component.ToArray();
                // Atomic connected components prevent a clipped selector from stretching an
                // opening, door leaf, ring or handle while its other triangles remain behind.
                if (!indices.All(i => InnerBounds(world[i], concrete))) continue;
                foreach (int index in indices) move.Add(index);
                var bounds = new Bounds(world[indices[0]], Vector3.zero);
                foreach (int index in indices) bounds.Encapsulate(world[index]);
                shifts.Add(new Shift { lod = mesh.name, material = material, vertices = indices.Length, boundsBefore = bounds });
            }
        }
        int removed = 0;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            if (sub >= materials.Length || !materials[sub] || materials[sub].name != "EntranceConcrete") continue;
            var triangles = mesh.GetTriangles(sub); var keep = new List<int>(triangles.Length);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                bool selected = true;
                for (int j = 0; j < 3; j++)
                {
                    int index = triangles[t + j];
                    selected &= !move.Contains(index) && OldStepBounds(world[index]);
                }
                if (selected) removed++;
                else { keep.Add(triangles[t]); keep.Add(triangles[t + 1]); keep.Add(triangles[t + 2]); }
            }
            mesh.SetTriangles(keep, sub, false);
        }
        Vector3 offset = filter.transform.InverseTransformVector(Vector3.down * LowerBy);
        foreach (int index in move) vertices[index] += offset;
        mesh.vertices = vertices; mesh.RecalculateBounds();
        return new Plan { filter = filter, mesh = mesh, removed = removed, moved = move.Count };
    }

    private static bool InnerMaterial(string name) => name == "EntranceSteel" || name == "DoorHardware" || name.StartsWith("Glass", StringComparison.Ordinal)
        || name == "ArchitecturalFanGlass" || name == "IvoryFrames" || name == "BalconyIron";
    private static bool InnerBounds(Vector3 p, bool concrete)
    {
        var d = p - Entrance; float along = Vector3.Dot(d, Forward), lateral = Mathf.Abs(Vector3.Dot(d, Right));
        return concrete ? p.y >= .630f && p.y <= 2.89f && lateral <= .965f && along >= -1.65f && along <= -1.15f
            : p.y >= .60f && p.y <= 3.60f && lateral < 1.05f && along >= -1.62f && along <= -1.18f;
    }
    private static bool OldStepBounds(Vector3 p)
    {
        var d = p - Entrance; float along = Vector3.Dot(d, Forward);
        return p.y >= -.005f && p.y <= .645f && Mathf.Abs(Vector3.Dot(d, Right)) <= 1.251f && along >= -1.801f && along <= .251f;
    }
    private static bool IsOldStepCollider(BoxCollider collider)
    {
        var center = collider.center; var extents = collider.size * .5f;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            if (!OldStepBounds(collider.transform.TransformPoint(center + Vector3.Scale(extents, new Vector3(x, y, z))))) return false;
        return collider.size.y > .1f && collider.size.y <= .65f;
    }

    private static float ExistingAsphaltHeight()
    {
        foreach (var hit in Physics.RaycastAll(Entrance + Forward * .95f + Vector3.up * 2, Vector3.down, 3).OrderByDescending(h => h.point.y))
        {
            if (!(hit.collider is MeshCollider collider) || !collider.sharedMesh || hit.normal.y < .85f || hit.triangleIndex < 0) continue;
            var renderer = collider.GetComponent<Renderer>(); if (!renderer) continue;
            int end = 0;
            for (int sub = 0; sub < collider.sharedMesh.subMeshCount; sub++)
            {
                end += (int)collider.sharedMesh.GetIndexCount(sub) / 3;
                if (hit.triangleIndex >= end) continue;
                var material = sub < renderer.sharedMaterials.Length ? renderer.sharedMaterials[sub] : null;
                if (material && (material.name.Contains("Asphalt")||CourtMode&&material.HasProperty("_SurfaceClass")&&material.GetFloat("_SurfaceClass")>5.5f) && hit.point.y >= -.02f && hit.point.y < .16f) return hit.point.y;
                break;
            }
        }
        Debug.LogWarning("PHOTO_ENTRANCE_11 no asphalt sample at ramp end; temporary approach height .065m retained.");
        return .065f;
    }

    private static Vector3 P(float across, float along, float height) => Entrance + Right * across + Forward * along + Vector3.up * height;
    private static void BuildLanding(Transform parent, Material concrete, float approachHeight)
    {
        var mesh = new EnvironmentMesh();
        mesh.Box(concrete, P(0, -.65f, .095f), new Vector3(2.4f, .13f, 1.6f), Quaternion.LookRotation(Forward));
        var a = P(-1.2f, .15f, .16f); var b = P(1.2f, .15f, .16f);
        var c = P(1.2f, .95f, approachHeight); var d = P(-1.2f, .95f, approachHeight);
        mesh.Face(concrete, Vector3.up, a, b, c, d);
        foreach (int side in new[] { -1, 1 })
            mesh.Face(concrete, Right * side, P(side * 1.2f, .15f, .03f), P(side * 1.2f, .15f, .16f),
                P(side * 1.2f, .95f, approachHeight), P(side * 1.2f, .95f, .03f));
        mesh.Face(concrete, Forward, c, d, P(-1.2f, .95f, .03f), P(1.2f, .95f, .03f));
        var surface = mesh.Create(CourtMode?"Court7LowThreshold":"Photo11LowThreshold", CourtMode?"House 7 · low courtyard threshold":"House 11 · low threshold and short approach", Root);
        surface.transform.SetParent(parent, true);
        surface.AddComponent<MeshCollider>().sharedMesh = surface.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(surface, StaticEditorFlags.BatchingStatic);
    }
}
