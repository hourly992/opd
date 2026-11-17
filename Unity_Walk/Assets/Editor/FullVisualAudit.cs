using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

internal static class FullVisualAudit
{
    internal const string Output = "street_qa/full_visual";

    [Serializable] private sealed class Inventory
    {
        public string scene, engine, pipeline, colorSpace, layoutHash;
        public string[] scenes, modelSources, materialAssets, textureAssets, meshAssets, runtimeScripts, editorScripts;
        public int renderers, lights, reflectionProbes, lightProbeGroups;
        public Surface[] activeMaterials;
    }

    [Serializable] private sealed class Surface
    {
        public string name, asset, shader, albedo, normal, mask;
        public Vector4 tileMeters;
    }

    [Serializable] private sealed class Pose
    {
        public string name;
        public Vector3 position, scale;
        public Quaternion rotation;
    }

    [Serializable] private sealed class Points
    {
        public string name;
        public Vector3[] points;
    }

    internal static void Save(string label)
    {
        var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
        var materials = renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material)
            .Distinct().OrderBy(material => AssetDatabase.GetAssetPath(material), StringComparer.Ordinal).ToArray();
        var paths = AssetDatabase.GetAllAssetPaths();
        var inventory = new Inventory
        {
            scene = EditorSceneManager.GetActiveScene().path, engine = Application.unityVersion,
            pipeline = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.name : "Built-in Forward",
            colorSpace = PlayerSettings.colorSpace.ToString(), layoutHash = LayoutHash(),
            scenes = Find("t:Scene"), materialAssets = Find("t:Material"), textureAssets = Find("t:Texture"),
            meshAssets = Find("t:Mesh"),
            modelSources = paths.Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
                new[] { ".fbx", ".blend", ".obj" }.Contains(Path.GetExtension(path).ToLowerInvariant())).ToArray(),
            runtimeScripts = paths.Where(path => path.StartsWith("Assets/Scripts/", StringComparison.Ordinal) &&
                path.EndsWith(".cs", StringComparison.Ordinal)).ToArray(),
            editorScripts = paths.Where(path => path.StartsWith("Assets/Editor/", StringComparison.Ordinal) &&
                path.EndsWith(".cs", StringComparison.Ordinal)).ToArray(),
            renderers = renderers.Length, lights = UnityEngine.Object.FindObjectsByType<Light>().Length,
            reflectionProbes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>().Length,
            lightProbeGroups = UnityEngine.Object.FindObjectsByType<LightProbeGroup>().Length,
            activeMaterials = materials.Select(material => new Surface
            {
                name = material.name, asset = AssetDatabase.GetAssetPath(material), shader = material.shader.name,
                albedo = TexturePath(material, "_MainTex"), normal = TexturePath(material, "_BumpMap"),
                mask = TexturePath(material, "_MaskMap"),
                tileMeters = material.HasProperty("_TileMeters") ? material.GetVector("_TileMeters") : default
            }).ToArray()
        };
        Directory.CreateDirectory(Output);
        File.WriteAllText($"{Output}/{label}.json", JsonUtility.ToJson(inventory, true));
        Debug.Log($"FULL_VISUAL_INVENTORY pass={label} scenes={inventory.scenes.Length} " +
                  $"materials={inventory.materialAssets.Length} textures={inventory.textureAssets.Length} " +
                  $"meshes={inventory.meshAssets.Length} activeMaterials={materials.Length} layout={inventory.layoutHash}");
    }

    internal static string LayoutHash()
    {
        var text = new StringBuilder();
        foreach (var building in UnityEngine.Object.FindObjectsByType<StreetBuilding>().OrderBy(b => b.osmId))
            AddPose(building.transform);
        var pilot = UnityEngine.Object.FindAnyObjectByType<PilotBlock>();
        AddPose(pilot.entrances[0].parent.parent);
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        foreach (var route in access.routes.OrderBy(route => route.address, StringComparer.Ordinal))
            AddPoints(route.address, route.points);
        foreach (var site in UnityEngine.Object.FindObjectsByType<PlaygroundSite>().OrderBy(site => site.osmId))
        {
            AddPoints(site.name, site.footprint);
            foreach (var equipment in site.equipment) AddPose(equipment);
        }
        foreach (var site in UnityEngine.Object.FindObjectsByType<ParkingSite>().OrderBy(site => site.osmId))
            AddPoints(site.name, site.boundary);
        foreach (var road in UnityEngine.Object.FindObjectsByType<StreetRoadNetwork>().OrderBy(road => road.name))
        {
            AddPoints(road.name + "/walk", road.walkingChecks);
            AddPoints(road.name + "/junction", road.junctionProbePoints);
        }
        using var algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");

        void AddPose(Transform transform) => text.AppendLine(JsonUtility.ToJson(new Pose
        {
            name = transform.name, position = transform.position, scale = transform.lossyScale, rotation = transform.rotation
        }));
        void AddPoints(string name, Vector3[] points) =>
            text.AppendLine(JsonUtility.ToJson(new Points { name = name, points = points }));
    }

    private static string[] Find(string query) => AssetDatabase.FindAssets(query, new[] { "Assets" })
        .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();

    private static string TexturePath(Material material, string property) =>
        material.HasProperty(property) ? AssetDatabase.GetAssetPath(material.GetTexture(property)) : "";
}
