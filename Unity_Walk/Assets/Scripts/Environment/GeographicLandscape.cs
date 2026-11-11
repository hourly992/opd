using System;
using UnityEngine;

// Saved geographic evidence and scene references. No runtime generation or network dependency.
public sealed class GeographicLandscape : MonoBehaviour
{
    [Serializable] public sealed class Patch
    {
        public long osmId;
        public string source;
        public Vector3[] boundary;
        public Transform[] trees, understorey;
    }
    public string sourceSha256;
    public string accuracy = "OSM woodland boundaries; individual stems, species and heights are reconstructed, not surveyed";
    public Patch[] patches;

    public static bool Contains(Vector3[] polygon, Vector3 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            if ((polygon[i].z > p.z) != (polygon[j].z > p.z) &&
                p.x < (polygon[j].x - polygon[i].x) * (p.z - polygon[i].z) / (polygon[j].z - polygon[i].z) + polygon[i].x)
                inside = !inside;
        return inside;
    }
}
