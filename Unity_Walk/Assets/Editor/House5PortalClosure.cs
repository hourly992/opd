using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class House5PortalClosure
{
    private const string Root = "Assets/PhotoReconstruction/Stage14";
    private const string ObjectName = "Дом №5 · закрытые откосы входных ниш";
    private const float ClosureDepth = .414f;
    private const float ClosureThickness = .148f;

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var pilot = UnityEngine.Object.FindAnyObjectByType<PilotBlock>();
        if (scene.path != "Assets/Scenes/Street.unity" || !pilot || pilot.entrances.Length != 5)
            throw new InvalidOperationException("The saved Street scene and its five House 5 entrances are required.");

        var entrances = pilot.entrances.Select(view => view.parent).ToArray();
        var house = entrances[0].parent;
        if (house.gameObject.scene != scene || entrances.Any(entrance => entrance.parent != house))
            throw new InvalidOperationException("The porch markers no longer share the saved House 5 root.");
        var brick = house.GetComponentsInChildren<MeshFilter>().Single(filter => filter.sharedMesh && filter.sharedMesh.name == "House5_10");
        var material = brick.GetComponent<MeshRenderer>().sharedMaterial;
        var plinth = house.Find("Штукатурка цоколя").GetComponent<MeshRenderer>().sharedMaterial;
        var backingProbe = new GameObject("Temporary House 5 brick probe");
        try
        {
            backingProbe.transform.SetParent(brick.transform, false);
            var backing = backingProbe.AddComponent<MeshCollider>();
            backing.sharedMesh = brick.sharedMesh;
            Physics.SyncTransforms();
            VerifyExistingBacking(entrances, backing);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(backingProbe);
        }

        var geometry = new EnvironmentMesh();
        foreach (var entrance in entrances)
        {
            float radius = Radius(entrance);
            // The cream wall ends 36 cm behind the facade. The original red backing
            // starts at 45 cm and falls 4 cm short of each jamb. Bridge both overlaps
            // behind the existing reveals, leaving the face, door and steps untouched.
            foreach (int side in new[] { -1, 1 })
            {
                var center = new Vector3(side * (radius + .01f), 2.04f, -ClosureDepth);
                geometry.Box(material, entrance.localPosition + entrance.localRotation * center,
                    new Vector3(.14f, 4.12f, ClosureThickness), entrance.localRotation);
                // Close the full lower reveal at a grazing angle, from just inside the
                // existing plinth face through the red backing. The arch remains recessed.
                var reveal = new Vector3(side * (radius + .018f), .70f, -.245f);
                geometry.Box(plinth, entrance.localPosition + entrance.localRotation * reveal,
                    new Vector3(.12f, 1.56f, .51f), entrance.localRotation);
            }
        }

        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var oldClosure = house.Find(ObjectName);
        if (oldClosure) UnityEngine.Object.DestroyImmediate(oldClosure.gameObject);
        var closure = geometry.Create("House5PortalJambReturns", ObjectName, Root, true);
        closure.transform.SetParent(house, false);
        var collider = closure.AddComponent<MeshCollider>();
        collider.sharedMesh = closure.GetComponent<MeshFilter>().sharedMesh;
        Physics.SyncTransforms();

        int closedRays = 0;
        foreach (var entrance in entrances)
        {
            float radius = Radius(entrance);
            foreach (int side in new[] { -1, 1 })
            foreach (float height in new[] { .40f, 1.15f })
            {
                var ray = FrontRay(entrance, side * (radius - .018f), height);
                if (!collider.Raycast(ray, out var hit, .9f) || Mathf.Abs(hit.distance - .29f) > .015f)
                    throw new InvalidOperationException("The jamb closure did not cover the original seam at " + entrance.name);
                var origin = entrance.TransformPoint(new Vector3(-side * .70f, 1.65f, 2.28f));
                var target = entrance.TransformPoint(new Vector3(side * (radius - .12f), height, 0));
                if (!collider.Raycast(new Ray(origin, target - origin), out _, Vector3.Distance(origin, target) + .30f))
                    throw new InvalidOperationException("The jamb closure is open from the side at " + entrance.name);
                closedRays += 2;
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"HOUSE5_PORTAL_CLOSURE entrances={entrances.Length} jambs={entrances.Length * 2} closedRays={closedRays}");
    }

    private static void VerifyExistingBacking(Transform[] entrances, MeshCollider backing)
    {
        foreach (var entrance in entrances)
        {
            float radius = Radius(entrance);
            foreach (int side in new[] { -1, 1 })
            foreach (float height in new[] { .40f, 1.15f })
            {
                var face = FrontRay(entrance, side * (radius - .075f), height);
                var seam = FrontRay(entrance, side * (radius - .018f), height);
                if (!backing.Raycast(face, out var hit, .80f) || Mathf.Abs(hit.distance - .75f) > .015f ||
                    backing.Raycast(seam, out _, .80f))
                    throw new InvalidOperationException("The archived House 5 backing seam did not match " + entrance.name);
            }
        }
    }

    private static float Radius(Transform entrance) =>
        Mathf.Abs(entrance.localPosition.z + .58f) < .01f ? 2.55f : 2.85f;

    private static Ray FrontRay(Transform entrance, float x, float y) =>
        new Ray(entrance.TransformPoint(new Vector3(x, y, .30f)), -entrance.forward);
}
