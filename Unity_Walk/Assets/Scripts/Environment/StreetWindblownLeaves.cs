using System;
using UnityEngine;

// Short trajectories over paving and beneath tree crowns are baked in the editor.
public sealed class StreetWindblownLeaves : MonoBehaviour
{
    [Serializable]
    public struct Path
    {
        public MeshRenderer renderer;
        public Vector3 from, to;
        public float period, phase;
        public float flightTime, scale;
    }

    public Path[] paths = Array.Empty<Path>();
    private Camera view;

    private void LateUpdate()
    {
        if (!view) view = Camera.main;
        foreach (var path in paths)
        {
            if (!path.renderer || path.period <= 0)
                continue;

            bool descending = path.flightTime > 0;
            float time = Mathf.Repeat(Time.time + path.phase, path.period);
            float travelTime = descending ? path.flightTime : 2.35f;
            bool visible = time < travelTime &&
                (!descending || !view || (view.transform.position - path.to).sqrMagnitude < 32f * 32f);
            if (path.renderer.enabled != visible) path.renderer.enabled = visible;
            if (!visible)
                continue;

            float t = time / travelTime;
            if (descending)
            {
                float flutter = t * 17f + path.phase;
                float taper = Mathf.Sin(t * Mathf.PI);
                var sway = new Vector3(Mathf.Sin(flutter) * .24f, Mathf.Cos(flutter * .7f) * .11f,
                    Mathf.Sin(flutter * .78f) * .17f) * taper;
                var leaf = path.renderer.transform;
                leaf.SetPositionAndRotation(Vector3.Lerp(path.from, path.to, t) + sway,
                    Quaternion.Euler(flutter * 42f, path.phase * 52f + t * 130f, Mathf.Sin(flutter) * 39f));
                leaf.localScale = Vector3.one * (path.scale * Mathf.Clamp01(Mathf.Min(t, 1f - t) * 14f));
                continue;
            }

            float hop = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3)) * .16f * Mathf.Sin(t * Mathf.PI);
            var position = Vector3.Lerp(path.from, path.to, t) + Vector3.up * (.045f + hop);
            var rotation = Quaternion.Euler(t * 465, 33 + t * 140, Mathf.Sin(t * 12) * 22);
            path.renderer.transform.SetPositionAndRotation(position, rotation);
        }
    }

    private void OnDisable()
    {
        foreach (var path in paths)
            if (path.renderer)
                path.renderer.enabled = false;
    }
}
