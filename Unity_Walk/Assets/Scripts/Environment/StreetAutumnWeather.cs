using UnityEngine;
using UnityEngine.Rendering;

// One small cloud field shared by sky/reflections and existing surface lighting.
// The shadow tile is a global texture, never Light.cookie: the latter can cause
// an extra ForwardAdd geometry pass in the built-in forward renderer.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class StreetAutumnWeather : MonoBehaviour
{
    public Light sun;
    public Material sky;
    [Min(100)] public float cloudHeight = 650;
    [Min(200)] public float cloudWorldSize = 1200;
    public Vector2 cloudVelocity = new Vector2(.98f, .684f);
    [Range(.1f,.8f)] public float cloudCoverage = .48f;
    [Range(.05f,.3f)] public float cloudSoftness = .18f;
    [Range(0,.4f)] public float cloudShadowStrength = .22f;
    [Min(1000)] public float cookieWorldSize = 2048;

    const int TileSize = 128, CookieRowsPerFrame = 8, ReflectionSize = 16;
    static StreetAutumnWeather active;
    Texture2D noise, cookie;
    Cubemap reflection;
    readonly float[] density = new float[TileSize * TileSize];
    readonly Color32[] cookiePixels = new Color32[TileSize * TileSize];
    readonly Color[] facePixels = new Color[ReflectionSize * ReflectionSize];
    readonly Vector3[] ambientDirections = { Vector3.up, Vector3.right, Vector3.left,
        Vector3.forward, Vector3.back, Vector3.down };
    readonly Color[] ambientSamples = new Color[6];
    Color zenith, horizon, cloudLit, cloudShade;
    Texture oldReflection;
    Material oldSky;
    AmbientMode oldAmbientMode;
    DefaultReflectionMode oldReflectionMode;
    SphericalHarmonicsL2 oldAmbient;
    Vector3 cookieBasePosition;
    int cookieRow, reflectionFace;
    float cookieTime, cookieAnchorTime, nextCookie, nextAmbient, nextReflection;
    bool ready;

    public static float SampleCloudTransmittance(Vector3 worldPosition)
    {
        return active && active.ready ? active.Transmittance(worldPosition,active.Clock()) : 1;
    }

    float Clock() => (float)Time.realtimeSinceStartupAsDouble;

    void OnEnable()
    {
        if (!sun) sun = RenderSettings.sun;
        if (!sky) sky = RenderSettings.skybox;
        if (!sun || !sky) return;
        cookieBasePosition = sun.transform.position;
        oldSky = RenderSettings.skybox;
        oldAmbientMode = RenderSettings.ambientMode; oldAmbient = RenderSettings.ambientProbe;
        oldReflection = RenderSettings.customReflectionTexture;
        oldReflectionMode = RenderSettings.defaultReflectionMode;
        zenith = WorkingColor(new Color(.38f,.57f,.77f));
        horizon = WorkingColor(new Color(.87f,.79f,.63f));
        cloudLit = WorkingColor(new Color(.96f,.925f,.84f));
        cloudShade = WorkingColor(new Color(.62f,.665f,.70f));
        MakeNoise();
        cookie = new Texture2D(TileSize,TileSize,TextureFormat.RGBA32,false,true)
            { name = "Street cloud cookie (128²)", wrapMode = TextureWrapMode.Repeat,
              filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        reflection = new Cubemap(ReflectionSize,TextureFormat.RGBAHalf,true)
            { name = "Street autumn sky reflection (16²)", hideFlags = HideFlags.HideAndDontSave };
        active = this; ready = true;
        Shader.SetGlobalTexture("_StreetWeatherCloudNoise",noise);
        SetLinear("_StreetWeatherZenith",zenith); SetLinear("_StreetWeatherHorizon",horizon);
        SetLinear("_StreetWeatherCloudLit",cloudLit); SetLinear("_StreetWeatherCloudShade",cloudShade);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Custom;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = reflection;
        float time = Clock();
        cookieAnchorTime = time;
        SetGlobals(time);
        // Startup initialization is complete before the first rendered frame.
        FillCookie(0,TileSize,time); UploadCookie();
        Shader.SetGlobalTexture("_StreetWeatherCloudCookie",cookie);
        for (int face=0; face<6; face++) FillReflection(face,time);
        reflection.Apply(true,false);
        UpdateAmbient(time);
        nextCookie = time+60; nextAmbient = time+2; nextReflection = time+.5f;
    }

    void Update()
    {
        if (!ready || !sun) return;
        float time = Clock();
        // Move only the global projection. The real sun/scene transform stays
        // untouched, including in ExecuteAlways editor previews.
        SetGlobals(time);
        // Re-centre the projection once a minute, at most 1024 cookie pixels per
        // frame. This avoids repeating the cookie border inside the play area.
        if (cookieRow > 0 || time >= nextCookie)
        {
            if (cookieRow == 0) cookieTime = time;
            FillCookie(cookieRow,CookieRowsPerFrame,cookieTime);
            cookieRow += CookieRowsPerFrame;
            if (cookieRow == TileSize)
            {
                UploadCookie(); cookieAnchorTime=cookieTime;
                SetProjection(time);
                cookieRow = 0; nextCookie = time+60;
            }
        }
        if (time >= nextAmbient) { UpdateAmbient(time); nextAmbient=time+2; }
        if (time >= nextReflection)
        {
            FillReflection(reflectionFace,time);
            reflection.Apply(true,false);
            reflectionFace=(reflectionFace+1)%6; nextReflection=time+.5f;
        }
    }

    void SetGlobals(float time)
    {
        Vector3 toSun = -sun.transform.forward;
        Shader.SetGlobalFloat("_StreetWeatherEnabled",1);
        Shader.SetGlobalVector("_StreetWeatherCloudParameters",new Vector4(cloudHeight,
            Mathf.Max(200,cloudWorldSize),1-cloudCoverage,Mathf.Max(.05f,cloudSoftness)));
        Shader.SetGlobalVector("_StreetWeatherCloudOffset",new Vector4(cloudVelocity.x*time,cloudVelocity.y*time,0,0));
        Shader.SetGlobalVector("_StreetWeatherSunDirection",new Vector4(toSun.x,toSun.y,toSun.z,0));
        Color light = WorkingColor(sun.color)*sun.intensity;
        Shader.SetGlobalVector("_StreetWeatherSunColor",new Vector4(light.r,light.g,light.b,0));
        SetProjection(time);
    }

    void SetProjection(float time)
    {
        Vector2 travel=cloudVelocity*(time-cookieAnchorTime);
        Vector3 origin=cookieBasePosition+new Vector3(travel.x,0,travel.y);
        Vector3 right=sun.transform.right/Mathf.Max(1000,cookieWorldSize);
        Vector3 up=sun.transform.up/Mathf.Max(1000,cookieWorldSize);
        Shader.SetGlobalVector("_StreetWeatherCookieOrigin",new Vector4(origin.x,origin.y,origin.z,0));
        Shader.SetGlobalVector("_StreetWeatherCookieRight",new Vector4(right.x,right.y,right.z,0));
        Shader.SetGlobalVector("_StreetWeatherCookieUp",new Vector4(up.x,up.y,up.z,0));
    }

    static void SetLinear(string name, Color value)
    {
        Shader.SetGlobalVector(name,new Vector4(value.r,value.g,value.b,0));
    }

    static Color WorkingColor(Color value)
    {
        return QualitySettings.activeColorSpace==ColorSpace.Linear ? value.linear : value;
    }

    void MakeNoise()
    {
        noise = new Texture2D(TileSize,TileSize,TextureFormat.RGBA32,false,true)
            { name="Street cloud density (128²)", wrapMode=TextureWrapMode.Repeat,
              filterMode=FilterMode.Bilinear, hideFlags=HideFlags.HideAndDontSave };
        var pixels = new Color32[density.Length];
        for (int y=0; y<TileSize; y++) for (int x=0; x<TileSize; x++)
        {
            float value=0, weight=.57f, sum=0;
            for (int octave=0; octave<4; octave++)
            {
                value+=ValueNoise((float)x/TileSize,(float)y/TileSize,4<<octave)*weight;
                sum+=weight; weight*=.49f;
            }
            byte sample=(byte)Mathf.RoundToInt(Mathf.Clamp01(value/sum)*255);
            density[y*TileSize+x]=sample/255f; pixels[y*TileSize+x]=new Color32(sample,sample,sample,255);
        }
        noise.SetPixels32(pixels); noise.Apply(false,true);
    }

    static float Hash(int x,int y)
    {
        unchecked
        {
            uint h=(uint)(x*374761393+y*668265263+1337);
            h=(h^(h>>13))*1274126177u; h^=h>>16;
            return (h&0x00ffffff)/16777215f;
        }
    }

    static float ValueNoise(float u,float v,int cells)
    {
        float x=u*cells,y=v*cells; int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);
        float fx=x-ix,fy=y-iy; fx=fx*fx*(3-2*fx); fy=fy*fy*(3-2*fy);
        return Mathf.Lerp(Mathf.Lerp(Hash(ix%cells,iy%cells),Hash((ix+1)%cells,iy%cells),fx),
            Mathf.Lerp(Hash(ix%cells,(iy+1)%cells),Hash((ix+1)%cells,(iy+1)%cells),fx),fy);
    }

    float Noise(Vector2 uv)
    {
        float x=Mathf.Repeat(uv.x,1)*TileSize-.5f,y=Mathf.Repeat(uv.y,1)*TileSize-.5f;
        int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y); float fx=x-ix,fy=y-iy;
        int x0=(ix+TileSize)%TileSize,x1=(x0+1)%TileSize;
        int y0=(iy+TileSize)%TileSize,y1=(y0+1)%TileSize;
        return Mathf.Lerp(Mathf.Lerp(density[y0*TileSize+x0],density[y0*TileSize+x1],fx),
            Mathf.Lerp(density[y1*TileSize+x0],density[y1*TileSize+x1],fx),fy);
    }

    Vector2 CloudUV(Vector3 origin,Vector3 direction,float layer,float time)
    {
        float distance=(cloudHeight*layer-origin.y)/Mathf.Max(.055f,direction.y);
        Vector3 point=origin+direction*distance;
        return (new Vector2(point.x,point.z)-cloudVelocity*time)/(Mathf.Max(200,cloudWorldSize)*layer)
            +(layer-1)*new Vector2(.37f,.19f);
    }

    float Shape(float value)
    {
        float a=1-cloudCoverage-Mathf.Max(.05f,cloudSoftness), b=1-cloudCoverage+Mathf.Max(.05f,cloudSoftness);
        float t=Mathf.Clamp01((value-a)/(b-a)); return t*t*(3-2*t);
    }

    float Transmittance(Vector3 position,float time)
    {
        Vector3 direction=-sun.transform.forward;
        if (direction.y <= .055f) return 1;
        float lower=Shape(Noise(CloudUV(position,direction,1,time)));
        float upper=Shape(Noise(CloudUV(position,direction,1.8f,time)));
        return 1-cloudShadowStrength*Mathf.Clamp01(lower*.82f+upper*.20f);
    }

    void FillCookie(int firstRow,int rows,float time)
    {
        var light=sun.transform; Vector3 origin=cookieBasePosition;
        float size=Mathf.Max(1000,cookieWorldSize);
        Vector3 right=light.right*size,up=light.up*size;
        for (int y=firstRow; y<firstRow+rows; y++) for (int x=0; x<TileSize; x++)
        {
            Vector3 position=origin+right*((x+.5f)/TileSize-.5f)+up*((y+.5f)/TileSize-.5f);
            byte alpha=(byte)Mathf.RoundToInt(Transmittance(position,time)*255);
            cookiePixels[y*TileSize+x]=new Color32(255,255,255,alpha);
        }
    }

    void UploadCookie() { cookie.SetPixels32(cookiePixels); cookie.Apply(false,false); }

    Color SkyColor(Vector3 origin,Vector3 direction,float time)
    {
        direction.Normalize(); Vector3 toSun=-sun.transform.forward;
        float facing=Mathf.Pow(Mathf.Max(0,Vector3.Dot(direction,toSun)),6);
        Color edge=Color.Lerp(horizon,new Color(horizon.r*1.10f,horizon.g*1.025f,horizon.b*.88f),facing*.30f);
        Color color=Color.Lerp(edge,zenith,Mathf.SmoothStep(0,1,Mathf.Clamp01(direction.y/.78f)));
        color=Color.Lerp(edge*.27f,color,Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.18f,.015f,direction.y)));
        if (direction.y <= .015f) return color;
        Vector2 uv=CloudUV(origin,direction,1,time);
        float d=Noise(uv),shape=Shape(d);
        Vector2 sunXZ=new Vector2(toSun.x+.0001f,toSun.z+.0001f).normalized;
        float dimensional=Mathf.Clamp01(.57f+(d-Noise(uv+sunXZ*.009f))*3.4f);
        float silver=facing*(1-shape)*.15f;
        Color cloud=Color.Lerp(cloudShade,cloudLit,dimensional)+new Color(silver,silver,silver,0);
        float upper=Shape(Noise(CloudUV(origin,direction,1.8f,time)));
        float fade=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.015f,.16f,direction.y));
        color=Color.Lerp(color,cloudLit*.83f,upper*.20f*fade);
        return Color.Lerp(color,cloud,shape*.82f*fade);
    }

    void UpdateAmbient(float time)
    {
        var probe=new SphericalHarmonicsL2(); Color mean=Color.black;
        for (int i=0;i<6;i++) { ambientSamples[i]=SkyColor(Vector3.zero,ambientDirections[i],time); mean+=ambientSamples[i]/6; }
        probe.AddAmbientLight(mean*.55f);
        for (int i=0;i<6;i++) probe.AddDirectionalLight(ambientDirections[i],ambientSamples[i],.22f);
        RenderSettings.ambientProbe=probe;
        // Representative linear fill for material white-balance compensation.
        Color fill=(ambientSamples[0]*.4f+(ambientSamples[1]+ambientSamples[2]+ambientSamples[3]+ambientSamples[4])*.15f)*.75f;
        SetLinear("_StreetWeatherAmbientColor",fill);
    }

    void FillReflection(int face,float time)
    {
        for (int y=0;y<ReflectionSize;y++) for (int x=0;x<ReflectionSize;x++)
        {
            float u=2*(x+.5f)/ReflectionSize-1,v=2*(y+.5f)/ReflectionSize-1;
            Vector3 direction;
            switch (face)
            {
                case 0: direction=new Vector3(1,-v,-u);break;
                case 1: direction=new Vector3(-1,-v,u);break;
                case 2: direction=new Vector3(u,1,v);break;
                case 3: direction=new Vector3(u,-1,-v);break;
                case 4: direction=new Vector3(u,-v,1);break;
                default: direction=new Vector3(-u,-v,-1);break;
            }
            facePixels[y*ReflectionSize+x]=SkyColor(Vector3.zero,direction,time);
        }
        reflection.SetPixels(facePixels,(CubemapFace)face);
    }

    void OnDisable()
    {
        if (!ready) return;
        if (active==this)
        {
            active=null; Shader.SetGlobalFloat("_StreetWeatherEnabled",0);
            RenderSettings.skybox=oldSky; RenderSettings.ambientMode=oldAmbientMode; RenderSettings.ambientProbe=oldAmbient;
            RenderSettings.defaultReflectionMode=oldReflectionMode; RenderSettings.customReflectionTexture=oldReflection;
        }
        Release(noise);Release(cookie);Release(reflection);ready=false;
    }

    static void Release(Object resource)
    {
        if (!resource) return;
        if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
    }
}
