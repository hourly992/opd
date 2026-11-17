using UnityEngine;

// Existing fixtures share one bounded runtime lighting controller.
public sealed class StreetLampNetwork : MonoBehaviour
{
    public Light[] lamps;
    public int retainedStreetPositions, addedCourtyardPositions;
    public bool forceNight;
    public float visibleDistance = 66f;
    [Range(.25f, 2f)] public float nightIntensityMultiplier = 1f;

    public const int LampBudget = 12;
    public const int ShadowBudget = 2;
    private readonly int[] nearest = new int[LampBudget];
    private readonly float[] distances = new float[LampBudget];
    private float[] originalIntensity;
    private float[] currentWeight;
    private bool[] selected;
    private bool[] shadows;
    private StreetDayNight clock;
    private Camera view;
    private float nextSelection;
    private Material[] lensMaterials;
    private Color[] lensBaseEmission;
    private float lastGlow = -1f;

    public int ActiveLampCount { get; private set; }
    public int ShadowLampCount { get; private set; }

    private void Start()
    {
        clock = FindAnyObjectByType<StreetDayNight>();
        view = Camera.main;
        int count = lamps?.Length ?? 0;
        originalIntensity = new float[count];
        currentWeight = new float[count];
        selected = new bool[count];
        shadows = new bool[count];
        for (int i = 0; i < count; i++)
            originalIntensity[i] = lamps[i] ? lamps[i].intensity : 0f;
        PrepareLensMaterials();
    }

    private void Update()
    {
        if (originalIntensity == null || originalIntensity.Length == 0)
            return;
        var sun = RenderSettings.sun;
        bool legacyNight = !sun || !sun.enabled || sun.intensity < .18f ||
            Vector3.Dot(sun.transform.forward, Vector3.down) < .10f;
        float night = forceNight ? 1f : clock ? clock.NightBlend : legacyNight ? 1f : 0f;
        if (Time.unscaledTime >= nextSelection)
        {
            nextSelection = Time.unscaledTime + .25f;
            if (!view) view = Camera.main;
            SelectNearest(night > .001f);
        }

        ActiveLampCount = ShadowLampCount = 0;
        float step = Time.unscaledDeltaTime * 1.8f;
        for (int i = 0; i < lamps.Length; i++)
        {
            var lamp = lamps[i];
            if (!lamp)
                continue;
            currentWeight[i] = Mathf.MoveTowards(currentWeight[i], selected[i] ? 1f : 0f, step);
            float weight = currentWeight[i] * night;
            bool visible = weight > .001f;
            if (lamp.enabled != visible)
                lamp.enabled = visible;
            if (!visible)
                continue;
            lamp.intensity = originalIntensity[i] * nightIntensityMultiplier * weight;
            var shadowMode = shadows[i] && weight > .08f ? LightShadows.Soft : LightShadows.None;
            if (lamp.shadows != shadowMode)
                lamp.shadows = shadowMode;
            ActiveLampCount++;
            if (shadowMode != LightShadows.None) ShadowLampCount++;
        }
        UpdateLensGlow(night);
    }

    private void SelectNearest(bool atNight)
    {
        float radiusSquared = visibleDistance * visibleDistance;
        for (int slot = 0; slot < LampBudget; slot++)
        {
            nearest[slot] = -1;
            distances[slot] = radiusSquared;
        }
        System.Array.Clear(selected, 0, selected.Length);
        System.Array.Clear(shadows, 0, shadows.Length);
        if (atNight && view)
        {
            var point = view.transform.position;
            var direction = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            for (int i = 0; i < lamps.Length; i++)
            {
                if (!lamps[i]) continue;
                var delta = lamps[i].transform.position - point;
                delta.y = 0f;
                float squareDistance = delta.sqrMagnitude;
                if (squareDistance > radiusSquared) continue;
                float forward = squareDistance > .01f ? Vector3.Dot(delta / Mathf.Sqrt(squareDistance), direction) : 1f;
                float score = squareDistance * Mathf.Lerp(1.7f, 1f, Mathf.Clamp01(forward + .25f));
                if (score >= distances[LampBudget - 1]) continue;
                int slot = LampBudget - 1;
                while (slot > 0 && score < distances[slot - 1])
                {
                    distances[slot] = distances[slot - 1];
                    nearest[slot] = nearest[slot - 1];
                    slot--;
                }
                nearest[slot] = i;
                distances[slot] = score;
            }
        }

        int shadowCount = 0;
        Transform shadowPole = null;
        for (int slot = 0; slot < LampBudget; slot++)
        {
            int index = nearest[slot];
            if (index < 0) continue;
            selected[index] = true;
            var pole = lamps[index].transform.parent;
            if (shadowCount >= ShadowBudget || shadowCount > 0 && pole == shadowPole)
                continue;
            shadows[index] = true;
            shadowPole = pole;
            shadowCount++;
        }
    }

    private void PrepareLensMaterials()
    {
        var originals = new System.Collections.Generic.List<Material>();
        var instances = new System.Collections.Generic.List<Material>();
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            var slots = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                var source = slots[i];
                if (!source || !source.name.StartsWith("WarmLedLens", System.StringComparison.Ordinal) ||
                    !source.HasProperty("_EmissionColor"))
                    continue;
                int index = originals.IndexOf(source);
                if (index < 0)
                {
                    index = originals.Count;
                    originals.Add(source);
                    instances.Add(new Material(source) { name = source.name + " · night runtime" });
                }
                slots[i] = instances[index];
                changed = true;
            }
            if (changed) renderer.sharedMaterials = slots;
        }
        lensMaterials = instances.ToArray();
        lensBaseEmission = new Color[lensMaterials.Length];
        for (int i = 0; i < lensMaterials.Length; i++)
            lensBaseEmission[i] = lensMaterials[i].GetColor("_EmissionColor");
    }

    private void UpdateLensGlow(float night)
    {
        if (Mathf.Abs(night - lastGlow) < .001f)
            return;
        lastGlow = night;
        for (int i = 0; i < lensMaterials.Length; i++)
            lensMaterials[i].SetColor("_EmissionColor",
                Color.Lerp(lensBaseEmission[i], new Color(15f, 9.4f, 4.2f), night));
    }

    private void OnDestroy()
    {
        if (lensMaterials == null) return;
        foreach (var material in lensMaterials)
            if (material) Destroy(material);
    }
}
