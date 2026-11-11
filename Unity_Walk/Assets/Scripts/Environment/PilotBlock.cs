using UnityEngine;

// Scene references only: no collection, currency or other gameplay implemented yet.
public sealed class PilotBlock : MonoBehaviour
{
    public Transform[] entrances;
    public Transform[] inspectionViews;
    public Collider[] containers;
    public Collider bench;
    public Collider tree;
    public Transform containerApproach;
}
