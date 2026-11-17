using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class StreetApvBake
{
    private const string StreetScenePath = "Assets/Scenes/Street.unity";
    private const string AssetFolder = "Assets/StreetHDRP";
    private const string SetPath = AssetFolder + "/Street District APV.asset";
    private const string SettingsPath = AssetFolder + "/Street APV.lighting";
    private const string VolumeRoot = "HDRP Adaptive Probe Volumes";
    private const int SectorWidth = 72;
    private const int MaximumBakeMinutes = 35;

    private static Scene scene;
    private static ProbeVolumeBakingSet bakingSet;
    private static Camera camera;
    private static RenderTexture warmupTarget;
    private static double started;
    private static DateTime startedUtc;
    private static bool batchBake;

    // For batch mode, omit -quit and -nographics so Unity can drive the asynchronous GPU bake.
    [MenuItem("Street/HDRP/Bake district probe volumes")]
    public static void BakeWindows()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning || AdaptiveProbeVolumes.isRunning)
            throw new InvalidOperationException("Stop Play mode and any current lighting bake first.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        batchBake = Application.isBatchMode;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("APV placement and baking require a graphics device.");

            ConfigurePipelines();
            scene = EditorSceneManager.OpenScene(StreetScenePath, OpenSceneMode.Single);
            camera = SceneObjects<Camera>().FirstOrDefault(item => item.CompareTag("MainCamera"));
            if (!camera)
                throw new InvalidOperationException("No Street player camera found.");

            ConfigureSceneVolumes();
            ConfigureBakeContributors();
            ConfigureLightmapping();

            bakingSet = ConfigureBakingSet();
            SaveSceneAndAssets();
            EditorApplication.delayCall += BeginBake;
        }
        catch (Exception exception)
        {
            Stop(1, exception);
        }
    }

    private static void ConfigurePipelines()
    {
        var assets = AssetDatabase.FindAssets("t:HDRenderPipelineAsset", new[] { AssetFolder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid)))
            .ToArray();
        if (assets.Length == 0 || GraphicsSettings.defaultRenderPipeline is not HDRenderPipelineAsset)
            throw new InvalidOperationException("No Street HDRP configuration is available.");

        foreach (var asset in assets)
        {
            var settings = asset.currentPlatformRenderPipelineSettings;
            settings.lightProbeSystem = RenderPipelineSettings.LightProbeSystem.AdaptiveProbeVolumes;
            settings.probeVolumeMemoryBudget = ProbeVolumeTextureMemoryBudget.MemoryBudgetLow;
            settings.probeVolumeSHBands = ProbeVolumeSHBands.SphericalHarmonicsL1;
            settings.supportProbeVolumeScenarios = false;
            settings.supportProbeVolumeScenarioBlending = false;
            settings.supportProbeVolumeGPUStreaming = false;
            settings.supportProbeVolumeDiskStreaming = false;
            asset.currentPlatformRenderPipelineSettings = settings;
            EditorUtility.SetDirty(asset);
        }
        AssetDatabase.SaveAssets();
    }

    private static void ConfigureSceneVolumes()
    {
        var root = scene.GetRootGameObjects().FirstOrDefault(item => item.name == VolumeRoot) ??
            new GameObject(VolumeRoot);
        var previous = root.GetComponentsInChildren<ProbeVolume>();
        var coverage = FindDistrictSectors();
        var volumeNames = new HashSet<string>();
        foreach (var (sector, verticalBounds) in coverage)
        {
            string label = $"APV {sector.x:+00;-00;00} {sector.y:+00;-00;00}";
            volumeNames.Add(label);
            var child = root.transform.Find(label)?.gameObject ?? new GameObject(label);
            child.transform.SetParent(root.transform, false);
            float floor = verticalBounds.x - 6f;
            float ceiling = verticalBounds.y + 24f;
            child.transform.position = new Vector3((sector.x + .5f) * SectorWidth,
                (floor + ceiling) * .5f, (sector.y + .5f) * SectorWidth);
            var volume = GetOrAdd<ProbeVolume>(child);
            volume.mode = ProbeVolume.Mode.Local;
            volume.size = new Vector3(SectorWidth + 10, ceiling - floor, SectorWidth + 10);
            volume.fillEmptySpaces = true;
        }

        // Retire only stale sectors owned by this tool.
        foreach (var stale in previous)
            if (!volumeNames.Contains(stale.name))
                UnityEngine.Object.DestroyImmediate(stale.gameObject);

        var profile = SceneObjects<Volume>()
            .Where(item => item.enabled && item.isGlobal && item.sharedProfile &&
                item.sharedProfile.TryGet<PhysicallyBasedSky>(out var sky) && sky.active)
            .OrderByDescending(item => item.priority).FirstOrDefault()?.sharedProfile;
        if (!profile)
            throw new InvalidOperationException("No configured physically based sky for baking.");

        var staticSky = SceneObjects<StaticLightingSky>().FirstOrDefault() ?? GetOrAdd<StaticLightingSky>(root);
        staticSky.profile = profile;
        staticSky.staticLightingSkyUniqueID = (int)SkyType.PhysicallyBased;

        if (!profile.TryGet<ProbeVolumesOptions>(out var probeOptions))
        {
            probeOptions = profile.Add<ProbeVolumesOptions>();
            AssetDatabase.AddObjectToAsset(probeOptions, profile);
        }
        probeOptions.leakReductionMode.Override(APVLeakReductionMode.Quality);
        probeOptions.normalBias.Override(.22f);
        probeOptions.viewBias.Override(.12f);
        probeOptions.samplingNoise.Override(.075f);
        EditorUtility.SetDirty(probeOptions);
        EditorUtility.SetDirty(profile);

        var data = GetOrAdd<HDAdditionalCameraData>(camera.gameObject);
        data.customRenderingSettings = true;
        data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.AdaptiveProbeVolume] = true;
        data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.AdaptiveProbeVolume, true);
        Debug.Log($"STREET_APV_COVERAGE sectors={coverage.Count} approxSectorWidth={SectorWidth}m");
    }

    private static Dictionary<Vector2Int, Vector2> FindDistrictSectors()
    {
        var source = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/StreetSource/street.json");
        if (!source)
            throw new InvalidOperationException("Cannot locate the saved district road source.");
        var street = JsonUtility.FromJson<StreetBuilder.Data>(source.text);
        var access = SceneObjects<ResidentialAccess>().FirstOrDefault();
        if (street?.roads == null || access?.routes == null)
            throw new InvalidOperationException("Cannot locate the main street and saved pedestrian routes.");

        var sectors = new Dictionary<Vector2Int, Vector2>();
        void AddPath(IEnumerable<Vector3> waypoints)
        {
            Vector3? previous = null;
            foreach (var point in waypoints)
            {
                var start = previous ?? point;
                int segments = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, point) / 12));
                for (int step = 0; step <= segments; step++)
                {
                    var sample = Vector3.Lerp(start, point, (float)step / segments);
                    // Retain the volume on both sides when a street or route runs along a sector edge.
                    foreach (float x in new[] { sample.x - 12, sample.x + 12 })
                    foreach (float z in new[] { sample.z - 12, sample.z + 12 })
                    {
                        var sector = new Vector2Int(Mathf.FloorToInt(x / SectorWidth), Mathf.FloorToInt(z / SectorWidth));
                        sectors[sector] = sectors.TryGetValue(sector, out var height)
                            ? new Vector2(Mathf.Min(height.x, sample.y), Mathf.Max(height.y, sample.y))
                            : new Vector2(sample.y, sample.y);
                    }
                }
                previous = point;
            }
        }

        foreach (var road in street.roads.Where(road => road.main))
            AddPath(road.points.Select(point => point.V));
        foreach (var route in access.routes)
            if (route?.points != null)
                AddPath(route.points);

        if (sectors.Count == 0)
            throw new InvalidOperationException("The source has no usable district sectors.");
        return sectors;
    }

    private static void ConfigureBakeContributors()
    {
        var volumeBounds = SceneObjects<ProbeVolume>().Select(volume =>
            new Bounds(volume.transform.position, volume.size)).ToArray();
        int contributors = 0;
        int invalidContributors = 0;
        foreach (var renderer in SceneObjects<MeshRenderer>())
        {
            var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            bool hasFaces = renderer.TryGetComponent<MeshFilter>(out var filter) && HasVoxelizableFaces(filter.sharedMesh);
            if (!hasFaces)
            {
                if ((flags & StaticEditorFlags.ContributeGI) != 0)
                {
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags & ~StaticEditorFlags.ContributeGI);
                    invalidContributors++;
                }
                continue;
            }

            var bounds = renderer.bounds;
            bool suitable = renderer.enabled && renderer.gameObject.activeInHierarchy &&
                bounds.size.sqrMagnitude > 1f && volumeBounds.Any(volume => volume.Intersects(bounds)) &&
                renderer.sharedMaterials.Length != 0 && renderer.sharedMaterials.All(IsBakedOpaque);
            if (suitable)
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                    flags | StaticEditorFlags.ContributeGI);
                renderer.receiveGI = ReceiveGI.LightProbes;
                contributors++;
            }
        }

        if (contributors == 0)
            throw new InvalidOperationException("No opaque district renderers contribute to the bake.");
        Debug.Log($"STREET_APV_CONTRIBUTORS count={contributors} excludedIndexless={invalidContributors}");
    }

    private static bool HasVoxelizableFaces(Mesh mesh)
    {
        if (!mesh || mesh.vertexCount < 3 || !mesh.HasVertexAttribute(VertexAttribute.Position))
            return false;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            var submesh = mesh.GetSubMesh(i);
            if (submesh.topology == MeshTopology.Triangles && submesh.indexCount >= 3 ||
                submesh.topology == MeshTopology.Quads && submesh.indexCount >= 4)
                return true;
        }
        return false;
    }

    private static bool IsBakedOpaque(Material material)
    {
        if (!material || material.renderQueue >= 2450)
            return false;
        if (material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") != 0f)
            return false;
        if (material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") != 0f)
            return false;
        return material.shader && material.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static void ConfigureLightmapping()
    {
        var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
        if (!settings)
        {
            settings = new LightingSettings { name = "Street APV" };
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.lightmapper = LightingSettings.Lightmapper.UnityComputeGPU;
        settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
        settings.directSampleCount = 16;
        settings.indirectSampleCount = 64;
        settings.environmentSampleCount = 64;
        settings.lightProbeSampleCountMultiplier = 1;
        settings.minBounces = 1;
        settings.maxBounces = 2;
        Lightmapping.lightingSettings = settings;
        EditorUtility.SetDirty(settings);

        var sun = RenderSettings.sun;
        if (!sun || sun.type != LightType.Directional)
            throw new InvalidOperationException("No saved directional sun configured for the APV bake.");
        sun.lightmapBakeType = LightmapBakeType.Mixed;
    }

    private static ProbeVolumeBakingSet ConfigureBakingSet()
    {
        var set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(SetPath);
        if (!set)
        {
            set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
            // The 17.6 APV Lighting window uses this initializer for all new Baking Sets.
            var initialize = typeof(ProbeVolumeBakingSet).GetMethod("SetDefaults",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (initialize == null)
                throw new InvalidOperationException("The current SRP Core does not expose the APV initializer.");
            initialize.Invoke(set, null);
            AssetDatabase.CreateAsset(set, SetPath);
        }

        var guid = AssetDatabase.AssetPathToGUID(StreetScenePath);
        if (!set.sceneGUIDs.Contains(guid) && !set.TryAddScene(guid))
            throw new InvalidOperationException("Street is assigned to a different APV Baking Set.");
        set.SetSceneBaking(guid, true);
        set.minDistanceBetweenProbes = 3f;
        set.simplificationLevels = 2;
        set.minRendererVolumeSize = .5f;
        set.skyOcclusion = true;
        set.skyOcclusionBakingSamples = 128;
        set.skyOcclusionBakingBounces = 1;
        set.skyOcclusionAverageAlbedo = .38f;
        set.skyOcclusionShadingDirection = false;

        var serialized = new SerializedObject(set);
        var dilation = serialized.FindProperty("settings.dilationSettings");
        if (dilation != null)
        {
            dilation.FindPropertyRelative("enableDilation").boolValue = true;
            dilation.FindPropertyRelative("dilationDistance").floatValue = 3.5f;
            dilation.FindPropertyRelative("dilationValidityThreshold").floatValue = .25f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(set);
        return set;
    }

    private static void BeginBake()
    {
        try
        {
            RenderTinyFrame();
            if (!ProbeReferenceVolume.instance.isInitialized)
                throw new InvalidOperationException("HDRP did not initialize adaptive probe volumes.");
            started = EditorApplication.timeSinceStartup;
            startedUtc = DateTime.UtcNow;
            if (!AdaptiveProbeVolumes.BakeAsync())
                throw new InvalidOperationException("Unity could not start the APV bake. Check APV placement and Baking Set registration.");
            EditorApplication.update += WaitForBake;
            Debug.Log($"STREET_APV_STARTED backend={Lightmapping.lightingSettings.lightmapper}");
        }
        catch (Exception exception)
        {
            Stop(1, exception);
        }
    }

    private static void WaitForBake()
    {
        try
        {
            if (AdaptiveProbeVolumes.isRunning)
            {
                if (EditorApplication.timeSinceStartup - started < MaximumBakeMinutes * 60)
                    return;
                AdaptiveProbeVolumes.Cancel();
                throw new TimeoutException("Street APV lighting did not bake within the configured time.");
            }

            if (!bakingSet.HasBakedData() || !SceneObjects<ProbeVolumePerSceneData>().Any(data => data.bakingSet == bakingSet))
                throw new InvalidOperationException("The APV baking set has no completed lighting data for Street.");
            SaveSceneAndAssets();
            RenderTinyFrame();

            var cells = new SerializedObject(bakingSet).FindProperty("cellDescs.m_Values");
            int cellCount = cells?.arraySize ?? 0;
            long probes = 0;
            for (int i = 0; i < cellCount; i++)
                probes += cells.GetArrayElementAtIndex(i).FindPropertyRelative("probeCount").intValue;
            var dataFiles = Directory.EnumerateFiles(AssetFolder, "Street District APV*.bytes")
                .Select(path => new FileInfo(path)).ToArray();
            long totalBytes = dataFiles.Sum(file => file.Length);
            bool wroteData = dataFiles.Any(file => file.LastWriteTimeUtc >= startedUtc);
            if (cellCount == 0 || probes == 0 || totalBytes == 0 || !wroteData)
                throw new InvalidOperationException("APV bake did not produce nonempty cell data.");

            bool loaded = ProbeReferenceVolume.instance.DataHasBeenLoaded();
            var settings = HDCamera.GetOrCreate(camera).frameSettings;
            Debug.Log($"STREET_APV_BAKED cells={cellCount} storedProbeSamples={probes} bytes={totalBytes} " +
                $"editorDataLoaded={loaded} cameraFrameSetting={settings.IsEnabled(FrameSettingsField.AdaptiveProbeVolume)} " +
                $"seconds={EditorApplication.timeSinceStartup - started:F1}");
            Stop(0);
        }
        catch (Exception exception)
        {
            Stop(1, exception);
        }
    }

    private static void RenderTinyFrame()
    {
        if (!warmupTarget)
            warmupTarget = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf);
        var request = new RenderPipeline.StandardRequest { destination = warmupTarget };
        if (!RenderPipeline.SupportsRenderRequest(camera, request))
            throw new InvalidOperationException("HDRP does not support the APV warmup camera request.");
        RenderPipeline.SubmitRenderRequest(camera, request);
    }

    private static void SaveSceneAndAssets()
    {
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save the APV scene.");
        AssetDatabase.SaveAssets();
    }

    private static IEnumerable<T> SceneObjects<T>() where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static T GetOrAdd<T>(GameObject obj) where T : Component =>
        obj.TryGetComponent<T>(out var component) ? component : obj.AddComponent<T>();

    private static void Stop(int code, Exception failure = null)
    {
        EditorApplication.update -= WaitForBake;
        if (warmupTarget)
            UnityEngine.Object.DestroyImmediate(warmupTarget);
        if (failure != null)
            Debug.LogException(failure);
        if (batchBake)
            EditorApplication.Exit(code);
    }
}