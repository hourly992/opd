using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

internal static class PaintedProfileAudit
{
    internal static void Verify()
    {
        Physics.SyncTransforms();
        var network = UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
        var drive = network.surfaces[0];
        var walk = network.surfaces[1];
        var kerb = network.surfaces[2];
        var mesh = walk.sharedMesh;
        var materials = walk.GetComponent<MeshRenderer>().sharedMaterials;
        var vertices = mesh.vertices;
        var rows = new List<string> { "x,z,roadY,vergeY,sidewalkY" };
        var roadToVerge = new List<float>();
        var vergeToWalk = new List<float>();
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i].name != "RoadVergeSurface") continue;
            int[] tris = mesh.GetTriangles(i);
            for (int j = 0; j < tris.Length; j += 27)
            {
                var a = walk.transform.TransformPoint(vertices[tris[j]]);
                var b = walk.transform.TransformPoint(vertices[tris[j+1]]);
                var c = walk.transform.TransformPoint(vertices[tris[j+2]]);
                var p = (a + b + c) / 3;
                if (Mathf.Abs(Vector3.Cross(b-a,c-a).normalized.y) < .9f || p.y < .08f) continue;
                var road = default(RaycastHit);
                Vector3 towardRoad = Vector3.zero;
                for (float distance = .25f; distance <= 4f && towardRoad == Vector3.zero; distance += .25f)
                    for (int bearing = 0; bearing < 12; bearing++)
                    {
                        float angle = bearing * Mathf.PI / 6;
                        var direction = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                        var start = p + direction * distance + Vector3.up * 1.1f;
                        if (!drive.Raycast(new Ray(start, Vector3.down), out road, 1.5f)) continue;
                        towardRoad = direction;
                        break;
                    }
                if (towardRoad == Vector3.zero) continue;
                for (float distance = .25f; distance <= 5.0f; distance += .25f)
                {
                    var start = p - towardRoad * distance + Vector3.up * 1.1f;
                    var ray = new Ray(start, Vector3.down);
                    if (!walk.Raycast(ray, out var path, 1.5f) || path.normal.y < .9f) continue;
                    if (kerb.Raycast(ray, out _, 1.5f)) continue;
                    if (path.triangleIndex < 0) continue;
                    int index = path.triangleIndex * 3;
                    int submesh = Enumerable.Range(0, mesh.subMeshCount)
                        .FirstOrDefault(k => index >= mesh.GetIndexStart(k) && index < mesh.GetIndexStart(k) + mesh.GetIndexCount(k));
                    string role = materials[submesh].name;
                    if (!(role.Contains("Footpath") || role.Contains("Pavement") || role.Contains("Pavers") || role.Contains("Paving"))) continue;
                    roadToVerge.Add(p.y - road.point.y);
                    vergeToWalk.Add(path.point.y - p.y);
                    rows.Add(string.Format(CultureInfo.InvariantCulture,"{0:F2},{1:F2},{2:F4},{3:F4},{4:F4}",p.x,p.z,road.point.y,p.y,path.point.y));
                    break;
                }
            }
        }
        if (roadToVerge.Count == 0) throw new InvalidOperationException("No complete roadside cross sections were found.");
        Directory.CreateDirectory("street_qa");
        File.WriteAllLines("street_qa/painted_profile.csv",rows);
        Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "PAINTED_PROFILE samples={0} roadToVerge={1:F3}..{2:F3} vergeToWalk={3:F3}..{4:F3}",
            roadToVerge.Count, roadToVerge.Min(), roadToVerge.Max(), vergeToWalk.Min(), vergeToWalk.Max()));
    }
}
