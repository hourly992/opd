using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// A small, real Unity probe bake. Building envelope boxes approximate bounce and sky
// obstruction; doors, trees and facade recesses are outside this coarse lighting model.
public static class FullVisualProbeBake
{
    private const string GroupName = "Непрямой свет · запечённые пробы района";
    private const string ProxyName = "Temporary probe baking envelopes";
    private const string Folder = FullVisualLighting.Root + "/ProbeBake";

    [Serializable]
    private sealed class Report
    {
        public string method = "Unity Progressive CPU, light probes, BakedIndirect sun, two bounces";
        public string approximation = "Existing building collider envelopes and coarse ground, no trees or facade recesses.";
        public bool passed;
        public int envelopeMeshes, probes, finiteCoefficients, lightmapAtlases;
        public float seconds, minimumL0, maximumL0;
        public string lightingData;
    }

    public static void Bake()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sun = RenderSettings.sun;
        var access = Object.FindAnyObjectByType<ResidentialAccess>();
        if (scene.path != "Assets/Scenes/Street.unity" || !sun || !access)
            throw new InvalidOperationException("Open the main Street scene before baking district light probes.");
        if (Lightmapping.isRunning || GameObject.Find(ProxyName))
            throw new InvalidOperationException("A lighting bake or its temporary geometry is already present.");

        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include)
            .Where(r => r.gameObject.scene == scene).ToArray();
        var flags = renderers.Select(r => GameObjectUtility.GetStaticEditorFlags(r.gameObject)).ToArray();
        Lightmapping.TryGetLightingSettings(out var previousSettings);
        var previousData = Lightmapping.lightingDataAsset;
        var previousProbes = LightmapSettings.lightProbes;
        var oldSunBake = sun.lightmapBakeType;
        var oldGroup = GameObject.Find(GroupName);
        var group = oldGroup ? oldGroup.GetComponent<LightProbeGroup>() : new GameObject(GroupName).AddComponent<LightProbeGroup>();
        var priorPositions = group.probePositions;
        var proxies = new GameObject(ProxyName);
        var report = new Report();
        var start = DateTime.UtcNow;
        bool completed = false;
        try
        {
            foreach (var renderer in renderers)
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                    GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~StaticEditorFlags.ContributeGI);
            var boxes = BuildingBoxes();
            if (boxes.Length < 20)
                throw new InvalidOperationException("The saved building envelope geometry is incomplete.");
            var masonry = Material("EnvelopeMasonry", new Color(.58f,.43f,.29f));
            var ground = Material("GroundBounce", new Color(.32f,.29f,.225f));
            foreach (var box in boxes)
            {
                var proxy = Cube(proxies.transform, masonry);
                proxy.transform.SetPositionAndRotation(box.transform.TransformPoint(box.center), box.transform.rotation);
                proxy.transform.localScale = Vector3.Scale(box.size, box.transform.lossyScale);
                report.envelopeMeshes++;
            }
            var groundSource = GameObject.Find("Grass");
            var bounds = groundSource ? groundSource.GetComponent<Collider>().bounds :
                new Bounds(new Vector3(0,-.01f,0),new Vector3(1200,.02f,1200));
            var floor = Cube(proxies.transform, ground);
            floor.transform.position = new Vector3(bounds.center.x, -.18f, bounds.center.z);
            floor.transform.localScale = new Vector3(Mathf.Max(1000,bounds.size.x), .25f, Mathf.Max(1000,bounds.size.z));
            report.envelopeMeshes++;
            group.probePositions = ProbePositions(access, boxes).ToArray();
            if (group.probePositions.Length < 150)
                throw new InvalidOperationException("Too few outdoor probe positions survived the building envelope filter.");
            EditorUtility.SetDirty(group);

            var settings = new LightingSettings
            {
                name = "DistrictProbeSettings",
                bakedGI = true,
                realtimeGI = false,
                lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                mixedBakeMode = MixedLightingMode.IndirectOnly,
                directSampleCount = 16,
                indirectSampleCount = 64,
                environmentSampleCount = 64,
                lightProbeSampleCountMultiplier = 2,
                maxBounces = 2,
                minBounces = 1,
                lightmapResolution = .2f,
                lightmapMaxSize = 512,
                filteringMode = LightingSettings.FilterMode.None
            };
            string settingsPath = Folder + "/DistrictProbeSettings.lighting";
            var savedSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
            if (savedSettings)
            {
                EditorUtility.CopySerialized(settings, savedSettings);
                Object.DestroyImmediate(settings);
                settings = savedSettings;
            }
            else AssetDatabase.CreateAsset(settings, settingsPath);
            Lightmapping.lightingSettings = settings;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            EditorUtility.SetDirty(sun);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"FULL_VISUAL_GI_BEGIN envelopes={report.envelopeMeshes} probes={group.probePositions.Length}");
            if (!Lightmapping.Bake())
                throw new InvalidOperationException("Unity could not finish the district probe bake.");
            var baked = LightmapSettings.lightProbes;
            if (!baked || baked.count != group.probePositions.Length || !Lightmapping.lightingDataAsset)
                throw new InvalidOperationException("Unity produced no complete baked light probe dataset.");
            report.probes = baked.count;
            report.lightmapAtlases = LightmapSettings.lightmaps.Length;
            report.minimumL0 = float.MaxValue;
            report.maximumL0 = float.MinValue;
            foreach (var sh in baked.bakedProbes)
            {
                for (int color = 0; color < 3; color++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    float value = sh[color,coefficient];
                    if (float.IsNaN(value) || float.IsInfinity(value))
                        throw new InvalidOperationException("Unity produced a non-finite SH coefficient.");
                    report.finiteCoefficients++;
                }
                float l0 = sh[0,0] * .2126f + sh[1,0] * .7152f + sh[2,0] * .0722f;
                report.minimumL0 = Mathf.Min(report.minimumL0,l0);
                report.maximumL0 = Mathf.Max(report.maximumL0,l0);
            }
            if (report.maximumL0 <= .001f || report.maximumL0 - report.minimumL0 < .0001f)
                throw new InvalidOperationException("The baked probes have no useful finite lighting variation.");
            report.lightingData = AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset);
            report.passed = true;
            completed = true;
        }
        finally
        {
            if (proxies) Object.DestroyImmediate(proxies);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i]) GameObjectUtility.SetStaticEditorFlags(renderers[i].gameObject,flags[i]);
            if (!completed)
            {
                sun.lightmapBakeType = oldSunBake;
                Lightmapping.lightingSettings = previousSettings;
                Lightmapping.lightingDataAsset = previousData;
                LightmapSettings.lightProbes = previousProbes;
                if (oldGroup) group.probePositions = priorPositions;
                else Object.DestroyImmediate(group.gameObject);
            }
            report.seconds = (float)(DateTime.UtcNow - start).TotalSeconds;
            Directory.CreateDirectory("street_qa/full_visual");
            File.WriteAllText("street_qa/full_visual/probe_bake.json",JsonUtility.ToJson(report,true));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
        Debug.Log($"FULL_VISUAL_GI probes={report.probes} coefficients={report.finiteCoefficients} " +
                  $"atlases={report.lightmapAtlases} seconds={report.seconds:F1} data={report.lightingData}");
    }

    private static BoxCollider[] BuildingBoxes()
    {
        var boxes = Object.FindObjectsByType<StreetBuilding>().SelectMany(b => b.bodyColliders)
            .OfType<BoxCollider>().ToList();
        var pilot = Object.FindAnyObjectByType<PilotBlock>();
        if (pilot && pilot.entrances.Length > 0)
            boxes.AddRange(pilot.entrances[0].parent.parent.GetComponents<BoxCollider>());
        return boxes.Where(b => b.enabled && !b.isTrigger && b.size.y > 2).Distinct().ToArray();
    }

    private static IEnumerable<Vector3> ProbePositions(ResidentialAccess access, BoxCollider[] buildings)
    {
        var cells = new Dictionary<Vector2Int, Vector3>();
        foreach (var route in access.routes)
        {
            if (route.points == null) continue;
            foreach (var point in route.points)
            {
                var cell = new Vector2Int(Mathf.RoundToInt(point.x/12),Mathf.RoundToInt(point.z/12));
                cells.TryAdd(cell,point);
            }
        }
        foreach (var point in cells.Values)
        foreach (float height in new[] { 1.2f, 4f, 11f })
        {
            var position = point + Vector3.up * height;
            if (!buildings.Any(b => (b.ClosestPoint(position) - position).sqrMagnitude < .01f))
                yield return position;
        }
    }

    private static MeshRenderer Cube(Transform parent, Material material)
    {
        var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
        proxy.transform.SetParent(parent,false);
        Object.DestroyImmediate(proxy.GetComponent<Collider>());
        GameObjectUtility.SetStaticEditorFlags(proxy,StaticEditorFlags.ContributeGI);
        var renderer = proxy.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.receiveGI = ReceiveGI.LightProbes;
        renderer.scaleInLightmap = 0;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        return renderer;
    }

    private static Material Material(string name, Color tint)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Standard")) { name = name };
            AssetDatabase.CreateAsset(material,path);
        }
        material.SetColor("_Color",tint);
        material.SetFloat("_Glossiness",0);
        EditorUtility.SetDirty(material);
        return material;
    }
}
