using UnityEngine;

// Saved geometry and inspection metadata only. No runtime generation or Update.
public sealed class UndergroundParkingEntrance : MonoBehaviour
{
    public string address;
    public bool placementProvisional;
    public float length, clearWidth, depth, maximumGrade;
    public MeshCollider floorAndWalls;
    [TextArea] public string evidence;
}
