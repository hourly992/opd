using UnityEngine;

// Reuse the original lamps and cap the number of simultaneous local lights.
public sealed class StreetLampNetwork : MonoBehaviour
{
    public Light[] lamps;
    public int retainedStreetPositions, addedCourtyardPositions;
    public bool forceNight;
    public float visibleDistance = 58f;

    private const int LampBudget = 8;
    private readonly int[] nearest = new int[LampBudget];
    private readonly float[] distances = new float[LampBudget];
    private float[] originalIntensity;
    private StreetDayNight clock;
    private Camera view;
    private float nextSelection;

    private void Start()
    {
        clock = FindFirstObjectByType<StreetDayNight>();
        view = Camera.main;
        originalIntensity = new float[lamps?.Length ?? 0];
        for (int i = 0; i < originalIntensity.Length; i++)
            originalIntensity[i] = lamps[i] ? lamps[i].intensity : 0f;
    }

    private void Update()
    {
        if (originalIntensity == null || originalIntensity.Length == 0)
            return;
        var sun = RenderSettings.sun;
        bool legacyNight = !sun || !sun.enabled || sun.intensity < .18f ||
            Vector3.Dot(sun.transform.forward, Vector3.down) < .10f;
        float weight = forceNight ? 1f : clock ? clock.NightBlend : legacyNight ? 1f : 0f;
        if (Time.unscaledTime >= nextSelection)
        {
            nextSelection = Time.unscaledTime + .35f;
            if (!view) view = Camera.main;
            SelectNearest(weight > .001f);
        }
        if (!clock || weight <= .001f)
            return;
        float level = .085f * weight;
        for (int slot = 0; slot < LampBudget; slot++)
        {
            int i = nearest[slot];
            if (i >= 0 && lamps[i] && lamps[i].enabled)
                lamps[i].intensity = originalIntensity[i] * level;
        }
    }

    private void SelectNearest(bool enabledAtNight)
    {
        float radiusSquared = visibleDistance * visibleDistance;
        for (int slot = 0; slot < LampBudget; slot++)
        {
            nearest[slot] = -1;
            distances[slot] = radiusSquared;
        }
        if (enabledAtNight && view)
        {
            var point = view.transform.position;
            for (int i = 0; i < lamps.Length; i++)
            {
                if (!lamps[i]) continue;
                float distance = (lamps[i].transform.position - point).sqrMagnitude;
                if (distance >= distances[LampBudget - 1]) continue;
                int slot = LampBudget - 1;
                while (slot > 0 && distance < distances[slot - 1])
                {
                    distances[slot] = distances[slot - 1];
                    nearest[slot] = nearest[slot - 1];
                    slot--;
                }
                nearest[slot] = i;
                distances[slot] = distance;
            }
        }

        for (int i = 0; i < lamps.Length; i++)
        {
            var lamp = lamps[i];
            if (!lamp) continue;
            bool selected = false;
            for (int slot = 0; slot < LampBudget && !selected; slot++)
                selected = nearest[slot] == i;
            if (lamp.enabled != selected)
                lamp.enabled = selected;
        }
    }
}
