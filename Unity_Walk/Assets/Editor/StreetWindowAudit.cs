using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetWindowAudit
{
    [Serializable] private sealed class Result
    {
        public int sampledPanes, illuminatedPanes, glassRenderers;
        public float illuminatedPercent;
    }

    public static void Measure()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Street.unity", OpenSceneMode.Single);
        var result = new Result();
        var keys = new HashSet<Vector3Int>();
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
        {
            if (renderer.gameObject.scene != scene || !renderer.TryGetComponent<MeshFilter>(out var filter) ||
                !filter.sharedMesh || !filter.sharedMesh.isReadable)
                continue;
            var materials = renderer.sharedMaterials;
            var mesh = filter.sharedMesh;
            var uv = new List<Vector4>();
            mesh.GetUVs(0, uv);
            if (uv.Count != mesh.vertexCount)
                continue;
            var vertices = mesh.vertices;
            for (int slot = 0; slot < Math.Min(materials.Length, mesh.subMeshCount); slot++)
            {
                if (!materials[slot] || materials[slot].shader.name != "Street/Glass")
                    continue;
                result.glassRenderers++;
                foreach (int index in mesh.GetIndices(slot))
                {
                    var coordinate = uv[index];
                    if (Mathf.Abs(coordinate.x) > .0001f || Mathf.Abs(coordinate.y) > .0001f)
                        continue;
                    var origin = filter.transform.TransformPoint(vertices[index]);
                    var key = Vector3Int.RoundToInt(origin * 5f);
                    if (!keys.Add(key))
                        continue;
                    float hint = coordinate.z;
                    float seed = Mathf.Abs(hint) > .000001f
                        ? Mathf.Floor(Mathf.Repeat(hint, 1f) * 65536f + .5f) / 65536f
                        : Vector3.Dot(new Vector3(Mathf.Floor(origin.x * 4f + .13f),
                            Mathf.Floor(origin.y * 4f + .13f), Mathf.Floor(origin.z * 4f + .13f)),
                            new Vector3(.173f, .317f, .427f));
                    float hash = Mathf.Repeat(Mathf.Sin((seed * 53f + 11f) * 127.13f + 7.17f) * 43758.5453f, 1f);
                    result.sampledPanes++;
                    if (hash >= .75f)
                        result.illuminatedPanes++;
                }
            }
        }
        result.illuminatedPercent = 100f * result.illuminatedPanes / Mathf.Max(1, result.sampledPanes);
        Directory.CreateDirectory("QA_HDRP");
        File.WriteAllText("QA_HDRP/window-occupancy.json", JsonUtility.ToJson(result, true));
        Debug.Log($"STREET_WINDOW_OCCUPANCY panes={result.sampledPanes} lit={result.illuminatedPanes} percent={result.illuminatedPercent:F2}");
    }

    public static void MeasureAndBuild()
    {
        foreach (string path in new[] { "Assets/Scenes/Street_LeafStudy.unity", "Assets/Scenes/Street.unity" })
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            StreetNightLighting.Apply(scene);
            if (scene.name == "Street")
                StreetGrassVolumeUpgrade.Apply(scene);
            var clock = UnityEngine.Object.FindFirstObjectByType<StreetDayNight>();
            var settings = new SerializedObject(clock);
            settings.FindProperty("moonLux").floatValue = 4.5f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        Measure();
        StreetAtmosphereUpgrade.Build();
    }
}
