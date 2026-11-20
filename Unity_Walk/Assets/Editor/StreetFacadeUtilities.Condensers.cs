using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

public static partial class StreetFacadeUtilities
{
    private const string CondenserArt = "Assets/Art/WindowCondensers";
    private const string CondenserRoot = "Фасады · наружные блоки кондиционеров";
    private const string CondenserQA = "QA_HDRP/window-condensers";
    private const int CondenserSeed = 771901;
    private static readonly Vector3 CondenserHalfSize = new(.51f, .39f, .19f);

    [Serializable]
    private sealed class CondenserPlacement
    {
        public long id;
        public string address, sector;
        public Vector3 position, window;
        public float sideGap, centerHeight;
        public int model;
    }

    [Serializable]
    private sealed class CondenserReport
    {
        public int seed = CondenserSeed;
        public int buildings, units, models = 2, collidersAdded;
        public List<CondenserPlacement> placements = new();
        public List<Shot> views = new();
    }

    private sealed class CondenserPosition
    {
        internal Wall Wall;
        internal Vector3 Mount, Window;
        internal float Gap;
    }

    [MenuItem("Street/Facades/Place a few outdoor units by windows")]
    public static void InstallCondensers()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var façades = scene.GetRootGameObjects().Single(root => root.name == RootName);
        var old = scene.GetRootGameObjects().SingleOrDefault(root => root.name == CondenserRoot);
        var probe = new GameObject("Temporary window probes") { hideFlags = HideFlags.HideAndDontSave };
        var random = new System.Random(CondenserSeed);
        List<CondenserPosition> placements;
        var report = new CondenserReport();
        try
        {
            var sites = FindSites(probe.transform);
            var pipes = façades.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Жёлтые трубы и зелёные водостоки");
            _ = new WallProbe(probe.transform, pipes);
            var plants = façades.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Вблизи")
                .Select(r => r.bounds).ToArray();
            Physics.SyncTransforms();

            var detailed = sites.Where(site => site.Detailed).OrderBy(_ => random.Next()).ToArray();
            placements = new List<CondenserPosition>();
            PlaceOne(detailed.Single(site => site.Source.id == House5));
            foreach (var site in detailed.Where(site => site.Source.address == "7"))
                if (PlaceOne(site)) break;
            foreach (var site in detailed)
            {
                if (placements.Count >= 11) break;
                if (placements.Any(item => item.Wall.Site == site)) continue;
                PlaceOne(site);
            }

            var lowRise = sites.Where(site => !site.Detailed && site.Source.levels >= 2 &&
                site.Source.kind is "apartments" or "detached" or "dormitory" or "office" or "public")
                .OrderBy(_ => random.Next()).GroupBy(site => Sector(site.Center)).ToArray();
            foreach (var sector in lowRise)
            {
                if (placements.Count >= 17) break;
                foreach (var site in sector)
                    if (PlaceOne(site)) break;
            }
            foreach (var site in lowRise.SelectMany(sector => sector))
            {
                if (placements.Count >= 17) break;
                if (!placements.Any(item => item.Wall.Site == site)) PlaceOne(site);
            }
            foreach (var site in detailed.Where(site => site.Area > 630))
            {
                if (placements.Count >= 20) break;
                if (placements.Any(item => item.Wall.Site == site)) PlaceOne(site);
            }

            report.views = CondenserViews(placements);
            if (placements.Count < 16 || report.views.Count < 4 ||
                !placements.Any(item => item.Wall.Site.Source.id == House5) ||
                !placements.Any(item => item.Wall.Site.Detailed && item.Wall.Site.Source.address == "7"))
                throw new InvalidOperationException($"Window mounting validation found only {placements.Count} units and {report.views.Count} inspection views.");

            bool PlaceOne(Site site)
            {
                foreach (var wall in site.Walls.Where(w => !w.Veranda && w.Width > 3.8f && w.Height >= 5f)
                    .OrderBy(_ => random.Next()))
                {
                    if (placements.Any(item => item.Wall == wall)) continue;
                    var candidates = WindowMounts(wall, plants).Where(candidate =>
                        placements.All(other => (candidate.Mount - other.Mount).sqrMagnitude > 7.0f * 7.0f))
                        .ToArray();
                    if (candidates.Length == 0) continue;
                    placements.Add(candidates[random.Next(candidates.Length)]);
                    return true;
                }
                return false;
            }
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }

