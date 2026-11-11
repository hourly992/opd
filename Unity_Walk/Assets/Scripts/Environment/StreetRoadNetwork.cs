using UnityEngine;

// References for optional runtime verification of the saved street surfaces.
public sealed class StreetRoadNetwork : MonoBehaviour
{
    public float streetLength;
    public int profileSections, panoramaYear, mappedCrossings;
    public Vector3[] walkingChecks;
    public Vector3[] transitionProbePoints, junctionProbePoints;
    public MeshCollider[] surfaces;
}
