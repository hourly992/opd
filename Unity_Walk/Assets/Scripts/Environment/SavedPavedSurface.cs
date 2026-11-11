using UnityEngine;

// A merged collider can carry both a connection and its original destination.
public sealed class SavedPavedSurface : MonoBehaviour
{
    public bool existingNetworkDestination;
    public string purpose;
}
