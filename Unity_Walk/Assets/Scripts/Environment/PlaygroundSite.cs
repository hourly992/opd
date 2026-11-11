using UnityEngine;

// A mapped playground footprint; equipment is authored scenery until site photographs are available.
// No runtime generation, rigidbody simulation or new player mechanics are introduced.
public sealed class PlaygroundSite : MonoBehaviour
{
    public long osmId;
    public string source;
    public bool equipmentPhotoConfirmed;
    public Vector3[] footprint;
    public MeshCollider ground;
    public Transform[] equipment;
    public Transform[] inspectionViews;
    public Bounds worldBounds;
}
