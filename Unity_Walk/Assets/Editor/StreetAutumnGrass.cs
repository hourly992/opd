using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class StreetAutumnGrass
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    internal const string AssetFolder = "Assets/Art/AutumnGrass";
    private const string CellPrefix = "Трава · ячейка ";
    private const string MaterialPath = AssetFolder + "/Urban autumn blades.mat";
    private const float EdgeClearance = .12f;
    private const float DiagonalClearance = EdgeClearance * .707107f;

    private static readonly Vector2[] SurfaceChecks =
    {
        Vector2.zero, new(EdgeClearance, 0), new(-EdgeClearance, 0),
        new(0, EdgeClearance), new(0, -EdgeClearance),
        new(DiagonalClearance, DiagonalClearance), new(DiagonalClearance, -DiagonalClearance),
        new(-DiagonalClearance, DiagonalClearance), new(-DiagonalClearance, -DiagonalClearance)
    };

    [MenuItem("Street/Grass/Apply autumn grass to saved Street")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var cells = UnityEngine.Object.FindObjectsByType<LODGroup>()
            .Where(group => group.gameObject.scene == scene &&
                group.name.StartsWith(CellPrefix, StringComparison.Ordinal))
            .OrderBy(group => group.name, StringComparer.Ordinal).ToArray();
        if (cells.Length == 0)
            throw new InvalidOperationException("The saved Street scene contains no turf cells.");

        var ground = UnityEngine.Object.FindObjectsByType<MeshCollider>()
            .Single(collider => collider.gameObject.scene == scene && collider.name == "Grass" &&
                collider.sharedMesh && collider.bounds.size.x > 1000);
        Physics.SyncTransforms();

        if (!AssetDatabase.IsValidFolder(AssetFolder))
            AssetDatabase.CreateFolder("Assets/Art", "AutumnGrass");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        int changed = 0, unchanged = 0;
        var tuftCounts = new int[3];
        var bladeCounts = new int[3];
        var meshCounts = new int[3];

        foreach (var cell in cells)
        {
            var lods = cell.GetLODs();
            for (int lod = 0; lod < lods.Length; lod++)
            {
                foreach (var renderer in lods[lod].renderers)
                {
                    if (!renderer || !renderer.TryGetComponent<MeshFilter>(out var filter))
                        throw new InvalidOperationException($"A grass renderer is missing its mesh in {cell.name}.");

                    var slots = renderer.sharedMaterials;
                    int grass = Array.FindIndex(slots, slot => slot && slot.name.StartsWith("LawnBlades", StringComparison.Ordinal));
                    if (grass < 0)
                    {
                        if (material && slots.Contains(material)) unchanged++;
                        continue;
                    }

                    material ??= CreateMaterial(slots[grass]);
                    var original = filter.sharedMesh;
                    if (!StreetAutumnGrassBlades.TryBuild(original, filter.transform, grass, lod,
                            position => GrassSurface(ground, position), out var mesh, out int tufts, out int blades))
                        throw new InvalidOperationException($"Cannot decode grass in {cell.name}, LOD {lod}, {original.name}.");

                    var rendererId = GlobalObjectId.GetGlobalObjectIdSlow(renderer).targetObjectId;
                    string path = $"{AssetFolder}/Cell_{rendererId}.asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (existing)
                    {
                        EditorUtility.CopySerialized(mesh, existing);
                        UnityEngine.Object.DestroyImmediate(mesh);
                        mesh = existing;
                    }
                    else
                        AssetDatabase.CreateAsset(mesh, path);

                    filter.sharedMesh = mesh;
                    slots[grass] = material;
                    renderer.sharedMaterials = slots;
                    EditorUtility.SetDirty(filter);
                    EditorUtility.SetDirty(renderer);
                    tuftCounts[lod] += tufts;
                    bladeCounts[lod] += blades;
                    meshCounts[lod]++;
                    changed++;
                }
            }
        }

        if (changed + unchanged < cells.Length * 2)
            throw new InvalidOperationException("The grass update did not cover the saved LOD cells.");
        if (changed == 0)
        {
            Debug.Log($"STREET_AUTUMN_GRASS already current, cells={cells.Length} filters={unchanged}");
            return;
        }

        int raisedTufts = StreetAutumnRaisedLawns.Apply(scene, material, AssetFolder);
        StreetAutumnGrassSurface.Apply(scene, AssetFolder);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Unable to save the grass changes to Street.");
        Debug.Log($"STREET_AUTUMN_GRASS cells={cells.Length} renderers={changed} " +
            $"lodMeshes={string.Join(",", meshCounts)} lodTufts={string.Join(",", tuftCounts)} " +
            $"lodBlades={string.Join(",", bladeCounts)} raisedTufts={raisedTufts}");
    }

    private static Vector3? GrassSurface(Collider ground, Vector3 position)
    {
        Vector3 root = position;
        foreach (var offset in SurfaceChecks)
        {
            Vector3 above = position + new Vector3(offset.x, .32f, offset.y);
            if (!Physics.Raycast(above, Vector3.down, out var hit, .44f, ~0, QueryTriggerInteraction.Ignore) ||
                hit.collider != ground || hit.normal.y < .65f)
                return null;
            if (offset == Vector2.zero)
                root = hit.point + Vector3.up * .002f;
        }
        return root;
    }

    private static Material CreateMaterial(Material source)
    {
        var material = new Material(source)
        {
            name = "Urban autumn blades",
            doubleSidedGI = true
        };
        material.SetTexture("_MainTex", null);
        material.SetTextureScale("_MainTex", Vector2.one);
        material.SetTextureOffset("_MainTex", Vector2.zero);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Grass", 2f);
        material.SetFloat("_MeadowKeep", 1f);
        material.SetFloat("_AutumnAmount", 0f);
        material.SetFloat("_BumpStrength", 0f);
        material.SetFloat("_WindStrength", .008f);
        material.SetFloat("_AlphaCutoffEnable", 0f);
        material.SetFloat("_DoubleSidedNormalMode", 2f);
        UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material);
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Opaque");
        material.SetShaderPassEnabled("MotionVectors", true);
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    public static void ApplyAndBuild()
    {
        Apply();
        BuildWindows();
    }

    public static void BuildWindows()
    {
        string output = Path.GetFullPath("Build/StreetWindowsHDRPGrass/DuninaGorkavicha.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            locationPathName = output,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Grass Windows build failed: {report.summary.result}");
        Debug.Log($"STREET_AUTUMN_GRASS_BUILD {output}");
    }
}
