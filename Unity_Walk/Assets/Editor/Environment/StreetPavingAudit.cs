using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class StreetPavingAudit
{
    [Serializable] private sealed class Finding
    {
        public string category, site;
        public Vector3 expected, actual;
        public Vector3 normal;
        public bool hit;
        public float difference;
        public int triangle;
    }
    [Serializable] private sealed class Result { public Finding[] findings; }

    [MenuItem("Street/Audit pavement joins")]
    public static void Check()
    {
        if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/Street.unity");
        else if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the Street scene before auditing pavement joins.");
        Physics.SyncTransforms();
        var findings = new List<Finding>();
        void Inspect(string category, string site, Vector3 point, MeshCollider[] surfaces, float tolerance)
        {
            RaycastHit best = default(RaycastHit); bool found = false;
            foreach (var surface in surfaces)
                if (surface && surface.Raycast(new Ray(point + Vector3.up * 2, Vector3.down), out var hit, 2.5f)
                    && (!found || hit.point.y > best.point.y)) { best = hit; found = true; }
            if (found && best.normal.y > .85f && Mathf.Abs(best.point.y - point.y) < tolerance) return;
            findings.Add(new Finding { category = category, site = site, expected = point, actual = best.point,
                hit = found, normal = best.normal, difference = found ? best.point.y - point.y : 999, triangle = found ? best.triangleIndex : -1 });
        }
        foreach (var site in UnityEngine.Object.FindObjectsByType<ParkingSite>())
            foreach (var point in site.surfaceChecks) Inspect("parking", site.osmId.ToString(), point, site.surfaces, .035f);
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        foreach (var point in access.entranceLandingChecks) Inspect("entrance", "landing", point, access.surfaces, .025f);
        Directory.CreateDirectory("street_qa");
        File.WriteAllText("street_qa/paving_editor_audit.json", JsonUtility.ToJson(new Result { findings = findings.ToArray() }, true));
        Debug.Log("PAVING_AUDIT findings=" + findings.Count);
    }

    public static void BuildVerified()
    {
        Check();
        var result = JsonUtility.FromJson<Result>(File.ReadAllText("street_qa/paving_editor_audit.json"));
        if (result.findings.Length > 0) throw new InvalidOperationException("Pavement joins need correction: " + result.findings.Length);
        StreetBuilder.BuildGeneratedScene();
    }
}
