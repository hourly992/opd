using UnityEngine;

// Evidence belongs to the editable scene; instances are tagged EditorOnly.
public sealed class PhotoSurveyMarker : MonoBehaviour
{
    public string category;
    [TextArea] public string observation;
    [TextArea] public string uncertainty;
    public string[] sourceUrls;
    public Bounds studyBounds;
    public bool geometryProvisional = true;
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = geometryProvisional ? new Color(1,.55f,.08f) : Color.green;
        Gizmos.DrawWireCube(studyBounds.center, studyBounds.size);
    }
}
