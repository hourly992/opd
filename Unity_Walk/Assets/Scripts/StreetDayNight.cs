using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(60), DisallowMultipleComponent]
public sealed class StreetDayNight : MonoBehaviour
{
    [Header("Игровые часы")]
    [SerializeField, Min(1)] private float dayLengthMinutes = 24f;
    [SerializeField, Range(0, 24)] private float startHour = 16.8f;
    [SerializeField, Range(0, 24)] private float currentHour = 16.8f;
    [SerializeField] private bool paused;
    [SerializeField, Range(.05f, 32f)] private float timeScale = 1f;
    [Header("Солнце и ночное освещение")]
    [SerializeField, Range(0, 23)] private float sunriseHour = 7f;
    [SerializeField, Range(1, 24)] private float sunsetHour = 19f;
    [SerializeField, Range(15, 80)] private float latitude = 61f;
    [SerializeField, Range(0, 360)] private float northAzimuth = 48f;
    [SerializeField, Range(0, 60)] private float moonLux = 16f;
    [SerializeField, Range(0, 4)] private float nightAmbient = 1f;
    [SerializeField, Range(1, 8)] private float nightExposure = 4.8f;
    [SerializeField] private bool milkyWay = true;
    [SerializeField, Min(0)] private float starRadiance = 1f;
    [Header("Компоненты сцены")]
    [SerializeField] private Light sun;
    [SerializeField] private Light moon;
    [SerializeField] private Light[] daylightFill;
    [SerializeField] private Volume baseVolume;
    [SerializeField] private Volume cycleVolume;
    [SerializeField] private Cubemap starCube;
    [SerializeField] private Cubemap milkyWayCube;

    private HDAdditionalLightData sunData;
    private VolumeProfile runtimeProfile;
    private PhysicallyBasedSky sky;
    private Exposure exposure;
    private IndirectLightingController ambient;
    private Fog fog;
    private VolumetricClouds clouds;
    private WhiteBalance whiteBalance;
    private float[] fillLux;
    private float dayEv = 12.05f, baseIndirect = 1.35f, baseCloud = .4f, baseFogDistance = 385f;
    private Color baseFogColor = new(.88f, .91f, .95f);
    private float baseWhiteBalance;
    private float nextEnvironmentUpdate;
    private float appliedHour = -1f;
    private bool appliedMilkyWay, initialized;

    public Light Sun => sun;
    public float CurrentHour => currentHour;
    public float SunriseHour => sunriseHour;
    public float SunsetHour => sunsetHour;
    public float SunElevation { get; private set; }
    public float StarVisibility { get; private set; }
    public float NightBlend { get; private set; }
    public bool Paused { get => paused; set => paused = value; }

    public void BindScene(Light solarLight, Light lunarLight, Light[] fill, Volume existing, Volume cycle,
        Cubemap stars, Cubemap withMilkyWay)
    {
        sun = solarLight;
        moon = lunarLight;
        daylightFill = fill;
        baseVolume = existing;
        cycleVolume = cycle;
        starCube = stars;
        milkyWayCube = withMilkyWay;
    }

    private void OnValidate()
    {
        startHour = Mathf.Repeat(startHour, 24f);
        sunsetHour = Mathf.Clamp(sunsetHour, sunriseHour + .5f, 24f);
        dayLengthMinutes = Mathf.Max(1f, dayLengthMinutes);
        if (initialized && Application.isPlaying)
            appliedHour = -1f;
    }

