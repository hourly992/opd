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
using Object = UnityEngine.Object;
using RendererRayTracing = UnityEngine.Experimental.Rendering.RayTracingMode;

// Adds an editable decoration layer to the saved street. The city and its collision stay in place.
public static partial class StreetFacadeUtilities
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string Art = "Assets/Art/FacadeUtilities";
    private const string Output = "QA_HDRP/facade-utilities";
    private const string RootName = "Фасады · лианы, жёлтые трубы и водостоки";
    private const long House5 = 114788180;
    private const float GasOffset = .265f;
    private const float DrainOffset = .137f;
    // FBX import bakes the handedness but retains the vine mesh's authored vertical axis (-Z).
    private static readonly Quaternion VineUpright = Quaternion.Euler(90, 0, 0);

    [Serializable]
    private sealed class SiteReport
    {
        public long id;
        public string address, kind, sector;
        public bool detailed;
        public int yellowRuns, addedDrains, retainedDrains, vines;
        public float pipeMetres, minPipeHeight, maxPipeHeight;
        public Vector3 center;
    }

    [Serializable]
    private sealed class Report
    {
        public int buildings, detailedBuildings, foregroundMeshes, yellowRuns, addedDrains, retainedDrains,
            vines, nearVineTriangles, farVineTriangles, pipeTriangles, collidersAdded;
        public float pipeMetres;
        public List<SiteReport> houses = new();
        public List<string> incomplete = new();
        public List<Shot> views = new();
    }

    [Serializable]
    private sealed class Shot
    {
        public string name, caption;
        public Vector3 eye, lookAt;
    }

    private sealed class Plan
    {
        internal Site Site;
        internal Run[] Gas;
        internal Drain[] Drains, Existing;
        internal Vine[] Vines;
    }

    private sealed class ArtSet
    {
        internal Material yellow, darkerYellow, drain, bracket, mouth;
        internal Material[] vineMaterials;
        internal Mesh near, far;
        internal Bounds ModelBounds;
    }

    [MenuItem("Street/Facades/Apply vine and pipe decorations")]
    public static void Install()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var art = LoadArt();
        var existingRoot = scene.GetRootGameObjects().SingleOrDefault(o => o.name == RootName);
        var report = new Report { nearVineTriangles = Triangles(art.near), farVineTriangles = Triangles(art.far) };
        var probeRoot = new GameObject("Temporary façade mesh probes") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var sites = FindSites(probeRoot.transform);
            Physics.SyncTransforms();
            var plans = sites.Select(site => PlanDecorations(site, art)).ToArray();
            report.buildings = sites.Length;
            report.detailedBuildings = sites.Count(site => site.Detailed);
            foreach (var plan in plans)
            {
                var building = plan.Site;
                var row = new SiteReport
                {
                    id = building.Source.id, address = building.Source.address, kind = building.Source.kind,
                    center = building.Center, detailed = building.Detailed, sector = Sector(building.Center),
                    yellowRuns = plan.Gas.Length, pipeMetres = plan.Gas.Sum(run => run.Length),
                    addedDrains = plan.Drains.Length, retainedDrains = plan.Existing.Length, vines = plan.Vines.Length,
                    minPipeHeight = plan.Gas.Select(run => run.Height).DefaultIfEmpty().Min(),
                    maxPipeHeight = plan.Gas.Select(run => run.Height).DefaultIfEmpty().Max()
                };
                report.houses.Add(row);
                if (row.yellowRuns == 0 || row.vines == 0 || row.retainedDrains + row.addedDrains == 0)
                    report.incomplete.Add($"{row.id}: {row.kind} {row.address}, pipe={row.yellowRuns}, " +
                        $"vine={row.vines}, downspout={row.retainedDrains + row.addedDrains} at {row.center:F2}");
            }
            report.views = PickViews(plans);
            report.yellowRuns = report.houses.Sum(row => row.yellowRuns);
            report.pipeMetres = report.houses.Sum(row => row.pipeMetres);
            report.addedDrains = report.houses.Sum(row => row.addedDrains);
            report.retainedDrains = report.houses.Sum(row => row.retainedDrains);
            report.vines = report.houses.Sum(row => row.vines);
            WriteReport(report);
            if (report.incomplete.Count != 0)
                throw new InvalidOperationException("The façade plan did not cover these " + report.incomplete.Count +
                    " buildings:\n" + string.Join("\n", report.incomplete));

            if (existingRoot) Object.DestroyImmediate(existingRoot);
            AddDecorations(plans, art, report);
        }
        finally
        {
            Object.DestroyImmediate(probeRoot);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the decorated street.");
        WriteReport(report);
        Debug.Log($"FACADES_SAVED buildings={report.buildings}, yellow={report.yellowRuns}, " +
            $"drains={report.addedDrains}+{report.retainedDrains}, vines={report.vines}");
    }

    private static void AddDecorations(Plan[] plans, ArtSet art, Report report)
    {
        var root = new GameObject(RootName);
        foreach (var section in plans.GroupBy(plan => Cell(plan.Site.Center)).OrderBy(group => group.Key.x).ThenBy(group => group.Key.y))
        {
            var cell = new GameObject($"Фасады · {section.Key.x}, {section.Key.y}").transform;
            cell.SetParent(root.transform, false);
            var pipes = new EnvironmentMesh();
            foreach (var plan in section)
            {
                foreach (var run in plan.Gas) DrawGas(pipes, run, art);
                foreach (var drain in plan.Drains) DrawDrain(pipes, drain, art);
                foreach (var vine in plan.Vines) AddVine(cell, vine, art, plan.Site.Source.id);
            }
            if (section.Any(plan => plan.Gas.Length + plan.Drains.Length > 0))
            {
                var obj = pipes.Create($"FacadePipes_{section.Key.x}_{section.Key.y}",
                    "Жёлтые трубы и зелёные водостоки", Art, true);
                obj.transform.SetParent(cell, false);
                var renderer = obj.GetComponent<MeshRenderer>();
                renderer.rayTracingMode = RendererRayTracing.Off;
                renderer.receiveShadows = true;
                report.foregroundMeshes++;
                report.pipeTriangles += Triangles(obj.GetComponent<MeshFilter>().sharedMesh);
            }
        }
        report.collidersAdded = root.GetComponentsInChildren<Collider>(true).Length;
        if (report.collidersAdded != 0) throw new InvalidOperationException("Façade decorations must be visual-only.");
    }

    private static Vector2Int Cell(Vector3 at) => new(Mathf.FloorToInt(at.x / 72), Mathf.FloorToInt(at.z / 72));

    private static string Sector(Vector3 at)
    {
        if (at.z >= 132) return "north";
        if (at.z <= -79) return "south";
        if (at.x < -48) return "west";
        if (at.x > 102) return "east";
        if (at.z > 29) return "courtyard";
        if (at.x > 23) return "playground";
        return "central";
    }

    private static void WriteReport(Report report)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/placements.json", JsonUtility.ToJson(report, true));
    }

    private static int Triangles(Mesh mesh) =>
        Enumerable.Range(0, mesh.subMeshCount).Sum(i => (int)mesh.GetIndexCount(i) / 3);

    private static ArtSet LoadArt()
    {
        Directory.CreateDirectory(Art + "/Meshes");
        AssetDatabase.Refresh();
        var art = new ArtSet
        {
            near = ImportVineMesh("VineNear"),
            far = ImportVineMesh("VineFar"),
            yellow = Paint("Gas-yellow", new Color(.93f, .69f, .132f), .39f),
            darkerYellow = Paint("Gas-collars", new Color(.68f, .48f, .104f), .34f),
            drain = Paint("Rainwater-green", new Color(.19f, .39f, .27f), .38f),
            bracket = Paint("Plated-steel", new Color(.27f, .29f, .25f), .31f),
            mouth = Paint("Pipe-interior", new Color(.038f, .048f, .042f), .15f),
            vineMaterials = new[]
            {
                Paint("Vine-olive", new Color(.78f, .77f, .61f), .28f),
                Paint("Vine-autumn", new Color(.88f, .73f, .49f), .26f),
                Paint("Vine-green", new Color(.75f, .81f, .65f), .28f)
            }
        };
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/VineAlbedo.jpg");
        if (!texture) throw new IOException("The supplied vine albedo was not imported.");
        var sourceBounds = art.near.bounds;
        art.ModelBounds = new Bounds(VineUpright * sourceBounds.center,
            new Vector3(sourceBounds.size.x, sourceBounds.size.z, sourceBounds.size.y));
        if (art.ModelBounds.size.y is < .85f or > 1.10f || Triangles(art.far) >= Triangles(art.near))
            throw new InvalidOperationException("The vine LOD dimensions or budgets are incorrect: " + art.ModelBounds);
        foreach (var material in art.vineMaterials)
        {
            material.SetTexture("_BaseColorMap", texture);
            material.SetFloat("_DoubleSidedEnable", 1);
            material.SetFloat("_DoubleSidedNormalMode", 1);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
        }
        return art;
    }

    private static Mesh ImportVineMesh(string name)
    {
        string path = $"{Art}/{name}.fbx";
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            throw new IOException("No FBX importer for " + path);
        if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation ||
            !importer.bakeAxisConversion)
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.bakeAxisConversion = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Single();
    }

    private static Material Paint(string name, Color color, float smoothness)
    {
        var shader = Shader.Find("HDRP/Lit");
        if (!shader) throw new InvalidOperationException("HDRP/Lit was unavailable.");
        string path = $"{Art}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", 0);
        material.SetFloat("_Smoothness", smoothness);
        material.enableInstancing = true;
        HDMaterial.ValidateMaterial(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AddVine(Transform parent, Vine vine, ArtSet art, long osmId)
    {
        var root = new GameObject($"Лиана · {osmId} · {vine.Wall.Index}").transform;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(vine.Wall.At(vine.X, vine.Foot, vine.Depth),
            Quaternion.LookRotation(vine.Wall.Out) * Quaternion.Euler(0, vine.Turn, vine.Tilt));
        root.localScale = new Vector3(vine.WidthScale, vine.Height, vine.DepthScale);
        var color = art.vineMaterials[vine.Tint];
        MeshRenderer AddLod(string name, Mesh mesh)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(root, false);
            obj.transform.localRotation = VineUpright;
            obj.transform.localPosition = new Vector3(-art.ModelBounds.center.x, -art.ModelBounds.min.y, -art.ModelBounds.min.z);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = color;
            renderer.rayTracingMode = RendererRayTracing.Off;
            renderer.shadowCastingMode = name == "Вблизи" ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return renderer;
        }
        var group = root.gameObject.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(.065f, new Renderer[] { AddLod("Вблизи", art.near) }),
            new LOD(.009f, new Renderer[] { AddLod("Вдали", art.far) }) });
        group.RecalculateBounds();
    }

    private static List<Shot> PickViews(Plan[] plans)
    {
        var rows = new List<Shot>();
        Add("house5-close", "Дом №5 · нижний ярус и лиана", plans.Where(p => p.Site.Source.id == House5));
        Add("court7-close", "Двор №7 · фасад и простенок", plans.Where(p =>
            p.Site.Detailed && p.Site.Source.address == "7"));
        Add("villa2-close", "Дом №2 · веранда", plans.Where(p => p.Site.Source.id == 110515918));
        Add("villa4-close", "Дом №4 · веранда", plans.Where(p => p.Site.Source.id == 114788560));
        foreach (var sector in plans.GroupBy(p => Sector(p.Site.Center)).OrderBy(g => g.Key))
        {
            string caption = sector.Key switch
            {
                "north" => "Северный сектор", "south" => "Южный сектор", "west" => "Западный сектор",
                "east" => "Восточный сектор", "courtyard" => "Дворовые корпуса",
                "playground" => "Дома у детской площадки", _ => "Центральный сектор"
            };
            Add(sector.Key + "-facade", caption, sector);
        }
        Add("low-house-close", "Малоэтажный дом · новый водосток и жёлтая труба",
            plans.Where(p => !p.Site.Detailed && p.Site.Source.kind == "detached"));
        return rows;

        void Add(string name, string caption, IEnumerable<Plan> candidates)
        {
            var options = candidates.SelectMany(p => p.Vines.Select(v => (plan: p, vine: v)))
                .OrderByDescending(p => p.plan.Gas.Where(r => r.Wall == p.vine.Wall).Sum(r => r.Length))
                .ThenBy(p => (p.plan.Site.Center - new Vector3(22, 0, 18)).sqrMagnitude);
            foreach (var item in options)
            {
                var vine = item.vine;
                var wall = vine.Wall;
                var vineTarget = wall.At(vine.X, vine.Foot + vine.Height * .60f, vine.Depth + .09f);
                var nearbyRun = item.plan.Gas.Where(run => run.Wall == wall)
                    .OrderBy(run => Mathf.Abs((run.From + run.To) * .5f - vine.X)).FirstOrDefault();
                var aim = nearbyRun == null ? vineTarget : Vector3.Lerp(vineTarget,
                    wall.At(Mathf.Clamp(vine.X, nearbyRun.From, nearbyRun.To), nearbyRun.Y, nearbyRun.Depth + GasOffset), .5f);
                if (rows.Any(row => (row.lookAt - aim).sqrMagnitude < 3)) continue;
                float baseDistance = Mathf.Clamp(3.8f + (nearbyRun?.Length ?? 2) * .22f, 4.5f, 8.0f);
                foreach (float distance in new[] { baseDistance, baseDistance * .63f })
                foreach (float along in new[] { 2f, -2f, 0, 3.5f, -3.5f })
                {
                    var eye = aim + wall.Out * distance + wall.Across * along;
                    eye.y = Ground(eye, wall.Start.y) + 1.70f;
                    if (Physics.CheckSphere(eye, .22f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 delta = vineTarget - eye;
                    if (Physics.Raycast(eye, delta.normalized, delta.magnitude - .42f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    rows.Add(new Shot { name = name, caption = caption, eye = eye, lookAt = aim });
                    return;
                }
            }
        }
    }

    [MenuItem("Street/Facades/Build saved scene for Windows")]
    public static void Build() => BuildFor(BuildTarget.StandaloneWindows64, "StreetWindowsHDRPFacades/DuninaGorkavicha.exe");

    private static void BuildFor(BuildTarget target, string name)
    {
        string location = Path.GetFullPath(Path.Combine("Build", name));
        Directory.CreateDirectory(Path.GetDirectoryName(location));
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath, "Assets/Scenes/Street_LeafStudy.unity" },
            locationPathName = location, target = target, subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.CompressWithLz4HC
        });
        if (result.summary.result != BuildResult.Succeeded)
            throw new IOException($"The {target} build failed with {result.summary.totalErrors} errors.");
        Debug.Log($"FACADES_BUILD {target} {result.summary.totalSize} bytes {location}");
    }
}
