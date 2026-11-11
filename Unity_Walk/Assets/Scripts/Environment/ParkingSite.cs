using UnityEngine;

// Saved, mapped parking geometry and its inspection data; no runtime construction.
public sealed class ParkingSite : MonoBehaviour
{
    public long osmId;
    public string address, evidenceLevel;
    public string[] sourceUrls;
    [TextArea] public string accuracyNote;
    public Vector3[] boundary, accessRoute, surfaceChecks, accessChecks;
    public Vector3[] transitionChecks;
    public MeshCollider[] surfaces;
    public StreetVehicle[] vehicles;
    public Transform[] inspectionViews;
    public int rejectedVehiclePlacements;
    public int connectedOpenEdgeSegments, rejectedOpenEdgeSegments;
    public bool connectedToServiceRoad, usesExistingSurface;
}
