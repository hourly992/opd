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

public static class StreetLocalLandmarks
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string Art = "Assets/Art/DuninLandmarks";
    private const string MonumentName = "Памятник А. А. Дунину-Горкавичу";
    private const string CourtLightName = "Тёплый свет · двор №7";
    private const string ReportFolder = "QA_HDRP/dunin-landmarks";
    private static readonly Vector3 PortalPosition = new(-112.31054f, 0, 162.30456f);
    private static readonly Vector3 TowardStreet = new Vector3(-.88f, 0, -.475f).normalized;
    private static readonly Vector2 ApproachHalf = new(1.254f, 1.731f);
    private const float ApproachCenter = 1.10f;

    [Serializable]
    private sealed class LampSite
    {
        public string site;
        public Vector3 position;
        public int globes, lod0, lod1;
    }

    [Serializable]
    private sealed class Report
    {
        public string scene = ScenePath;
        public Vector3 monumentPosition, front;
        public float offsetFromPortal, height, approachSupportRange;
        public int monumentLod0, monumentLod1, terrainDetailsRemoved, meshTrianglesRemoved;
        public int previousStreetSources, totalSources;
        public List<LampSite> court = new();
    }

    public static void Install()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var network = UnityEngine.Object.FindObjectsByType<StreetLampNetwork>().Single();
        var currentLights = network.lamps.Where(lamp => lamp).ToList();
        if (currentLights.Count != 56 || UnityEngine.Object.FindObjectsByType<Transform>()
                .Any(t => t.name == MonumentName))
            throw new InvalidOperationException("The original Yni Viar scene is required.");

        var report = new Report { previousStreetSources = currentLights.Count };
        Physics.SyncTransforms();
        var monument = CreateMonument();
        PlaceMonument(monument, report);
        var lampPole = MaterialFor("CourtCastIron", new Color(.066f, .087f, .078f), .54f, .35f);
        var glass = MaterialFor("WarmLedLens_CourtOpal", new Color(.83f, .76f, .60f), .04f, .53f);
        glass.SetColor("_EmissiveColor", new Color(.025f, .020f, .014f));
        HDMaterial.ValidateMaterial(glass);
        EditorUtility.SetDirty(glass);
        var one = CreateGlobePrefab(false, lampPole, glass);
        var three = CreateGlobePrefab(true, lampPole, glass);
        var template = currentLights.First(lamp => lamp.transform.parent.name == "CourtyardGlobe3m9");
        var court = new GameObject("Шаровые фонари · двор №7").transform;
        court.SetParent(network.transform, false);

        PlaceLight("Островок у скамей", new Vector3(34.60f, 0, 33.77f), 3, three, 13f);
        PlaceLight("Островок напротив входа", new Vector3(42.98f, 0, 43.86f), 3, three, 28f);
        PlaceLight("Северный проход", new Vector3(40.27f, 0, 51.22f), 1, one, 0f);
        PlaceLight("Подход к восточному крыльцу", new Vector3(51.93f, 0, 34.28f), 1, one, 0f);
        network.lamps = currentLights.ToArray();
        network.shadowlessLamps = (network.shadowlessLamps ?? Array.Empty<Light>())
            .Concat(currentLights.Skip(report.previousStreetSources)).ToArray();
        report.totalSources = network.lamps.Length;
        if (report.totalSources != 60) throw new InvalidOperationException("Unexpected source count.");

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("The saved street could not be updated.");
        Directory.CreateDirectory(ReportFolder);
        File.WriteAllText(ReportFolder + "/placements.json", JsonUtility.ToJson(report, true));
        Debug.Log("DUNIN_LANDMARKS_SAVED " + JsonUtility.ToJson(report));

        void PlaceLight(string site, Vector3 target, int globes, GameObject prefab, float angle)
        {
            var support = FindLawnPoint(target, site);
            if (currentLights.Any(light => Horizontal(light.transform.position - support).sqrMagnitude < 2.25f))
                throw new InvalidOperationException("The courtyard position duplicates an existing source: " + site);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, court);
            instance.name = site;
            instance.transform.SetPositionAndRotation(support, Quaternion.Euler(0, angle, 0));
            float height = globes == 3 ? 3.12f : 2.68f;
            var glow = new GameObject(CourtLightName);
            glow.transform.SetParent(instance.transform, false);
            glow.transform.localPosition = Vector3.up * (height - .22f);
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lamp = glow.AddComponent<Light>();
            EditorUtility.CopySerialized(template, lamp);
            var source = template.GetComponent<HDAdditionalLightData>();
            if (source)
                EditorUtility.CopySerialized(source, glow.GetComponent<HDAdditionalLightData>() ??
                    glow.AddComponent<HDAdditionalLightData>());
            lamp.range = globes == 3 ? 9.5f : 7.5f;
            lamp.spotAngle = 104f;
            lamp.intensity *= globes == 3 ? .84f : .58f;
            lamp.enabled = false;
            currentLights.Add(lamp);
            var levels = instance.GetComponent<LODGroup>().GetLODs();
            report.court.Add(new LampSite
            {
                site = site, position = support, globes = globes,
                lod0 = TriangleCount(levels[0].renderers), lod1 = TriangleCount(levels[1].renderers)
            });
        }
    }

    private static GameObject CreateMonument()
    {
        string path = Art + "/Dunin-Gorkavich.fbx";
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (!importer) throw new IOException("Missing Blender export at " + path);
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.generateSecondaryUV = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.SaveAndReimport();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!asset) throw new IOException("Could not load the monument mesh.");

        var granite = MaterialFor("RoseGranite", Color.white, 0, .51f);
        granite.SetTexture("_BaseColorMap", ImportMap("Granite_Albedo", false));
        granite.SetTexture("_NormalMap", ImportMap("Granite_Normal", true));
        granite.SetFloat("_NormalScale", .46f);
        HDMaterial.ValidateMaterial(granite);
        EditorUtility.SetDirty(granite);
        var material = new Dictionary<string, Material>
        {
            { "RoseGranite", granite },
            { "CastBronze", MaterialFor("CastBronze", new Color(.337f,.278f,.206f), .72f,.54f) },
            { "BurnishedBronze", MaterialFor("BurnishedBronze", new Color(.433f,.363f,.256f), .68f,.58f) },
            { "BronzePatina", MaterialFor("BronzePatina", new Color(.247f,.232f,.176f), .64f,.39f) },
            { "RaisedLettering", MaterialFor("RaisedLettering", new Color(.737f,.604f,.396f), .42f,.45f) },
            { "StoneJoint", MaterialFor("StoneJoint", new Color(.398f,.322f,.293f), 0,.20f) },
            { "PavingGrout", MaterialFor("PavingGrout", new Color(.469f,.441f,.411f), 0,.16f) },
            { "MemorialPaver_0", MaterialFor("MemorialPaver_0", new Color(.639f,.436f,.387f), 0,.27f) },
            { "MemorialPaver_1", MaterialFor("MemorialPaver_1", new Color(.560f,.385f,.340f), 0,.27f) },
            { "MemorialPaver_2", MaterialFor("MemorialPaver_2", new Color(.590f,.565f,.528f), 0,.25f) },
            { "MemorialPaver_3", MaterialFor("MemorialPaver_3", new Color(.532f,.509f,.481f), 0,.24f) }
        };
        var root = new GameObject(MonumentName);
        try
        {
            var geo = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            var renderers = geo.GetComponentsInChildren<MeshRenderer>();
            var near = renderers.Where(renderer => !renderer.name.EndsWith("Low", StringComparison.Ordinal)).ToArray();
            var far = renderers.Where(renderer => renderer.name.EndsWith("Low", StringComparison.Ordinal)).ToArray();
            if (near.Length != 4 || far.Length != 4)
                throw new InvalidOperationException("Unexpected monument LODs: " + string.Join(", ", renderers.Select(r => r.name)));
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(source => source &&
                    material.TryGetValue(source.name, out var replacement) ? replacement :
                    throw new InvalidOperationException("Unknown monument material: " + (source ? source.name : "none"))).ToArray();
            }
            // Derive the forward axis from the tiled approach of the imported mesh.
            var approach = near.Single(renderer => renderer.name == "Approach").bounds;
            var sculpture = near.Single(renderer => renderer.name == "Granite").bounds;
            var front = Horizontal(approach.center - sculpture.center);
            geo.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(front)) * geo.transform.localRotation;
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.12f, near), new LOD(.009f, far) });
            lod.fadeMode = LODFadeMode.CrossFade;
            lod.RecalculateBounds();
            AddBox(root, new Vector3(0,.016f,1.10f), new Vector3(2.416f,.072f,3.456f));
            AddBox(root, new Vector3(0,.69f,-.05f), new Vector3(.61f,1.37f,.56f));
            AddBox(root, new Vector3(-.45f,1.87f,-.172f), new Vector3(.53f,2.44f,.26f));
            AddBox(root, new Vector3(.47f,1.32f,-.12f), new Vector3(.28f,1.06f,.27f));
            AddBox(root, new Vector3(.01f,1.93f,.045f), new Vector3(.56f,1.05f,.34f));
            var info = root.AddComponent<MonumentReference>();
            info.identity = MonumentName + ", ул. Дунина-Горкавича, 1";
            info.source = "https://visit-hm.ru/places/216";
            info.latitude = 61.001164;
            info.longitude = 69.060422;
            info.accuracy = "Форма реконструирована по опубликованным фотографиям. Высота, разворот и место относительно метки портала оценочные.";
            return PrefabUtility.SaveAsPrefabAsset(root, Art+"/Dunin-Gorkavich.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void PlaceMonument(GameObject prefab, Report report)
    {
        var face = Quaternion.LookRotation(TowardStreet);
        var tangent = face * Vector3.right;
        // The straight line from the portal marker to the pavement passes through the existing spruce crown.
        var visiblePatch = -tangent * 4f - TowardStreet * 2f;
        var range = new List<Vector3>();
        for (int x = -12; x <= 12; x++)
            for (int z = -6; z <= 7; z++)
                if (x*x + z*z <= 144)
                    range.Add(tangent * (x*.40f) + TowardStreet * (z*.40f));
        var floors = new List<float>();
        foreach (var delta in range.OrderBy(offset => (offset-visiblePatch).sqrMagnitude))
        {
            var center = PortalPosition + delta;
            floors.Clear();
            for (int x = -2; x <= 2; ++x)
                for (int z = -3; z <= 3; ++z)
                {
                    var sample = center + face * new Vector3(ApproachHalf.x*.46f*x, 0,
                        ApproachCenter + ApproachHalf.y*.315f*z);
                    if (TryGround(sample, out var floor) && IsLawn(floor))
                        floors.Add(floor.point.y);
                }
            if (floors.Count != 35 || floors.Max() - floors.Min() > .047f) continue;
            center.y = floors.Max() + .002f;
            var clearance = center + face * new Vector3(0, 1.8f, ApproachCenter);
            if (Physics.CheckBox(clearance, new Vector3(1.24f,1.55f,1.73f), face,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(center, face);
            report.monumentPosition = center;
            report.front = TowardStreet;
            report.offsetFromPortal = delta.magnitude;
            report.approachSupportRange = floors.Max()-floors.Min();
            var levels = instance.GetComponent<LODGroup>().GetLODs();
            report.monumentLod0 = TriangleCount(levels[0].renderers);
            report.monumentLod1 = TriangleCount(levels[1].renderers);
            report.height = levels[0].renderers.Max(renderer => renderer.bounds.max.y)-center.y;
            if (report.height is < 3.22f or > 3.33f || report.monumentLod1 >= report.monumentLod0)
                throw new InvalidOperationException("Incorrect monument scale or LOD.");
            RemoveGrassUnderApproach(instance.transform, report);
            return;
        }
        throw new InvalidOperationException("A collision-free site on the lawn near the tourist portal marker was not found.");
    }

    private static Vector3 FindLawnPoint(Vector3 at, string site)
    {
        var offsets = new List<Vector3>();
        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
                offsets.Add(new Vector3(x*.19f, 0, z*.19f));
        foreach (var offset in offsets.OrderBy(value => value.sqrMagnitude))
        {
            var sample = at + offset;
            if (!TryGround(sample, out var hit) || !IsLawn(hit)) continue;
            var foot = hit.point;
            if (Physics.CheckCapsule(foot+Vector3.up*.28f, foot+Vector3.up*3.22f, .205f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                continue;
            if (new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }.All(dir =>
                    TryGround(foot+dir*.15f, out var edge) && Mathf.Abs(edge.point.y-foot.y) <= .036f && IsLawn(edge)))
                return foot;
        }
        throw new InvalidOperationException("No supported position near the photographed lawn: " + site);
    }

    private static bool TryGround(Vector3 at, out RaycastHit hit) =>
        Physics.Raycast(new Vector3(at.x, 3.6f, at.z), Vector3.down, out hit, 4.1f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.normal.y > .80f;

    private static bool IsLawn(RaycastHit hit)
    {
        if (hit.collider.name == "Grass" && hit.collider.bounds.size.x > 1000) return true;
        if (hit.collider is not MeshCollider collider || !collider.sharedMesh || hit.triangleIndex < 0 ||
            !collider.TryGetComponent<MeshRenderer>(out var renderer)) return false;
        int index = hit.triangleIndex;
        var materials = renderer.sharedMaterials;
        for (int slot = 0; slot < collider.sharedMesh.subMeshCount && slot < materials.Length; slot++)
        {
            int count = (int)collider.sharedMesh.GetIndexCount(slot)/3;
            if (index < count)
            {
                var material = materials[slot];
                return material && (material.name is "Lawn ground" or "Raised lawn ground" or "RoadVergeSurface" ||
                    material.HasProperty("_SurfaceClass") && Mathf.Abs(material.GetFloat("_SurfaceClass")-1f)<.3f);
            }
            index -= count;
        }
        return false;
    }

    private static void RemoveGrassUnderApproach(Transform root, Report report)
    {
        var area = new Bounds(root.TransformPoint(new Vector3(0, .35f, ApproachCenter)), new Vector3(5.1f, 2f, 5.1f));
        foreach (var tile in UnityEngine.Object.FindObjectsByType<Terrain>())
        {
            var original = tile.terrainData;
            if (!original || tile.drawHeightmap || original.detailPrototypes.Length == 0 ||
                !new Bounds(tile.transform.position + original.size*.5f, original.size).Intersects(area)) continue;
            var copy = UnityEngine.Object.Instantiate(original);
            var origin = tile.transform.position;
            var min = area.min - origin;
            var max = area.max - origin;
            float stepX = copy.size.x / copy.detailWidth, stepZ = copy.size.z / copy.detailHeight;
            int left = Mathf.Clamp(Mathf.FloorToInt(min.x/stepX), 0, copy.detailWidth-1);
            int bottom = Mathf.Clamp(Mathf.FloorToInt(min.z/stepZ), 0, copy.detailHeight-1);
            int width = Mathf.Min(copy.detailWidth - left, Mathf.CeilToInt(max.x / stepX) - left);
            int height = Mathf.Min(copy.detailHeight - bottom, Mathf.CeilToInt(max.z / stepZ) - bottom);
            if (width <= 0 || height <= 0) { UnityEngine.Object.DestroyImmediate(copy); continue; }
            int removed = 0;
            for (int layer = 0; layer < copy.detailPrototypes.Length; ++layer)
            {
                var cells = copy.GetDetailLayer(left, bottom, width, height, layer);
                for (int x = 0; x < width; x++)
                    for (int z = 0; z < height; z++)
                    {
                        var center = origin + new Vector3((left+x+.5f)*stepX, 0, (bottom+z+.5f)*stepZ);
                        if (!InsideApproach(center, .5f*Mathf.Max(stepX,stepZ))) continue;
                        removed += cells[z,x];
                        cells[z,x] = 0;
                    }
                copy.SetDetailLayer(left, bottom, layer, cells);
            }
            if (removed == 0) { UnityEngine.Object.DestroyImmediate(copy); continue; }
            AssetDatabase.CreateAsset(copy, Art + "/MemorialGrass_" + tile.name + ".asset");
            tile.terrainData = copy;
            report.terrainDetailsRemoved += removed;
        }
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if (!filter.sharedMesh || !filter.sharedMesh.isReadable ||
                !filter.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.bounds.Intersects(area) ||
                !renderer.sharedMaterials.Any(material => material && material.HasProperty("_Grass") &&
                    material.GetFloat("_Grass") >= 1f)) continue;
            var original = filter.sharedMesh;
            var points = original.vertices;
            var inside = points.Select(point => InsideApproach(filter.transform.TransformPoint(point), -.012f)).ToArray();
            if (!inside.Any(value => value)) continue;
            var copy = UnityEngine.Object.Instantiate(original);
            for (int sub = 0; sub < original.subMeshCount; ++sub)
            {
                var tris = original.GetTriangles(sub);
                var kept = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                    if (inside[tris[i]] || inside[tris[i+1]] || inside[tris[i+2]])
                        report.meshTrianglesRemoved++;
                    else
                    {
                        kept.Add(tris[i]); kept.Add(tris[i+1]); kept.Add(tris[i+2]);
                    }
                copy.SetTriangles(kept, sub, false);
            }
            var save = AssetDatabase.GenerateUniqueAssetPath(Art + "/MemorialGrassMesh.asset");
            AssetDatabase.CreateAsset(copy, save);
            filter.sharedMesh = copy;
        }

        bool InsideApproach(Vector3 point, float clearance)
        {
            var local = root.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= ApproachHalf.x + clearance &&
                Mathf.Abs(local.z-ApproachCenter) <= ApproachHalf.y + clearance;
        }
    }

    private static GameObject CreateGlobePrefab(bool triple, Material steel, Material glass)
    {
        var name = triple ? "CourtTripleGlobe" : "CourtSingleGlobe";
        var root = new GameObject(name);
        float top = triple ? 3.12f : 2.68f;
        var levels = new LOD[2];
        for (int index = 0; index < levels.Length; index++)
        {
            var mesh = new EnvironmentMesh();
            int sides = index == 0 ? 16 : 8;
            mesh.Cylinder(steel, Vector3.zero, Vector3.up*.076f, .177f, .170f, sides);
            mesh.Cylinder(steel, Vector3.up*.076f, Vector3.up*.32f, .170f, .122f, sides);
            mesh.Cylinder(steel, Vector3.up*.32f, Vector3.up*.61f, .122f, .078f, sides);
            mesh.Cylinder(steel, Vector3.up*.61f, Vector3.up*(top-.20f), .056f, .036f, sides);
            mesh.Cylinder(steel, Vector3.up*.56f, Vector3.up*.63f, .100f, .085f, sides);
            mesh.Cylinder(steel, Vector3.up*(top-.22f), Vector3.up*(top-.135f), .047f, .071f, sides);
            int rings = index == 0 ? 11 : 6;
            mesh.Ellipsoid(glass, Vector3.up*top, new Vector3(.171f,.165f,.171f), sides, rings);
            if (triple)
                foreach (int sign in new[] { -1, 1 })
                {
                    var start = new Vector3(0, top-.86f, 0);
                    Vector3 previous = start;
                    int pieces = index == 0 ? 12 : 6;
                    for (int step = 1; step <= pieces; step++)
                    {
                        float t = (float)step/pieces;
                        var point = start + new Vector3(sign*.438f*Mathf.Sin(t*Mathf.PI*.5f),
                            .51f*t-.19f*Mathf.Sin(t*Mathf.PI), 0);
                        mesh.Cylinder(steel, previous, point, .023f, .023f, sides);
                        previous = point;
                    }
                    mesh.Cylinder(steel, previous, previous+Vector3.up*.084f,.047f,.062f,sides);
                    mesh.Ellipsoid(glass, previous+Vector3.up*.224f,new Vector3(.156f,.151f,.156f),sides,rings);
                }
            var model = mesh.Create(name + "_LOD" + index, "LOD" + index, Art);
            model.transform.SetParent(root.transform, false);
            levels[index] = new LOD(index == 0 ? .073f : .007f, new[] { model.GetComponent<MeshRenderer>() });
        }
        var lod = root.AddComponent<LODGroup>();
        lod.SetLODs(levels);
        lod.fadeMode = LODFadeMode.CrossFade;
        lod.RecalculateBounds();
        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = Vector3.up * ((top-.26f)*.5f);
        capsule.height = top-.26f;
        capsule.radius = .116f;
        return EnvironmentMesh.SavePrefab(root, name, Art);
    }

    private static Material MaterialFor(string name, Color color, float metallic, float smoothness)
    {
        string path = Art + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("HDRP/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.enableInstancing = true;
        HDMaterial.ValidateMaterial(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D ImportMap(string name, bool normal)
    {
        string path = Art + "/" + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normal;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void AddBox(GameObject root, Vector3 center, Vector3 size)
    {
        var collider = root.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;
    }

    private static Vector3 Horizontal(Vector3 value) => Vector3.ProjectOnPlane(value, Vector3.up);

    private static int TriangleCount(Renderer[] renderers) => renderers.Sum(renderer =>
    {
        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        return Enumerable.Range(0, mesh.subMeshCount).Sum(i => (int)mesh.GetIndexCount(i) / 3);
    });

    [MenuItem("Street/Landmarks/Build saved landmark scene and Windows game")]
    public static void Build()
    {
        const string path = "Build/StreetWindowsHDRPDuninLandmarks/DuninaGorkavicha.exe";
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath, "Assets/Scenes/Street_LeafStudy.unity" },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = Path.GetFullPath(path)
        });
        if (result.summary.result != BuildResult.Succeeded) throw new IOException("The Windows build failed.");
        Debug.Log("DUNIN_LANDMARKS_BUILD " + path);
    }

    public static void InstallAndBuild()
    {
        Install();
        Build();
    }
}
