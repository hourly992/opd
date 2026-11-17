using UnityEngine;
using UnityEngine.Rendering;

// A small camera-local dust batch. Existing DepthNormals supplies soft edges;
// only two amortised sun-occlusion raycasts are allowed per rendered frame.
[DisallowMultipleComponent, RequireComponent(typeof(Camera))]
public sealed class StreetAtmosphereParticles : MonoBehaviour
{
    public Shader dustShader;
    [Range(0, 32)] public int maximumDust = 24;
    [Range(.2f, 2)] public float birthsPerSecond = .85f;
    [Range(.01f, .12f)] public float opacity = .055f;

    private const int Capacity = 32;
    private struct Mote
    {
        public Vector3 position;
        public float age, lifetime, size, phase, sunlight;
        public bool active;
    }

    private readonly Mote[] motes = new Mote[Capacity];
    private readonly Vector3[] vertices = new Vector3[Capacity * 4];
    private readonly Color32[] colors = new Color32[Capacity * 4];
    private Camera view;
    private Mesh mesh;
    private Material material;
    private float birthClock;
    private int nextSlot, nextOcclusion;
    private uint randomState = 160511;

    private void OnEnable()
    {
        if (!Application.isPlaying || !dustShader || !dustShader.isSupported) return;
        view = GetComponent<Camera>();
        // StreetPresentation already requests this texture; never request an
        // extra Depth pass or a new full-screen render target for the motes.
        view.depthTextureMode |= DepthTextureMode.DepthNormals;
        mesh = new Mesh { name = "Nearby sunlit dust (32 quad ceiling)" };
        mesh.MarkDynamic();
        material = new Material(dustShader) { hideFlags = HideFlags.HideAndDontSave };
        var uv = new Vector2[Capacity * 4];
        var indices = new int[Capacity * 6];
        for (int i = 0; i < Capacity; i++)
        {
            int v = i * 4, t = i * 6;
            uv[v] = new Vector2(-1, -1); uv[v + 1] = new Vector2(1, -1);
            uv[v + 2] = new Vector2(1, 1); uv[v + 3] = new Vector2(-1, 1);
            indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
            indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
        }
        mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = indices;
        mesh.colors32 = colors;
    }

    private float Random01()
    {
        unchecked { randomState = randomState * 1664525u + 1013904223u; }
        return (randomState & 0x00ffffffu) / 16777216f;
    }

    private static float Smooth(float from, float to, float value) =>
        Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, value));

    private void LateUpdate()
    {
        if (!mesh || !material || !view) return;
        int limit = Mathf.Clamp(maximumDust, 0, Capacity);
        Light sun = RenderSettings.sun;
        Vector3 sunDirection = sun ? -sun.transform.forward : Vector3.up;
        float sunPower = sun && sun.enabled ? Mathf.Clamp01(sun.intensity * .7f) *
            Smooth(.06f, .22f, sunDirection.y) : 0;
        float scattering = Mathf.Lerp(.08f, 1f,
            Smooth(-.15f, .72f, Vector3.Dot(view.transform.forward, sunDirection)));
        float dt = Mathf.Min(Time.deltaTime, .1f);
        Vector3 cameraPosition = view.transform.position;
        Vector3 right = view.transform.right, up = view.transform.up;
        birthClock += dt * birthsPerSecond;
        if (limit > 0 && birthClock >= 1 && sunPower > .05f)
        {
            birthClock = 0;
            int index = nextSlot++ % limit;
            motes[index] = new Mote
            {
                active = true, age = 0, lifetime = Mathf.Lerp(13, 20, Random01()),
                size = Mathf.Lerp(.004f, .009f, Random01()), phase = Random01() * 6.283185f,
                position = cameraPosition + view.transform.forward * Mathf.Lerp(1.5f, 7, Random01()) +
                    right * Mathf.Lerp(-3, 3, Random01()) + up * Mathf.Lerp(-.9f, 1.1f, Random01())
            };
        }
        // Rotation through the fixed pool bounds physics work, even at a low
        // frame rate. A newly born mote remains transparent until tested.
        for (int r = 0; r < 2 && limit > 0; r++)
        {
            int i = nextOcclusion++ % limit;
            Mote mote = motes[i];
            if (!mote.active) continue;
            mote.sunlight = sunPower > .05f && !Physics.Raycast(mote.position + sunDirection * .04f,
                sunDirection, 65, ~0, QueryTriggerInteraction.Ignore)
                ? StreetAutumnWeather.SampleCloudTransmittance(mote.position) : 0;
            motes[i] = mote;
        }
        int visible = 0;
        for (int i = 0; i < Capacity; i++)
        {
            Mote mote = motes[i];
            int v = i * 4;
            float alpha = 0;
            if (i < limit && mote.active)
            {
                mote.age += dt;
                mote.position += new Vector3(.050f, -.007f + Mathf.Sin(Time.time * .47f + mote.phase) * .011f, .035f) * dt;
                float distance = (mote.position - cameraPosition).magnitude;
                if (mote.age >= mote.lifetime || distance > 10) mote.active = false;
                float lifetimeFade = Smooth(0, 2, mote.age) *
                    (1 - Smooth(mote.lifetime - 3, mote.lifetime, mote.age));
                alpha = mote.active ? opacity * lifetimeFade * sunPower * scattering * mote.sunlight *
                    (1 - Smooth(6, 9, distance)) : 0;
            }
            motes[i] = mote;
            Vector3 x = right * mote.size, y = up * mote.size;
            vertices[v] = mote.position - x - y; vertices[v + 1] = mote.position + x - y;
            vertices[v + 2] = mote.position + x + y; vertices[v + 3] = mote.position - x + y;
            Color32 color = new Color32(255, 245, 220, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255));
            colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = color;
            if (color.a > 0) visible++;
        }
        if (visible == 0) return;
        mesh.SetVertices(vertices); mesh.SetColors(colors);
        mesh.bounds = new Bounds(cameraPosition, Vector3.one * 24);
        Graphics.DrawMesh(mesh, Matrix4x4.identity, material, gameObject.layer, view, 0, null,
            ShadowCastingMode.Off, false, null, LightProbeUsage.Off, null);
    }

    private void OnDisable()
    {
        if (mesh) Destroy(mesh);
        if (material) Destroy(material);
        mesh = null; material = null;
        for (int i = 0; i < Capacity; i++) motes[i].active = false;
        birthClock = 0; nextSlot = nextOcclusion = 0;
    }
}
