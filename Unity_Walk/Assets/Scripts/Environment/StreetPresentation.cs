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
    private Material material;
    private Material occlusion;
    private Material sunlight;
    private Camera view;
    private StreetRayContact rayContact;
    private bool rayContactAttempted;
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
                int contactDownsample = paintedDistrictGrade ? 3 : 2;
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
                    sunMask = Temporary(source, 4); sunBlur = Temporary(source, 4);
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
