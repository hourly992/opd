using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class StreetSketchfabLamps
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string AssetPath = "Assets/ThirdParty/Sketchfab/YniViar/StreetLamp";
    private const string PrefabPath = AssetPath + "/StreetLamp_YniViar.prefab";
    private const string ReportPath = "QA_HDRP/yni-viar-lamps";

    [Serializable]
    private sealed class Placement
    {
        public string original, district;
        public Vector3 originalPosition, position, forward, emitter, roadCenter;
        public float height, scale, footError;
    }

    [Serializable]
    private sealed class Report
    {
        public int street, courtyard, legacy, removedDuplicateSources, registeredSources, modelTriangles;
        public int maxActiveSources = StreetLampNetwork.LampBudget, maxShadowSources = StreetLampNetwork.ShadowBudget;
        public float modelHeight;
        public List<Placement> poles = new();
    }

    private sealed class Model
    {
        public GameObject Prefab;
        public float Height;
        public Vector3 Emitter;
        public int Triangles;
    }

    [MenuItem("Street/Lights/Install Street Lamp by Yni Viar")]
    public static void Install()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var network = UnityEngine.Object.FindObjectsByType<StreetLampNetwork>().Single();
        var previous = network.lamps.Where(lamp => lamp).ToArray();
        var poles = previous.GroupBy(light => light.transform.parent).ToArray();
        var legacy = UnityEngine.Object.FindObjectsByType<MeshFilter>()
            .Where(filter => filter.sharedMesh && filter.sharedMesh.name == "CourtyardLamp" &&
                !filter.transform.IsChildOf(network.transform)).ToArray();
        if (poles.Length != 51 || previous.Length != 80 || legacy.Length != 5 ||
            UnityEngine.Object.FindObjectsByType<Transform>().Any(t => t.name == "Street Lamp · Yni Viar"))
            throw new InvalidOperationException("The original 56 exterior poles could not be verified.");

        var model = CreateModel();
        var report = new Report { modelHeight = model.Height, modelTriangles = model.Triangles };
        var lights = new List<Light>();
        var template = previous.First(light => light.transform.parent.name == "CourtyardGlobe3m9");
        var data = JsonUtility.FromJson<StreetBuilder.Data>(File.ReadAllText("Assets/StreetSource/street.json"));
        var centerline = data.roads.Single(road => road.main).points.Select(point => point.V).ToArray();
        Physics.SyncTransforms();

        foreach (var group in poles)
        {
            var pole = group.Key;
            bool street = pole.name == "StreetLampTwin8m";
            if (!street && pole.name != "CourtyardGlobe3m9")
                throw new InvalidOperationException("Unexpected fixture type: " + pole.name);
            var oldLights = group.ToArray();
            if (oldLights.Length != (street ? 2 : 1))
                throw new InvalidOperationException("Unexpected lighting layout at " + pole.position);
            Vector3 roadCenter = street ? NearestRoadPoint(pole.position, centerline) : Vector3.zero;
            Vector3 forward = street ? Vector3.ProjectOnPlane(roadCenter - pole.position, Vector3.up).normalized :
                pole.forward;
            var light = street
                ? oldLights.OrderByDescending(lamp => Vector3.Dot(lamp.transform.position - pole.position, forward)).First()
                : oldLights[0];
            var placement = Replace(pole, model, forward, light, street ? "street" : "courtyard");
            placement.roadCenter = roadCenter;
            if (street)
                light.transform.rotation = Quaternion.LookRotation(roadCenter - light.transform.position, forward);
            foreach (var duplicate in oldLights.Where(x => x != light))
            {
                duplicate.enabled = false;
                duplicate.gameObject.SetActive(false);
                report.removedDuplicateSources++;
            }
            lights.Add(light);
            report.poles.Add(placement);
            if (street) report.street++;
            else report.courtyard++;
        }
        foreach (var old in legacy)
        {
            if (old.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("A legacy fixture unexpectedly already has a light source.");
            var lampObject = new GameObject("Тёплый свет · включается в сумерках");
            lampObject.transform.SetParent(old.transform, false);
            lampObject.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            var light = lampObject.AddComponent<Light>();
            EditorUtility.CopySerialized(template, light);
            var hdrp = template.GetComponent<HDAdditionalLightData>();
            if (hdrp)
                EditorUtility.CopySerialized(hdrp, lampObject.GetComponent<HDAdditionalLightData>() ??
                    lampObject.AddComponent<HDAdditionalLightData>());
            // Old decorative poles join the existing capped network with the courtyard settings.
            report.poles.Add(Replace(old.transform, model, -old.transform.forward, light, "legacy"));
            lights.Add(light);
            report.legacy++;
        }
        network.lamps = lights.ToArray();
        report.registeredSources = lights.Count;
        if (lights.Count != 56 || report.poles.Any(pole => pole.footError > .006f))
            throw new InvalidOperationException("A lamp base or the registered source count failed verification.");

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unable to save the Sketchfab lamps.");
        Directory.CreateDirectory(ReportPath);
        File.WriteAllText(ReportPath + "/positions.json", JsonUtility.ToJson(report, true));
        Debug.Log($"SKETCHFAB_LAMPS_SAVED street={report.street} courtyard={report.courtyard} legacy={report.legacy}");
    }

    private static Model CreateModel()
    {
        // The source FBX contains the three unmodified source meshes and their UVs.
        var path = AssetPath + "/StreetLamp.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.importLights = false;
        importer.importCameras = false;
        importer.isReadable = false;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!source) throw new InvalidOperationException("The converted Sketchfab model could not be imported.");

        var prefab = new GameObject("Street Lamp · Yni Viar");
        try
        {
            var geometry = (GameObject)PrefabUtility.InstantiatePrefab(source, prefab.transform);
            var parts = geometry.GetComponentsInChildren<MeshRenderer>();
            var pillar = parts.Single(part => part.name == "LampPillar");
            var arm = parts.Single(part => part.name == "LampArm");
            var head = parts.Single(part => part.name == "LampHead");
            pillar.sharedMaterial = MakeMaterial("Pillar", "YniViar_Pillar", false);
            arm.sharedMaterial = MakeMaterial("Arm", "YniViar_Arm", false);
            head.sharedMaterial = MakeMaterial("Head", "WarmLedLens_YniViar", true);
            var bounds = pillar.bounds;
            bounds.Encapsulate(arm.bounds);
            bounds.Encapsulate(head.bounds);
            float height = bounds.size.y;
            if (height < 1f || height < bounds.size.x * 2.5f || height < bounds.size.z * 2.5f)
                throw new InvalidOperationException("Imported model is not upright in Unity.");

            var foot = pillar.bounds.center;
            foot.y = bounds.min.y;
            var lens = head.bounds.center;
            lens.y = head.bounds.min.y - height * .0015f;
            var outward = Vector3.ProjectOnPlane(lens - foot, Vector3.up);
            var toForward = Quaternion.Inverse(Quaternion.LookRotation(outward));
            geometry.transform.localRotation = toForward * geometry.transform.localRotation;
            geometry.transform.localPosition = toForward * (geometry.transform.localPosition - foot);

            var bakedLod = prefab.AddComponent<LODGroup>();
            bakedLod.SetLODs(new[] { new LOD(.0075f, parts) });
            bakedLod.fadeMode = LODFadeMode.CrossFade;
            bakedLod.RecalculateBounds();
            int triangleCount = parts.Sum(part => (int)part.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0)) / 3;
            if (triangleCount != 800) throw new InvalidOperationException("Imported topology differs from the GLB.");
            var asset = PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            if (!asset) throw new IOException("Could not create the shared street-lamp prefab.");
            return new Model { Prefab = asset, Height = height, Emitter = toForward * (lens - foot), Triangles = triangleCount };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(prefab);
        }
    }

    private static Texture2D Texture(string name, bool linear = false, bool normal = false)
    {
        string path = AssetPath + "/Textures/" + name + ".png";
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            throw new IOException("Missing original texture " + name);
        importer.sRGBTexture = !linear;
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.alphaSource = name.EndsWith("_Mask", StringComparison.Ordinal)
            ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material MakeMaterial(string part, string name, bool emissive)
    {
        var shader = Shader.Find("HDRP/Lit");
        if (!shader) throw new InvalidOperationException("HDRP/Lit is missing.");
        string path = AssetPath + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.enableInstancing = true;
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BaseColorMap", Texture(part + "_Albedo"));
        material.SetTexture("_NormalMap", Texture(part + "_Normal", true, true));
        material.SetTexture("_MaskMap", Texture(part + "_Mask", true));
        material.SetFloat("_Metallic", 1f);
        material.SetFloat("_Smoothness", 1f);
        material.SetFloat("_DoubleSidedEnable", 1f);
        if (emissive)
        {
            material.SetTexture("_EmissiveColorMap", Texture(part + "_Emission"));
            material.SetColor("_EmissiveColor", new Color(.035f, .028f, .019f));
        }
        HDMaterial.ValidateMaterial(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Placement Replace(Transform pole, Model model, Vector3 forward, Light light, string kind)
    {
        var originalPosition = pole.position;
        var supported = GroundAt(pole);
        var previousRenderers = pole.GetComponentsInChildren<MeshRenderer>(true);
        float height = previousRenderers.Where(renderer => renderer.enabled)
            .Max(renderer => renderer.bounds.max.y) - originalPosition.y;
        var oldLod = pole.GetComponent<LODGroup>();
        if (oldLod) oldLod.enabled = false;
        foreach (var renderer in previousRenderers)
            renderer.enabled = false;

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model.Prefab, pole);
        visual.name = "Street Lamp · Yni Viar";
        visual.transform.SetPositionAndRotation(supported, Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up)));
        float scale = height / model.Height;
        var parentScale = pole.lossyScale;
        visual.transform.localScale = new Vector3(scale / parentScale.x, scale / parentScale.y, scale / parentScale.z);
        var point = visual.transform.TransformPoint(model.Emitter);
        light.transform.position = point;
        light.enabled = false;
        if (pole.TryGetComponent<CapsuleCollider>(out var capsule))
        {
            float bottom = pole.InverseTransformPoint(supported).y;
            capsule.height = height / Mathf.Abs(parentScale.y);
            capsule.center = new Vector3(capsule.center.x, bottom + capsule.height * .5f, capsule.center.z);
        }
        var newBottom = visual.GetComponentsInChildren<MeshRenderer>().Min(renderer => renderer.bounds.min.y);
        return new Placement { original = pole.name, district = kind, originalPosition = originalPosition,
            position = supported, forward = visual.transform.forward, emitter = point,
            height = height, scale = scale, footError = Mathf.Abs(newBottom - supported.y) };
    }

    private static Vector3 GroundAt(Transform pole)
    {
        Vector3 origin = pole.position;
        var hits = Physics.RaycastAll(origin + Vector3.up * .70f, Vector3.down, 1.45f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        var ground = hits.Where(hit => hit.normal.y > .72f && !hit.transform.IsChildOf(pole) &&
            Mathf.Abs(hit.point.y - origin.y) < .55f).OrderByDescending(hit => hit.point.y).FirstOrDefault();
        if (!ground.collider)
            throw new InvalidOperationException("No verified supporting surface for " + pole.name + " at " + origin);
        return new Vector3(origin.x, ground.point.y, origin.z);
    }

    private static Vector3 NearestRoadPoint(Vector3 position, Vector3[] centerline)
    {
        var atGround = Vector3.ProjectOnPlane(position, Vector3.up);
        float distanceSquared = float.PositiveInfinity;
        var nearest = Vector3.zero;
        for (int i = 1; i < centerline.Length; ++i)
        {
            var start = centerline[i - 1];
            var delta = centerline[i] - start;
            if (delta.sqrMagnitude < .001f) continue;
            float t = Mathf.Clamp01(Vector3.Dot(atGround - start, delta) / delta.sqrMagnitude);
            var point = start + t * delta;
            float square = (point - atGround).sqrMagnitude;
            if (square >= distanceSquared) continue;
            nearest = point;
            distanceSquared = square;
        }
        if (distanceSquared < 1f || distanceSquared > 64f ||
            !Physics.Raycast(nearest + Vector3.up * 1.5f, Vector3.down, out var road, 2.1f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            throw new InvalidOperationException("Could not verify the carriageway at " + position);
        return road.point;
    }

    [MenuItem("Street/Lights/Build Windows game with imported street lamps")]
    public static void Build()
    {
        const string path = "Build/StreetWindowsHDRPYniViar/DuninaGorkavicha.exe";
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath, "Assets/Scenes/Street_LeafStudy.unity" },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = Path.GetFullPath(path)
        });
        if (result.summary.result != BuildResult.Succeeded) throw new IOException("Windows lamp build failed.");
        Debug.Log("SKETCHFAB_LAMPS_BUILD " + path);
    }

    public static void InstallAndBuild()
    {
        Install();
        Build();
    }
}
