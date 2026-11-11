using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Inspect the saved render geometry. An existing wall collider can conceal a visible hole.
internal static class DistrictPortalRepairs
{
    private const string Root = "Assets/PhotoReconstruction/DistrictEntrances";
    private const string Prefix = "Входные ниши · откосы LOD";
    private const float Front = -1.34f, Back = -1.815f, Radius = 2.75f, Spring = 1.35f;
    private static readonly float[] Depths = { -1.36f, -1.50f, -1.66f, -1.80f };
    private static readonly float[] Heights = { .05f, .40f, .85f, 1.15f };

    [Serializable]
    private sealed class Report
    {
        public int candidatePortals, standardPortalLods, probes, closedBefore, closedAfter, repairedSections;
        public string[] excludedPortalLods;
    }

    private sealed class Inspection
    {
        internal readonly Report report = new Report();
        internal readonly List<string> excluded = new List<string>();
        internal readonly List<Ray> gaps = new List<Ray>();
        internal readonly EnvironmentMesh repair = new EnvironmentMesh();
    }

    internal static void Apply()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before inspecting its entrances.");
        var totals = new Report();
        var excluded = new List<string>();
        foreach (var building in Object.FindObjectsByType<StreetBuilding>())
        {
            var markers = building.GetComponentsInChildren<Transform>().Where(t =>
                t.name.StartsWith("Access · подход к крыльцу", StringComparison.Ordinal)).ToArray();
            if (markers.Length == 0) continue;
            totals.candidatePortals += markers.Length;
            var group = building.GetComponent<LODGroup>();
            var lods = group.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                var renderer = lods[i].renderers[0];
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                var inspection = new Inspection();
                var temporary = new GameObject("District portal render geometry probe");
                try
                {
                    temporary.transform.SetParent(renderer.transform, false);
                    var probe = temporary.AddComponent<MeshCollider>();
                    probe.sharedMesh = mesh;
                    Physics.SyncTransforms();
                    foreach (var entrance in markers)
                        InspectPortal(building.transform, entrance, probe, renderer.sharedMaterials, inspection);
                }
                finally { Object.DestroyImmediate(temporary); }
                if (inspection.gaps.Count != 0)
                {
                    var repaired = SaveAndVerify(building, i, inspection);
                    lods[i].renderers = lods[i].renderers.Where(r => r && r.name != Prefix + i)
                        .Append(repaired).ToArray();
                    group.SetLODs(lods);
                    group.RecalculateBounds();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(group);
                    EditorUtility.SetDirty(group);
                }
                totals.standardPortalLods += inspection.report.standardPortalLods;
                totals.probes += inspection.report.probes;
                totals.closedBefore += inspection.report.closedBefore;
                totals.closedAfter += inspection.report.closedBefore + inspection.gaps.Count;
                totals.repairedSections += inspection.report.repairedSections;
                excluded.AddRange(inspection.excluded.Select(label => $"{building.address}/{label}/LOD{i}"));
            }
        }
        totals.excludedPortalLods = excluded.ToArray();
        if (totals.standardPortalLods == 0 || totals.closedAfter != totals.probes)
            throw new InvalidOperationException("The district portal audit did not verify the saved arches.");
        Directory.CreateDirectory("street_qa");
        File.WriteAllText("street_qa/district_portals.json", JsonUtility.ToJson(totals, true));
        Debug.Log($"DISTRICT_PORTALS candidates={totals.candidatePortals} standardLods={totals.standardPortalLods} " +
                  $"closedBefore={totals.closedBefore} closedAfter={totals.closedAfter} repairs={totals.repairedSections} " +
                  $"nonTemplateLods={excluded.Count}");
    }

    private static void InspectPortal(Transform building, Transform entry, MeshCollider probe,
        Material[] materials, Inspection inspection)
    {
        // The two red backing witnesses and the facade edges distinguish a retained arch
        // from a low photo-based doorway even when it has the same access marker.
        var sides = new[] { -1, 1 };
        Ray FromFront(float x, float y) => new Ray(entry.TransformPoint(new Vector3(x, y, -1.20f)), -entry.forward);
        foreach (int side in sides)
        {
            if (!probe.Raycast(FromFront(side * 2.1f, .50f), out var backing, .68f) ||
                Mathf.Abs(entry.InverseTransformPoint(backing.point).z - Back) > .012f ||
                !probe.Raycast(FromFront(side * 2.84f, .50f), out var facade, .25f) ||
                Mathf.Abs(entry.InverseTransformPoint(facade.point).z - Front) > .012f)
            {
                inspection.excluded.Add(entry.name);
                return;
            }
        }

        inspection.report.standardPortalLods++;
        foreach (int side in sides)
        {
            int prior = inspection.gaps.Count;
            foreach (float y in Heights)
            foreach (float z in Depths)
                Check(new Vector3(side * 2.59f, y, z), new Vector3(side, 0, 0));
            if (inspection.gaps.Count == prior) continue;
            probe.Raycast(FromFront(side * 2.84f, .50f), out var source, .25f);
            float x = side * (Radius + .005f);
            Add(MaterialAt(probe, materials, source), Vector3.left * side,
                new Vector3(x, -.02f, Front), new Vector3(x, Spring, Front),
                new Vector3(x, Spring, Back - .015f), new Vector3(x, -.02f, Back - .015f));
        }
        for (int i = 0; i < 16; i++)
        {
            float angle = (i + .5f) * Mathf.PI / 16;
            var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
            var center = new Vector3(0, Spring, 0);
            int prior = inspection.gaps.Count;
            foreach (float z in Depths)
                Check(center + radial * 2.59f + Vector3.forward * z, radial);
            if (inspection.gaps.Count == prior) continue;
            var sourcePoint = center + radial * 2.82f;
            if (!probe.Raycast(FromFront(sourcePoint.x, sourcePoint.y), out var source, .25f))
                throw new InvalidOperationException($"An arch repair needs an intact material witness at {entry.position:F3}.");
            float a = i * Mathf.PI / 16, b = (i + 1) * Mathf.PI / 16;
            var p = center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * (Radius + .005f);
            var q = center + new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0) * (Radius + .005f);
            Add(MaterialAt(probe, materials, source), -radial, p + Vector3.forward * Front, q + Vector3.forward * Front,
                q + Vector3.forward * (Back - .015f), p + Vector3.forward * (Back - .015f));
        }

        void Check(Vector3 origin, Vector3 direction)
        {
            inspection.report.probes++;
            var ray = new Ray(entry.TransformPoint(origin), entry.TransformDirection(direction));
            if (probe.Raycast(ray, out var hit, .24f) && hit.distance > .11f) inspection.report.closedBefore++;
            else inspection.gaps.Add(ray);
        }

        void Add(Material material, Vector3 normal, params Vector3[] points)
        {
            inspection.report.repairedSections++;
            inspection.repair.Face(material, building.InverseTransformDirection(entry.TransformDirection(normal)),
                points.Select(p => building.InverseTransformPoint(entry.TransformPoint(p))).ToArray());
        }
    }

    private static Material MaterialAt(MeshCollider collider, Material[] materials, RaycastHit hit)
    {
        int triangle = hit.triangleIndex;
        for (int i = 0; i < collider.sharedMesh.subMeshCount; i++)
        {
            int count = (int)collider.sharedMesh.GetIndexCount(i) / 3;
            if (triangle < count) return materials[i];
            triangle -= count;
        }
        throw new InvalidOperationException("Could not identify the adjacent doorway material.");
    }

    private static Renderer SaveAndVerify(StreetBuilding building, int lod, Inspection inspection)
    {
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var previous = building.transform.Find(Prefix + lod);
        if (previous) Object.DestroyImmediate(previous.gameObject);
        var repair = inspection.repair.Create($"PortalReturns_{building.osmId}_LOD{lod}", Prefix + lod, Root, true);
        repair.transform.SetParent(building.transform, false);
        var probe = repair.AddComponent<MeshCollider>();
        probe.sharedMesh = repair.GetComponent<MeshFilter>().sharedMesh;
        Physics.SyncTransforms();
        foreach (var ray in inspection.gaps)
            if (!probe.Raycast(ray, out _, .24f))
                throw new InvalidOperationException($"Unclosed arch section on {building.address}, LOD{lod}.");
        // The existing main building box already supplies the wall collision here.
        Object.DestroyImmediate(probe);
        GameObjectUtility.SetStaticEditorFlags(repair, StaticEditorFlags.BatchingStatic);
        return repair.GetComponent<Renderer>();
    }
}