        var prefabs = new[] { CondenserPrefab(false), CondenserPrefab(true) };
        if (old) Object.DestroyImmediate(old);
        var parent = new GameObject(CondenserRoot).transform;
        foreach (var placement in placements)
        {
            var site = placement.Wall.Site;
            int model = random.Next(prefabs.Length);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[model], parent);
            instance.name = $"Кондиционер у окна · {site.Source.id}";
            instance.transform.SetPositionAndRotation(placement.Mount, Quaternion.LookRotation(placement.Wall.Out));
            report.placements.Add(new CondenserPlacement
            {
                id = site.Source.id, address = site.Source.address, sector = Sector(site.Center),
                position = placement.Mount, window = placement.Window, model = model,
                sideGap = placement.Gap, centerHeight = placement.Mount.y - placement.Wall.Start.y
            });
        }
        report.units = report.placements.Count;
        report.buildings = report.placements.Select(p => p.id).Distinct().Count();
        report.collidersAdded = parent.GetComponentsInChildren<Collider>().Length;
        if (report.collidersAdded != 0 || report.placements.GroupBy(p => p.id).Any(g => g.Count() > 2))
            throw new InvalidOperationException("Unexpected unit density or colliders.");
        Directory.CreateDirectory(CondenserQA);
        File.WriteAllText(CondenserQA + "/placements.json", JsonUtility.ToJson(report, true));
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save window condensers.");
        Debug.Log($"CONDENSERS_SAVED units={report.units} buildings={report.buildings} views={report.views.Count}");
    }

    private static IEnumerable<CondenserPosition> WindowMounts(Wall wall, Bounds[] plants)
    {
        const float step = .14f, front = 2.2f;
        for (float y = wall.Y(2.65f); y < wall.Y(Mathf.Min(wall.Height - .65f, 9.4f)); y += .67f)
        {
            float x = .66f;
            while (x < wall.Width - .70f)
            {
                if (!Glass(x, y, out var first)) { x += step; continue; }
                float left = x, right = x;
                var last = first;
                x += step;
                while (x < wall.Width - .50f)
                {
                    if (Glass(x, y, out var hit)) { last = hit; right = x; }
                    if (x - right > .31f) break; // The window frame may have a mullion.
                    x += step;
                }
                if (right - left < .42f) continue;
                foreach (int side in new[] { -1, 1 })
                foreach (float gap in new[] { .22f, .36f, .48f })
                {
                    const float halfBody = .40f;
                    float edge = side < 0 ? left : right;
                    float u = edge + side * (halfBody + gap);
                    float height = y - .31f;
                    if (u < .53f || u > wall.Width - .53f || !WallDepth(wall, u, height, false, out float depth)) continue;
                    if (!Supports(u, height, depth)) continue;
                    var mount = wall.At(u, height, depth + .018f);
                    if (mount.y - Ground(mount + wall.Out * .30f, wall.Start.y) < 1.88f) continue;
                    var volumeCenter = mount + wall.Out * .215f - Vector3.up * .055f;
                    var rotation = Quaternion.LookRotation(wall.Out);
                    if (Physics.CheckBox(volumeCenter, CondenserHalfSize, rotation, 1 << 2,
                        QueryTriggerInteraction.Ignore)) continue;
                    if (plants.Any(p => p.Intersects(new Bounds(volumeCenter, Vector3.one * .76f)))) continue;
                    yield return new CondenserPosition
                    {
                        Wall = wall, Mount = mount, Window = side < 0 ? first.point : last.point, Gap = gap
                    };
                    break;
                }
            }
        }

        bool Glass(float x, float y, out RaycastHit hit) =>
            HitFacade(wall, x, y, front, out hit, out var surface) &&
            surface == Surface.Window && Vector3.Dot(hit.normal, wall.Out) > .80f;

        bool Supports(float x, float y, float depth)
        {
            foreach (float lateral in new[] { -.43f, 0, .43f })
            foreach (float rise in new[] { -.36f, .23f })
                if (!WallDepth(wall, x + lateral, y + rise, false, out var d) || Mathf.Abs(depth - d) > .055f)
                    return false;
            return true;
        }
    }

    private static List<Shot> CondenserViews(List<CondenserPosition> placements)
    {
        var views = new List<Shot>();
        Add("window-house5", "Дом №5 · кондиционер возле окна", placements.Where(p => p.Wall.Site.Source.id == House5));
        Add("window-house7", "Двор №7 · кондиционер возле окна",
            placements.Where(p => p.Wall.Site.Detailed && p.Wall.Site.Source.address == "7"));
        foreach (var sector in placements.GroupBy(p => Sector(p.Wall.Site.Center)).OrderBy(g => g.Key))
        {
            string caption = sector.Key switch
            {
                "north" => "Северный сектор · блок у окна", "south" => "Южный сектор · блок у окна",
                "west" => "Западный сектор · блок у окна", "east" => "Восточный сектор · блок у окна",
                "courtyard" => "Дворовые корпуса · блок у окна", "playground" => "Дома у площадки · блок у окна",
                _ => "Центральный сектор · блок у окна"
            };
            Add(sector.Key + "-window", caption, sector);
        }
        return views;

        void Add(string name, string caption, IEnumerable<CondenserPosition> positions)
        {
            foreach (var item in positions.OrderBy(p => p.Mount.y))
            {
                var wall = item.Wall;
                var target = Vector3.Lerp(item.Mount, item.Window, .32f) + wall.Out * .15f;
                foreach (float distance in new[] { 5.3f, 7.3f, 3.9f })
                foreach (float along in new[] { 1.5f, -1.5f, 0f, 2.8f, -2.8f })
                {
                    var eye = target + wall.Out * distance + wall.Across * along;
                    eye.y = Ground(eye, wall.Start.y) + 1.73f;
                    if (Physics.CheckSphere(eye, .25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    var delta = item.Mount + wall.Out * .23f - eye;
                    if (Physics.Raycast(eye, delta.normalized, delta.magnitude - .40f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    views.Add(new Shot { name = name, caption = caption, eye = eye, lookAt = target });
                    return;
                }
            }
        }
    }

    private static GameObject CondenserPrefab(bool small)
    {
        Directory.CreateDirectory(CondenserArt + "/Meshes");
        AssetDatabase.Refresh();
        string name = small ? "CondenserCompact" : "CondenserStandard";
        var shell = SurfacePaint(small ? "Warm-enamel" : "Light-grey-enamel",
            small ? new Color(.77f, .74f, .66f) : new Color(.76f, .79f, .80f), .47f);
        var frame = SurfacePaint("Grille", new Color(.17f, .20f, .20f), .36f);
        var vent = SurfacePaint("Fan-recess", new Color(.075f, .083f, .082f), .28f);
        var blade = SurfacePaint("Fan-blades", new Color(.23f, .25f, .25f), .33f);
        var metal = SurfacePaint("Brackets", new Color(.39f, .41f, .41f), .41f);

        float scale = small ? .88f : 1f;
        var geometry = new EnvironmentMesh();
        Vector3 P(float x, float y, float z) => new Vector3(x, y, z) * scale;
        void Block(Material material, Vector3 center, Vector3 size) =>
            geometry.Box(material, center, size * scale, Quaternion.identity);
        void Tube(Material material, Vector3 a, Vector3 b, float radius, int sides = 6) =>
            geometry.Cylinder(material, a, b, radius * scale, sides: sides);

        Block(shell, P(0, 0, .176f), new Vector3(.78f, .50f, .316f));
        Block(shell, P(0, .251f, .176f), new Vector3(.796f, .022f, .327f));
        Block(metal, P(.227f, .003f, .336f), new Vector3(.211f, .445f, .012f));
        Block(shell, P(.228f, .003f, .344f), new Vector3(.192f, .428f, .012f));
        // A dark fan recess with blades behind the circular protective grille.
        const float center = -.112f;
        Tube(frame, P(center, 0, .336f), P(center, 0, .353f), .217f, 28);
        Tube(vent, P(center, 0, .354f), P(center, 0, .360f), .202f, 28);
        for (int i = 0; i < 5; i++)
        {
            var q = Quaternion.Euler(0, 0, i*72 + 20);
            geometry.Box(blade, P(center, 0, .366f) + q * P(.104f,.013f,0),
                new Vector3(.139f,.053f,.009f) * scale, q);
        }
        Tube(metal, P(center, 0, .372f), P(center, 0, .386f), .047f, 12);
        foreach (float radius in new[] { .064f, .096f, .128f, .162f, .194f })
        for (int i = 0; i < 20; i++)
        {
            float a = i * Mathf.PI / 10, b = (i + 1) * Mathf.PI / 10;
            Tube(metal, P(center + Mathf.Cos(a)*radius, Mathf.Sin(a)*radius, .382f),
                P(center + Mathf.Cos(b)*radius, Mathf.Sin(b)*radius, .382f), .0044f, 4);
        }
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            Tube(metal, P(center + Mathf.Cos(a)*.048f, Mathf.Sin(a)*.048f, .382f),
                P(center + Mathf.Cos(a)*.208f, Mathf.Sin(a)*.208f, .382f), .0048f);
        }
        for (int i = 0; i < 5; i++)
            Block(vent, P(.237f, -.07f - i*.020f, .352f), new Vector3(.106f, .008f, .007f));
        foreach (float side in new[] { -.26f, .26f })
        {
            Block(metal, P(side, -.25f, .016f), new Vector3(.048f, .305f, .026f));
            Block(metal, P(side, -.269f, .181f), new Vector3(.043f, .039f, .347f));
            Tube(metal, P(side, -.393f, .026f), P(side, -.287f, .238f), .016f);
        }
        Tube(shell, P(.34f,-.20f,.14f), P(.455f,-.20f,.134f), .022f, 8);
        Tube(shell, P(.455f,-.20f,.134f), P(.455f,-.15f,.025f), .022f, 8);

        var model = geometry.Create(name, name, CondenserArt, true);
        var meshRenderer = model.GetComponent<MeshRenderer>();
        meshRenderer.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.Off;
        try
        {
            return PrefabUtility.SaveAsPrefabAsset(model, CondenserArt + "/" + name + ".prefab");
        }
        finally { Object.DestroyImmediate(model); }

        Material SurfacePaint(string title, Color value, float smoothness)
        {
            string path = $"{CondenserArt}/{title}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", value);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }
    }

    public static void InstallAndBuildCondensers()
    {
        InstallCondensers();
        BuildCondensers();
    }

    [MenuItem("Street/Facades/Build saved scene with window condensers")]
    public static void BuildCondensers()
    {
        string path = Path.GetFullPath("Build/StreetWindowsHDRPWindowAC/DuninaGorkavicha.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath, "Assets/Scenes/Street_LeafStudy.unity" },
            locationPathName = path, target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.CompressWithLz4
        });
        if (result.summary.result != BuildResult.Succeeded) throw new IOException("The window condenser player build failed.");
        Debug.Log($"CONDENSERS_BUILD {result.summary.totalSize} bytes {path}");
    }
}