    private void Start()
    {
        if (!sun)
            sun = RenderSettings.sun;
        if (!sun || !moon || !cycleVolume || !baseVolume || !starCube || !milkyWayCube ||
            !sun.TryGetComponent(out sunData))
        {
            Debug.LogError("StreetDayNight: incomplete HDRP scene references.", this);
            enabled = false;
            return;
        }
        sunData.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
        RenderSettings.sun = sun;
        fillLux = new float[daylightFill?.Length ?? 0];
        for (int index = 0; index < fillLux.Length; index++)
            fillLux[index] = daylightFill[index] ? daylightFill[index].intensity : 0f;

        var profile = baseVolume.sharedProfile;
        if (profile && profile.TryGet<Exposure>(out var dayExposure)) dayEv = dayExposure.fixedExposure.value;
        if (profile && profile.TryGet<IndirectLightingController>(out var dayIndirect))
            baseIndirect = dayIndirect.indirectDiffuseLightingMultiplier.value;
        if (profile && profile.TryGet<VolumetricClouds>(out var dayClouds))
            baseCloud = dayClouds.densityMultiplier.value;
        if (profile && profile.TryGet<WhiteBalance>(out var dayWhiteBalance))
            baseWhiteBalance = dayWhiteBalance.temperature.value;
        if (profile && profile.TryGet<Fog>(out var dayFog))
        {
            baseFogDistance = dayFog.meanFreePath.value;
            baseFogColor = dayFog.albedo.value;
        }

        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.name = "Street · dynamic time of day";
        cycleVolume.profile = runtimeProfile;
        sky = runtimeProfile.Add<PhysicallyBasedSky>();
        sky.type.Override(PhysicallyBasedSkyModel.EarthAdvanced);
        sky.updateMode.Override(EnvironmentUpdateMode.OnDemand);
        sky.spaceEmissionTexture.Override(milkyWay ? milkyWayCube : starCube);
        sky.spaceEmissionMultiplier.Override(0f);
        exposure = runtimeProfile.Add<Exposure>();
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(dayEv);
        ambient = runtimeProfile.Add<IndirectLightingController>();
        ambient.indirectDiffuseLightingMultiplier.Override(baseIndirect);
        ambient.reflectionLightingMultiplier.Override(1f);
        ambient.reflectionProbeIntensityMultiplier.Override(1f);
        whiteBalance = runtimeProfile.Add<WhiteBalance>();
        whiteBalance.temperature.Override(baseWhiteBalance);
        fog = runtimeProfile.Add<Fog>();
        fog.albedo.Override(baseFogColor);
        fog.meanFreePath.Override(baseFogDistance);
        clouds = runtimeProfile.Add<VolumetricClouds>();
        clouds.densityMultiplier.Override(baseCloud);

        initialized = true;
        float requested = startHour;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-streetPauseTime")
                paused = true;
            if (args[i] == "-streetHour" && i + 1 < args.Length &&
                float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
                float.IsFinite(parsed))
                requested = parsed;
        }
        SetHour(requested);
    }

    public void SetHour(float hour)
    {
        if (!float.IsFinite(hour))
            return;
        currentHour = Mathf.Repeat(hour, 24f);
        if (initialized)
            ApplyTime(true);
    }

    private void Update()
    {
        if (!initialized)
            return;
        if (!paused)
            currentHour = Mathf.Repeat(currentHour + Time.unscaledDeltaTime * timeScale * 24f /
                (60f * dayLengthMinutes), 24f);
        if (currentHour != appliedHour || milkyWay != appliedMilkyWay)
            ApplyTime(false);
    }

    private Vector3 SolarDirection(float hour)
    {
        float phi = latitude * Mathf.Deg2Rad;
        float arc = (sunsetHour - sunriseHour) * Mathf.PI / 24f;
        float declination = Mathf.Atan(-Mathf.Cos(arc) / Mathf.Tan(phi));
        float hourAngle = (hour - (sunriseHour + sunsetHour) * .5f) * Mathf.PI / 12f;
        float sinPhi = Mathf.Sin(phi), cosPhi = Mathf.Cos(phi);
        float sinDecl = Mathf.Sin(declination), cosDecl = Mathf.Cos(declination);
        float cosHour = Mathf.Cos(hourAngle);
        var eastUpNorth = new Vector3(
            -cosDecl * Mathf.Sin(hourAngle),
            sinPhi * sinDecl + cosPhi * cosDecl * cosHour,
            cosPhi * sinDecl - sinPhi * cosDecl * cosHour);
        return Quaternion.AngleAxis(northAzimuth, Vector3.up) * eastUpNorth;
    }

    private void ApplyTime(bool immediate)
    {
        Vector3 direction = SolarDirection(currentHour);
        SunElevation = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
        float daylight = Rise(-10f, 15f, SunElevation);
        float surfaceSun = Rise(0f, 5f, SunElevation);
        float highSun = Rise(0f, 30f, SunElevation);
        NightBlend = 1f - Rise(-9f, 3f, SunElevation);
        StarVisibility = 1f - Rise(-15f, -4f, SunElevation);

        sun.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
        bool morning = currentHour < (sunriseHour + sunsetHour) * .5f;
        sun.colorTemperature = Mathf.Lerp(morning ? 3550f : 3150f, 5700f, highSun);
        // Keep a small atmospheric input through civil twilight with the direct-light dimmer at zero.
        float solarLux = Mathf.Lerp(18500f, 66000f, highSun);
        sun.intensity = solarLux * Rise(-9f, 1f, SunElevation);
        sunData.lightDimmer = surfaceSun;
        sunData.volumetricDimmer = .92f * Rise(-8f, 4f, SunElevation);
        sun.shadowStrength = surfaceSun;
        var shadowMode = SunElevation > 0f ? LightShadows.Soft : LightShadows.None;
        if (sun.shadows != shadowMode)
            sun.shadows = shadowMode;

        var moonDirection = Quaternion.AngleAxis(28f, Vector3.up) * -direction;
        moon.transform.rotation = Quaternion.LookRotation(-moonDirection, Vector3.up);
        moon.intensity = moonLux * NightBlend * Rise(0f, 10f, -SunElevation);
        moon.enabled = moon.intensity > .001f;
        for (int index = 0; index < fillLux.Length; index++)
            if (daylightFill[index])
                daylightFill[index].intensity = fillLux[index] * (Rise(-5f, 17f, SunElevation) * .82f +
                    .006f * nightAmbient);

        exposure.fixedExposure.value = Mathf.Lerp(nightExposure, dayEv + .48f * highSun, daylight);
        ambient.indirectDiffuseLightingMultiplier.value = baseIndirect *
            Mathf.Lerp(.00045f * nightAmbient, 1f, Rise(-6f, 16f, SunElevation));
        ambient.reflectionLightingMultiplier.value = Mathf.Lerp(.78f, 1f, daylight);
        ambient.reflectionProbeIntensityMultiplier.value = Mathf.Lerp(.008f, 1f, daylight);
        whiteBalance.temperature.value = Mathf.Lerp(-22f, baseWhiteBalance, daylight);
        fog.albedo.value = Color.Lerp(new Color(.48f, .62f, .86f), baseFogColor, daylight);
        fog.meanFreePath.value = Mathf.Lerp(baseFogDistance * 1.45f, baseFogDistance, daylight);
        clouds.densityMultiplier.value = Mathf.Lerp(baseCloud * .27f, baseCloud, Rise(-11f, 4f, SunElevation));
        // The camera sky updates continuously. Refresh the convolved sky and its ambient probe less often.
        sky.spaceEmissionMultiplier.value = starRadiance * StarVisibility;
        if (milkyWay != appliedMilkyWay)
            sky.spaceEmissionTexture.value = milkyWay ? milkyWayCube : starCube;
        if ((immediate || Time.unscaledTime >= nextEnvironmentUpdate) &&
            RenderPipelineManager.currentPipeline is HDRenderPipeline hdrp)
        {
            hdrp.RequestSkyEnvironmentUpdate();
            nextEnvironmentUpdate = Time.unscaledTime + 1.25f;
        }
        appliedHour = currentHour;
        appliedMilkyWay = milkyWay;
    }

    private static float Rise(float from, float to, float value) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

    private void OnDestroy()
    {
        if (!runtimeProfile)
            return;
        foreach (var component in runtimeProfile.components)
            Destroy(component);
        Destroy(runtimeProfile);
    }
}
