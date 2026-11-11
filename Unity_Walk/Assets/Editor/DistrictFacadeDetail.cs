using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class DistrictFacadeDetail
{
    private const string GroupName = "Дворы · посадки у подходов";

    public static void ApplyAndBuild()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Street.unity");
        string layout = FullVisualAudit.LayoutHash();
        var renderers = Object.FindObjectsByType<MeshRenderer>();

        var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct();
        int tuned = 0;
        foreach (var material in materials)
        {
            string path = AssetDatabase.GetAssetPath(material);
            if (!path.StartsWith("Assets/Art/PaintedDistrict/", StringComparison.Ordinal)) continue;
            if (material.shader.name == "Street/TexturedSurface" &&
                (path.Contains("_CreamBrick_") || path.Contains("_RedBrick_")))
            {
                // A small warm diffuse fill on masonry preserves the existing sun and ground response.
                material.SetColor("_EmissionColor", path.Contains("_CreamBrick_")
                    ? new Color(.142f,.112f,.085f) : new Color(.123f,.074f,.055f));
                material.SetFloat("_OcclusionStrength", .34f);
                material.SetFloat("_BumpStrength", .28f);
            }
            else if (material.shader.name == "Street/Glass")
            {
                material.SetFloat("_ReflectionStrength", 1.18f);
                material.SetFloat("_InteriorDetail", .26f);
            }
            else continue;
            EditorUtility.SetDirty(material);
            tuned++;
        }

        var sources = Object.FindObjectsByType<LODGroup>().Where(g => g.size is > .55f and < 3.2f &&
            g.GetLODs().Length >= 2 && g.GetLODs()[0].renderers.Any(r => r &&
                r.sharedMaterials.Any(m => m && m.name.StartsWith("HedgeLeaves", StringComparison.Ordinal))))
            .OrderBy(g => g.name, StringComparer.Ordinal).ThenBy(g => g.transform.position.x).ToArray();
        if (sources.Length == 0 || tuned == 0) throw new InvalidOperationException("Saved facade or shrub assets missing.");
        var ground = Object.FindObjectsByType<MeshCollider>().Single(c => c.name == "Grass" && c.bounds.size.x > 1000);
        var old = GameObject.Find(GroupName);
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject(GroupName);
        var occupied = Object.FindObjectsByType<LODGroup>().Select(g => g.transform.position).ToList();
        Physics.SyncTransforms();
        int planted = 0;
        // Anchors flank approach lawns visible in the western and eastern courtyard inspection views.
        foreach (var anchor in new[] { new Vector3(-94,0,14), new Vector3(-90,0,17), new Vector3(-77,0,18),
            new Vector3(219,0,30), new Vector3(217,0,39), new Vector3(214,0,47),
            new Vector3(166,0,104), new Vector3(168,0,108) })
        {
            Vector3 site = default;
            bool found = false;
            for (int ring = 0; ring <= 3 && !found; ring++)
                for (int bearing = 0; bearing < 8 && !found; bearing++)
                {
                    float angle = bearing * Mathf.PI / 4;
                    Vector3 p = anchor + new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)) * (ring * 1.4f);
                    if (occupied.Any(v => (new Vector2(v.x-p.x,v.z-p.z)).sqrMagnitude < 3.24f)) continue;
                    if (!OnGround(p, out var hit) ||
                        !OnGround(p+Vector3.right*.82f,out _) || !OnGround(p-Vector3.right*.82f,out _) ||
                        !OnGround(p+Vector3.forward*.82f,out _) || !OnGround(p-Vector3.forward*.82f,out _)) continue;
                    site = hit.point;
                    found = true;
                }
            if (!found) continue;
            var source = sources[planted % sources.Length];
            var shrub = Object.Instantiate(source.gameObject, site, Quaternion.Euler(0,planted*67,0), root.transform);
            shrub.name = $"Посадка {planted+1:00}";
            shrub.transform.localScale = source.transform.lossyScale * (.82f + .07f * (planted % 4));
            foreach (var collider in shrub.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var renderer in shrub.GetComponentsInChildren<Renderer>(true))
                renderer.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.Off;
            occupied.Add(site);
            planted++;
        }
        if (planted < 4) throw new InvalidOperationException($"Only {planted} safe planting sites.");
        if (layout != FullVisualAudit.LayoutHash() || Object.FindObjectsByType<StreetVehicle>(FindObjectsInactive.Include).Length != 0)
            throw new InvalidOperationException("District layout or vehicle inventory changed.");
        DistrictSurfaceAuditStage04.Check();
        PaintedProfileAudit.Verify();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Street save failed.");
        AssetDatabase.SaveAssets();
        Debug.Log($"DISTRICT_DETAIL materials={tuned} shrubs={planted} layout={layout}");
        StreetBuilder.Build();

        bool OnGround(Vector3 p, out RaycastHit hit) =>
            Physics.Raycast(p+Vector3.up*3, Vector3.down,out hit,5,~0,QueryTriggerInteraction.Ignore) &&
            hit.collider == ground && hit.normal.y > .9f;
    }
}
