using System;
using UnityEngine;

// Built-in renderer: bilateral contact AO, restrained HDR bloom, display transform and edge antialiasing.
[ExecuteAlways, RequireComponent(typeof(Camera))]
public sealed class StreetPresentation : MonoBehaviour
{
    public Shader shader;
    public Shader occlusionShader;
    public Shader sunShaftShader;
    public ComputeShader rayContactShader;
    [Range(0, 1)] public float sunShafts = .38f;
    [Range(0, 1)] public float contactShadows = .55f;
    [Range(.1f, 2)] public float contactRadius = .7f;
    [Range(.2f, 2)] public float exposure = 1.1f;
    [Range(0, 1.2f)] public float saturation = 1;
    [Range(0, .5f)] public float bloom = .12f;
    [Range(0, .4f)] public float sharpness = .15f;
    [Range(0, 1)] public float autumnGrade;
    [Range(0, 1)] public float leafGrade;
    public bool paintedDistrictGrade;
    [Range(0, 1)] public float warmAutumnGrade;
    [Range(0, .2f)] public float vignette = .09f;
    public bool neutralAsphaltOutput = true;
    private Material material;
    private Material occlusion;
    private Material sunlight;
    private Camera view;
    private StreetRayContact rayContact;
    private bool rayContactAttempted;
    private const int AsphaltToneSamples = 32;
    private static readonly Vector3 Luma = new Vector3(.2126f,.7152f,.0722f);
    private readonly Vector4[] asphaltNeutralTone = new Vector4[AsphaltToneSamples];
    private Vector3 asphaltToneState = new Vector3(float.NaN,float.NaN,float.NaN);
    private static StreetPresentation asphaltToneOwner;
    private static readonly int AsphaltNeutralOutput = Shader.PropertyToID("_AsphaltNeutralOutput");
    private static readonly int AsphaltNeutralTone = Shader.PropertyToID("_AsphaltNeutralTone");
    internal bool RayTracingEnabled { get; set; } = true;
    internal bool RayTracingActive { get; private set; }
    internal int RayTracingInstances => rayContact?.InstanceCount ?? 0;
    internal int CountRayTracedContacts() => RayTracingActive ? rayContact.CountOccludedPixels() : 0;

    private void OnEnable()
    {
        view = GetComponent<Camera>();
        view.allowHDR = true;
        view.depthTextureMode |= DepthTextureMode.DepthNormals;
        if (Application.isPlaying && Array.IndexOf(Environment.GetCommandLineArgs(), "-noRayTracing") >= 0)
            RayTracingEnabled = false;
    }

    private void OnPreCull()
    {
        PublishAsphaltNeutrality();
    }

