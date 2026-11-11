using UnityEngine;
using System.Linq;

// One controller for the saved lamp network. Daylight has no local-light cost.
public sealed class StreetLampNetwork : MonoBehaviour
{
    public Light[] lamps;
    public int retainedStreetPositions,addedCourtyardPositions;
    public bool forceNight;
    public float visibleDistance=58;
    float nextUpdate;
    void Update()
    {
        if(Time.unscaledTime<nextUpdate)return;nextUpdate=Time.unscaledTime+.5f;
        var sun=RenderSettings.sun;
        bool night=forceNight||!sun||!sun.enabled||sun.intensity<.18f||Vector3.Dot(sun.transform.forward,Vector3.down)<.10f;
        var camera=Camera.main;if(!camera)return;
        var active=night?lamps.Where(l=>l&&(l.transform.position-camera.transform.position).sqrMagnitude<visibleDistance*visibleDistance)
            .OrderBy(l=>(l.transform.position-camera.transform.position).sqrMagnitude).Take(8).ToArray():new Light[0];
        foreach(var lamp in lamps)if(lamp)lamp.enabled=active.Contains(lamp);
    }
}
