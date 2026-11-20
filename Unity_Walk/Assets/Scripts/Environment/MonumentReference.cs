using UnityEngine;

// Records the published reference point and the accuracy of the landmark model.
public sealed class MonumentReference : MonoBehaviour
{
    public string identity, source;
    [TextArea] public string accuracy;
    public double latitude, longitude;
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, .6f, .15f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 2, 2);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 4);
    }
}
