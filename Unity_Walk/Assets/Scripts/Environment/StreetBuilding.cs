using UnityEngine;

// Saved architectural provenance and QA references; no runtime generation.
public sealed class StreetBuilding : MonoBehaviour
{
    public long osmId;
    public string address;
    public string referenceId;
    [TextArea] public string accuracyNote;
    public int[] sectionStoreys;
    public Bounds localBounds;
    public Collider[] bodyColliders;
}
