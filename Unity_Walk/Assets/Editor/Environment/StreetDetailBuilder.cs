using UnityEditor;
using UnityEngine;

internal static class StreetDetailBuilder
{
    internal static void Generate(Transform parent)
    {
        var root = new GameObject("Люки · по изученным видам улицы").transform;
        root.SetParent(parent, false);
        var cover = Cover();
        foreach (var position in StreetRoadBuilder.Manholes)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(cover, root);
            instance.transform.localPosition = position + Vector3.up * (StreetRoadBuilder.RoadHeight(position) + .002f);
        }
    }
    private static GameObject Cover()
    {
        Material metal = EnvironmentMesh.Material("ManholeMetal", new Color(.25f, .26f, .24f), 0, .25f, .65f);
        Material recess = EnvironmentMesh.Material("ManholeRecess", new Color(.10f, .11f, .10f));
        var mesh = new EnvironmentMesh();
        mesh.Cylinder(recess, Vector3.zero, Vector3.up * .011f, .365f, .365f, 48);
        mesh.Cylinder(metal, Vector3.up * .006f, Vector3.up * .015f, .331f, .331f, 48);
        for (float x = -.28f; x <= .28f; x += .07f)
        {
            float length = Mathf.Sqrt(.31f * .31f - x * x) * 2;
            mesh.Box(recess, new Vector3(x, .017f, 0), new Vector3(.012f, .003f, length));
            mesh.Box(recess, new Vector3(0, .017f, x), new Vector3(length, .003f, .012f));
        }
        return EnvironmentMesh.SavePrefab(mesh.Create("ManholeCover", "Чугунный люк · решётчатая крышка"), "ManholeCover");
    }
}
