using System;
using UnityEngine;

// Saved pedestrian connections to already modelled entrances; no runtime construction.
public sealed class ResidentialAccess : MonoBehaviour
{
    [Serializable] public sealed class Route
    {
        public string address;
        public Vector3[] points;
        public bool usesExistingSurface;
    }
    public Route[] routes;
    public Vector3[] surfaceChecks;
    public Vector3[] entranceLandingChecks;
    public MeshCollider[] surfaces;
    public int entranceCount, connectedEntrances;
    public string[] blockedEntrances;
    public string[] occludedSideDetails;
    [TextArea] public string accuracyNote;
}