    private void Update()
    {
        if (Application.isPlaying && StreetRayContact.IsSupported(rayContactShader) && Input.GetKeyDown(KeyCode.F6))
            RayTracingEnabled = !RayTracingEnabled;
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        RayTracingActive = false;
        if (!shader || !shader.isSupported) { Graphics.Blit(source, destination); return; }
        if (!material) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetFloat("_Exposure", exposure);
        material.SetFloat("_Saturation", saturation);
        material.SetFloat("_Sharpness", sharpness);
        material.SetFloat("_BloomStrength", bloom);
        material.SetFloat("_AutumnGrade", autumnGrade);
        material.SetFloat("_LeafGrade", leafGrade);
        material.SetFloat("_PaintedDistrictGrade", paintedDistrictGrade ? 1 : 0);
        material.SetFloat("_WarmAutumnGrade", warmAutumnGrade);
        material.SetFloat("_Vignette", vignette);
        bool previous = GL.sRGBWrite;
        RenderTexture raw = null, blurred = null, shaded = null;
        RenderTexture highlights = null, bloomSmall = null, bloomWide = null;
        RenderTexture sunMask = null, sunBlur = null, sunComposite = null;
        try
        {
            GL.sRGBWrite = false;
            if (occlusionShader && occlusionShader.isSupported && contactShadows > 0)
            {
                if (!occlusion) occlusion = new Material(occlusionShader) { hideFlags = HideFlags.HideAndDontSave };
                int contactDownsample = paintedDistrictGrade ? 4 : 2;
                raw = RenderTexture.GetTemporary(Mathf.Max(1, source.width / contactDownsample), Mathf.Max(1, source.height / contactDownsample), 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
                blurred = RenderTexture.GetTemporary(raw.width, raw.height, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
                raw.filterMode = blurred.filterMode = FilterMode.Bilinear;
                shaded = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear);
                occlusion.SetVector("_ProjectionScale", new Vector4(1 / view.projectionMatrix.m00, 1 / view.projectionMatrix.m11, 0, 0));
                var rayMask = RenderRayContacts(source, raw.width, raw.height);
                RayTracingActive = rayMask != null;
                occlusion.SetVector("_AOSettings", new Vector4(contactRadius, 1, contactShadows, paintedDistrictGrade ? 1 : 0));
                if (!RayTracingActive)
                    Graphics.Blit(source, raw, occlusion, 0);
                occlusion.SetVector("_BlurDirection", new Vector4(1, 0, 0, 0));
                Graphics.Blit(rayMask ? rayMask : raw, blurred, occlusion, 1);
                occlusion.SetVector("_BlurDirection", new Vector4(0, 1, 0, 0));
                Graphics.Blit(blurred, raw, occlusion, 1);
                occlusion.SetTexture("_AOTex", raw);
                Graphics.Blit(source, shaded, occlusion, 2);
            }
            // Depth-masked shafts: opaque geometry interrupts the sky mask before radial integration.
            // The effect fades when the sun leaves the view; no permanent cones placed in the scene.
            var sun = RenderSettings.sun;
            if (sunShaftShader && sunShaftShader.isSupported && sun && sunShafts > 0)
            {
                Vector3 screenSun = view.WorldToViewportPoint(view.transform.position - sun.transform.forward * 1000);
                float edge = Mathf.Max(Mathf.Abs(screenSun.x - .5f), Mathf.Abs(screenSun.y - .5f));
                float visibility = screenSun.z > 0 ? 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, .9f, edge)) : 0;
                if (visibility > 0)
                {
                    if (!sunlight) sunlight = new Material(sunShaftShader) { hideFlags = HideFlags.HideAndDontSave };
                    sunlight.SetVector("_SunPosition", new Vector4(screenSun.x, screenSun.y, (float)source.width / source.height, visibility));
                    sunlight.SetColor("_SunColor", sun.color.linear * Mathf.Min(2, sun.intensity));
                    sunlight.SetFloat("_Strength", sunShafts);
                    sunMask = SunMask(source); sunBlur = SunMask(source);
                    Graphics.Blit(source, sunMask, sunlight, 0);
                    sunlight.SetFloat("_RadialScale", .7f);
                    Graphics.Blit(sunMask, sunBlur, sunlight, 1);
                    sunlight.SetFloat("_RadialScale", .35f);
                    Graphics.Blit(sunBlur, sunMask, sunlight, 1);
                    sunlight.SetTexture("_ShaftTex", sunMask);
                    sunComposite = Temporary(source, 1);
                    Graphics.Blit(shaded ? shaded : source, sunComposite, sunlight, 2);
                }
            }
            var scene = sunComposite ? sunComposite : shaded ? shaded : source;
            if (bloom > 0)
            {
                highlights = Temporary(source, 2);
                bloomSmall = Temporary(source, 4);
                bloomWide = Temporary(source, 8);
                Graphics.Blit(scene, highlights, material, 1);
                Graphics.Blit(highlights, bloomSmall, material, 2);
                Graphics.Blit(bloomSmall, bloomWide, material, 2);
            }
            material.SetTexture("_BloomTex", bloomSmall ? (Texture)bloomSmall : Texture2D.blackTexture);
            material.SetTexture("_BloomWideTex", bloomWide ? (Texture)bloomWide : Texture2D.blackTexture);
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(scene, destination, material, 0);
        }
        finally
        {
            GL.sRGBWrite = previous;
            if (raw) RenderTexture.ReleaseTemporary(raw);
            if (blurred) RenderTexture.ReleaseTemporary(blurred);
            if (shaded) RenderTexture.ReleaseTemporary(shaded);
            if (highlights) RenderTexture.ReleaseTemporary(highlights);
            if (bloomSmall) RenderTexture.ReleaseTemporary(bloomSmall);
            if (bloomWide) RenderTexture.ReleaseTemporary(bloomWide);
            if (sunMask) RenderTexture.ReleaseTemporary(sunMask);
            if (sunBlur) RenderTexture.ReleaseTemporary(sunBlur);
            if (sunComposite) RenderTexture.ReleaseTemporary(sunComposite);
        }
    }

    // This small table inverts the actual Painted/Warm display equations, not
    // the source JPEG average. Updating a slider rebuilds it once; cloud shadow,
    // sun color and AO only change lookup luminance. No per-frame arrays/RTs.
    private void PublishAsphaltNeutrality()
    {
        if (!neutralAsphaltOutput || !paintedDistrictGrade || !shader || !shader.isSupported)
        {
            if (asphaltToneOwner == this)
            {
                Shader.SetGlobalVector(AsphaltNeutralOutput,Vector4.zero);
                asphaltToneOwner = null;
            }
            return;
        }
        var state = new Vector3(exposure,saturation,warmAutumnGrade);
        bool rebuild = state != asphaltToneState;
        if (rebuild)
        {
            for (int i=0;i<AsphaltToneSamples;i++)
            {
                float level = Mathf.Pow(2,-10f + i*12f/(AsphaltToneSamples-1));
                Vector3 guess = Vector3.one * level;
                float target = Mathf.Max(Vector3.Dot(DisplayReference(guess),Luma),
                    MaxChannel(DisplayReference(Vector3.zero))+.00001f);
                float h = Mathf.Max(.00001f,level*.002f);
                for (int step=0;step<14;step++)
                {
                    Vector3 current = DisplayReference(guess);
                    Vector3 error = current - Vector3.one*target;
                    if (MaxChannel(Abs(error)) < .0000001f) break;
                    Vector3 a = (DisplayReference(guess+Vector3.right*h)-current)/h;
                    Vector3 b = (DisplayReference(guess+Vector3.up*h)-current)/h;
                    Vector3 c = (DisplayReference(guess+Vector3.forward*h)-current)/h;
                    float determinant = Vector3.Dot(a,Vector3.Cross(b,c));
                    if (Mathf.Abs(determinant)<.00000000000001f) break;
                    Vector3 delta = new Vector3(Vector3.Dot(error,Vector3.Cross(b,c)),
                        Vector3.Dot(a,Vector3.Cross(error,c)),Vector3.Dot(a,Vector3.Cross(b,error))) / determinant;
                    guess -= delta*.85f;
                    guess = new Vector3(Mathf.Max(.000001f,guess.x),Mathf.Max(.000001f,guess.y),Mathf.Max(.000001f,guess.z));
                }
                Vector3 gain = guess/level;
                asphaltNeutralTone[i] = new Vector4(gain.x,gain.y,gain.z,0);
            }
            asphaltToneState = state;
        }
        if (rebuild || asphaltToneOwner != this)
        {
            Shader.SetGlobalVectorArray(AsphaltNeutralTone,asphaltNeutralTone);
            Shader.SetGlobalVector(AsphaltNeutralOutput,new Vector4(1,-10,1f/12,AsphaltToneSamples));
            asphaltToneOwner = this;
        }
    }

    // CPU mirror of PaintedGrade -> saturation -> WarmAutumnGrade. The numeric
    // validation checks both versions and the final sRGB neutral-channel error.
    private Vector3 DisplayReference(Vector3 value)
    {
        value *= Mathf.Max(.01f,exposure);
        float shadow = 1-SmoothRange(.08f,.68f,Vector3.Dot(value,Luma));
        Vector3 balance = Vector3.LerpUnclamped(new Vector3(1.028f,1.006f,.970f),new Vector3(1.060f,1.026f,.949f),shadow);
        Vector3 color = Vector3.Scale(value,balance);
        color = new Vector3(Filmic(color.x),Filmic(color.y),Filmic(color.z));
        float level = Vector3.Dot(color,Luma);
        color += new Vector3(1,.88f,.67f)*((1-SmoothRange(.025f,.49f,level))*.038f);
        color = Vector3.LerpUnclamped(color,Vector3.one*Vector3.Dot(color,Luma),.10f*SmoothRange(.78f,1,level));
        float grey = Vector3.Dot(color,Luma);
        float chroma = MaxChannel(color)-MinChannel(color);
        color = Vector3.one*grey+(color-Vector3.one*grey)*(saturation+(1-Mathf.Clamp01(chroma*3))*.04f);
        float peak = MaxChannel(color);
        chroma = peak-MinChannel(color);
        float amount = warmAutumnGrade*(1-SmoothRange(.52f,.82f,peak))*Mathf.Lerp(1,.075f,Mathf.Clamp01(chroma*4));
        Vector3 warmed = Vector3.Scale(color,new Vector3(1.12f,1.022f,.91f));
        warmed *= Vector3.Dot(color,Luma)/Mathf.Max(.00001f,Vector3.Dot(warmed,Luma));
        return Vector3.LerpUnclamped(color,warmed,amount);
    }

    private static float Filmic(float value) => Mathf.Clamp01(value*(2.51f*value+.03f)/(value*(2.43f*value+.59f)+.14f));
    private static float SmoothRange(float low,float high,float value)
    {
        float t = Mathf.Clamp01((value-low)/(high-low));
        return t*t*(3-2*t);
    }
    private static float MaxChannel(Vector3 c) => Mathf.Max(c.x,Mathf.Max(c.y,c.z));
    private static float MinChannel(Vector3 c) => Mathf.Min(c.x,Mathf.Min(c.y,c.z));
    private static Vector3 Abs(Vector3 c) => new Vector3(Mathf.Abs(c.x),Mathf.Abs(c.y),Mathf.Abs(c.z));

    private static RenderTexture SunMask(RenderTexture source)
    {
        var result = RenderTexture.GetTemporary(Mathf.Max(1,source.width/4),Mathf.Max(1,source.height/4),
            0,RenderTextureFormat.RHalf,RenderTextureReadWrite.Linear);
        result.filterMode = FilterMode.Bilinear;
        result.wrapMode = TextureWrapMode.Clamp;
        return result;
    }

    private RenderTexture RenderRayContacts(RenderTexture source, int width, int height)
    {
        if (!Application.isPlaying || !RayTracingEnabled || view.orthographic || !StreetRayContact.IsSupported(rayContactShader))
            return null;
        RenderTexture packed = null;
        try
        {
            if (!rayContactAttempted)
            {
                rayContactAttempted = true;
                rayContact = new StreetRayContact(rayContactShader, view, occlusion);
                Debug.Log($"STREET_DXR active instances={rayContact.InstanceCount} gpu={SystemInfo.graphicsDeviceName}");
            }
            if (rayContact == null)
                return null;
            packed = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            packed.filterMode = FilterMode.Point;
            Graphics.Blit(source, packed, occlusion, 3);
            return rayContact.Render(view, packed, Mathf.Clamp(contactRadius * 1.6f, .45f, 1.2f));
        }
        catch (Exception error)
        {
            Debug.LogWarning("STREET_DXR using screen-space contact shadows: " + error.Message);
            rayContact?.Dispose();
            rayContact = null;
            return null;
        }
        finally
        {
            if (packed) RenderTexture.ReleaseTemporary(packed);
        }
    }

    private static RenderTexture Temporary(RenderTexture source, int downsample)
    {
        var result = RenderTexture.GetTemporary(Mathf.Max(1, source.width / downsample),
            Mathf.Max(1, source.height / downsample), 0, source.format, RenderTextureReadWrite.Linear);
        result.filterMode = FilterMode.Bilinear;
        result.wrapMode = TextureWrapMode.Clamp;
        return result;
    }

    private void OnDisable()
    {
        if (asphaltToneOwner == this)
        {
            Shader.SetGlobalVector(AsphaltNeutralOutput,Vector4.zero);
            asphaltToneOwner = null;
        }
        rayContact?.Dispose();
        rayContact = null;
        rayContactAttempted = false;
        RayTracingActive = false;
        foreach (var effect in new[] { material, occlusion, sunlight })
        {
            if (!effect) continue;
            if (Application.isPlaying) Destroy(effect);
            else DestroyImmediate(effect);
        }
        material = null; occlusion = null; sunlight = null;
    }
}
