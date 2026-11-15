"""CPU reference for the lit-asphalt inverse camera tone, not captured frames.

The sweep includes varying illumination/AO/cookie strength, fixed source maps,
SM3.5 uniform-table interpolation and the current saved presentation parameters.
Requires numpy/Pillow/shapely from the StreetRepair environment.
"""
import argparse
import colorsys
import hashlib
import json
from pathlib import Path
import re
import subprocess

import numpy as np
from unity_yaml import ROOT, field

BASE = '0d73c23ee7bd71c144bf188d0f5b4cb91304c979'
REPO = ROOT.parent
W = np.array([.2126,.7152,.0722],dtype=np.float32)


def smooth(low, high, value):
    t = np.clip((value-low)/(high-low),0,1)
    return t*t*(3-2*t)


def display(value, exposure=1.035, saturation=1.075, warmth=.95):
    """StreetPresentation CPU DisplayReference and matching shader operations."""
    color = np.asarray(value,dtype=np.float32)*exposure
    shadow = 1-smooth(.08,.68,color@W)
    balance = np.array([1.028,1.006,.970],dtype=np.float32)*(1-shadow)+np.array([1.060,1.026,.949],dtype=np.float32)*shadow
    color *= balance
    color = np.clip(color*(2.51*color+.03)/(color*(2.43*color+.59)+.14),0,1)
    level = color@W
    color += np.array([1,.88,.67],dtype=np.float32)*((1-smooth(.025,.49,level))*.038)
    h = .1*smooth(.78,1,level)
    color = color*(1-h)+(color@W)*h
    grey = color@W
    chroma = color.max()-color.min()
    factor = saturation+(1-np.clip(chroma*3,0,1))*.04
    color = grey+(color-grey)*factor
    return warm_grade(color,warmth).astype(np.float32)


def warm_grade(value, amount):
    color = np.asarray(value,dtype=np.float32)
    peak = color.max()
    chroma = peak-color.min()
    factor = amount*(1-smooth(.52,.82,peak))*(1-.925*np.clip(chroma*4,0,1))
    warmed = color*np.array([1.12,1.022,.91],dtype=np.float32)
    warmed *= (color@W)/max(.00001,warmed@W)
    return color+(warmed-color)*factor


def srgb8(value):
    c = np.maximum(value,0)
    return np.where(c<=.0031308,c*12.92,1.055*c**(1/2.4)-.055)*255


def table(exposure, saturation, warmth):
    result = []
    for i in range(32):
        level = np.float32(2**(-10+i*12/31))
        guess = np.full(3,level,dtype=np.float32)
        target = max(display(guess,exposure,saturation,warmth)@W,
                     display(np.zeros(3),exposure,saturation,warmth).max()+.00001)
        h = max(.00001,level*.002)
        for step in range(14):
            current = display(guess,exposure,saturation,warmth)
            error = current-target
            if abs(error).max()<.0000001:
                break
            a,b,c = [(display(guess+np.eye(3,dtype=np.float32)[j]*h,exposure,saturation,warmth)-current)/h for j in range(3)]
            determinant = a@np.cross(b,c)
            if abs(determinant)<.00000000000001:
                break
            delta = np.array([error@np.cross(b,c),a@np.cross(error,c),a@np.cross(b,error)],dtype=np.float32)/determinant
            guess = np.maximum(.000001,guess-delta*.85).astype(np.float32)
        result.append(guess/level)
    return np.array(result,dtype=np.float32)


def compensate(luminance, lut):
    index = np.clip((np.log2(max(luminance,.0009765625))+10)/12,0,1)*31
    first = min(30,int(index))
    gain = lut[first]*(1-(index-first))+lut[first+1]*(index-first)
    return luminance*gain


def run():
    cs = (ROOT/'Assets/Scripts/Environment/StreetPresentation.cs').read_text()
    post = (ROOT/'Assets/Shaders/StreetPresentation.shader').read_text()
    asphalt = (ROOT/'Assets/Shaders/StreetAsphalt020S.shader').read_text()
    shafts = (ROOT/'Assets/Shaders/StreetSunShafts.shader').read_text()
    from install_asphalt020s import SHADER,verify
    assert asphalt == SHADER
    assert '#pragma target 3.5' in asphalt
    assert 'exclude_path:deferred' in asphalt and 'LightingStandard(s,viewDir,gi)' in asphalt
    assert 'LightingStandard_GI(s,data,gi)' in asphalt
    assert asphalt.count('tex2D(') == 3
    assert '#include "StreetAutumnWeather.cginc"' in asphalt
    assert '#if defined(UNITY_PASS_FORWARDBASE) && defined(DIRECTIONAL)' in asphalt
    assert 'gi.light.color *= StreetWeatherCloudShadow(data.worldPos);' in asphalt
    cloud=(ROOT/'Assets/Shaders/StreetAutumnWeather.cginc').read_text()
    shadow=re.search(r'float StreetWeatherCloudShadow\(.*?\n}',cloud,re.S)[0]
    assert shadow.count('tex2D(')==1 and '_StreetWeatherEnabled < .5' in shadow
    assert 'private const int AsphaltToneSamples = 32;' in cs and '_AsphaltNeutralTone[32]' in asphalt
    assert 'private void OnPreCull()' in cs and 'PublishAsphaltNeutrality();' in cs
    assert 'public bool neutralAsphaltOutput = true;' in cs
    assert 'Shader.SetGlobalVectorArray(AsphaltNeutralTone,asphaltNeutralTone)' in cs
    assert 'new Vector4(1,-10,1f/12,AsphaltToneSamples)' in cs
    # Pin all nonlinear coefficients mirrored by the CPU solve; a drift must
    # update both references rather than silently retain an obsolete inverse.
    for token in ['float3(1.028,1.006,.970)','float3(1.060,1.026,.949)',
                  'float3(1.12,1.022,.91)','lerp(1,.075,saturate(chroma * 4))',
                  'smoothstep(.52,.82,peak)','smoothstep(.025,.49,level)']:
        assert token in post, ('GPU grade coefficient drifted',token)
    for token in ['new Vector3(1.028f,1.006f,.970f)','new Vector3(1.060f,1.026f,.949f)',
                  'new Vector3(1.12f,1.022f,.91f)','Mathf.Lerp(1,.075f,Mathf.Clamp01(chroma*4))',
                  'SmoothRange(.52f,.82f,peak)','SmoothRange(.025f,.49f,level)',
                  'for (int step=0;step<14;step++)','guess -= delta*.85f;']:
        assert token in cs, ('CPU grade coefficient drifted',token)
    oldpost = subprocess.check_output(['git','show',BASE+':Unity_Walk/Assets/Shaders/StreetPresentation.shader'],cwd=REPO,text=True)
    assert post.count('tex2D(') == oldpost.count('tex2D('), 'Vignette added a texture read'
    assert len(re.findall(r'^        Pass\s*$',post,re.M)) == len(re.findall(r'^        Pass\s*$',oldpost,re.M))
    assert 'n < 12' in shafts and '/ 12)' in shafts and 'n < 24' not in shafts
    assert '(1-step(.5,_StreetWeatherEnabled))' in shafts
    assert 'RenderTextureFormat.RHalf' in cs and 'paintedDistrictGrade ? 4 : 2' in cs
    assert 'float knee=clamp(brightness-.72,0,.5);' in post
    assert 'float contribution=min(2.5,max(brightness-1.05,knee*knee/.5));' in post
    def bloom_source(level):
        knee=np.clip(level-.72,0,.5)
        return float(min(2.5,max(level-1.05,knee*knee/.5)))
    bloom_samples={str(v):bloom_source(v) for v in [.2,.4,.7,.8,1,1.5,8]}
    assert all(bloom_source(v)==0 for v in [.2,.4,.7])
    assert 0<bloom_source(.8)<.02 and bloom_source(8)==2.5
    source = ROOT/'Assets/ThirdParty/ambientCG/Asphalt020S'
    protected = []
    for path in sorted(source.iterdir()):
        if path.suffix.lower() in ('.jpg','.png') or path.name.endswith(('.jpg.meta','.png.meta')):
            old = subprocess.check_output(['git','show',BASE+':'+str(path.relative_to(REPO))],cwd=REPO)
            assert old == path.read_bytes(), ('Source/importer changed',path)
            protected.append(dict(file=path.name,sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    settings = dict(exposure=1.035,saturation=1.075,warmth=.95)
    lut = table(**settings)
    assert np.isfinite(lut).all() and (lut>=0).all()
    errors = []
    examples = []
    # Illumination combinations change the Standard PBR result before the
    # inverse. Cloud shadow and AO are scalar here, as in the saved shaders.
    # Independent brightness sweep also tests steep grade transition regions.
    for level in np.geomspace(.002,3,512):
        after = srgb8(display(compensate(level,lut),**settings))
        errors.append(float(after.max()-after.min()))
    combinations = 0
    maximum_diffuse_asphalt_hdr_channel=0
    for sun in ([1,.91,.75],[1,.8,.55],[.9,.96,1.1]):
        sun = np.array(sun)
        for ambient in ([.35,.45,.7],[.8,.76,.68],[.55,.56,.57]):
            for dot_sun in np.linspace(0,1,5):
                for cookie in (.75,.88,1):
                    for ao in (.55,.75,1):
                        color = .11*(sun*dot_sun*cookie+np.array(ambient)*.4)*ao
                        level = color@W
                        maximum_diffuse_asphalt_hdr_channel=max(maximum_diffuse_asphalt_hdr_channel,float(compensate(level,lut).max()))
                        before = srgb8(display(color,**settings))
                        after = srgb8(display(compensate(level,lut),**settings))
                        errors.append(float(after.max()-after.min()))
                        if len(examples)<4:
                            examples.append({'litHDR':color.tolist(),'beforeDisplaySRGB8':before.tolist(),'afterDisplaySRGB8':after.tolist()})
                        combinations += 1
    assert maximum_diffuse_asphalt_hdr_channel<.72, 'Diffuse asphalt enters the bloom prefilter'
    assert max(errors)<.75, ('Compensated final sRGB asphalt is not neutral',max(errors))
    swatches = {}
    for name,color in {'redPaving':[.52,.25,.17],'greenPaving':[.16,.42,.10],
                       'yellowLeaf':[.74,.51,.08],'redLeaf':[.60,.18,.06]}.items():
        before = np.array(color)
        after = warm_grade(before,.95)
        old_hue = colorsys.rgb_to_hsv(*before)[0]*360
        hue = colorsys.rgb_to_hsv(*after)[0]*360
        delta = min(abs(old_hue-hue),360-abs(old_hue-hue))
        assert delta < .7,(name,delta)
        swatches[name] = {'before':before.tolist(),'after':after.tolist(),'addedHueShiftDegrees':delta}
    for value in np.linspace(.82,1,32):
        assert np.array_equal(warm_grade([value]*3,.95),np.array([value]*3,dtype=np.float32)), 'Bright whites acquire added tint'
    neutral = np.array([.35]*3)
    warmed = warm_grade(neutral,.95)
    assert warmed.max()/warmed.min()>1.15, 'Autumn warmth remains imperceptibly weak'
    # Include the installer audit without confusing source-albedo mean with
    # this separate final-display compensation test.
    import contextlib,io
    with contextlib.redirect_stdout(io.StringIO()):
        installation=verify()
    return {'baselineCommit':BASE,'settingsTested':settings,
            'neutralOutputMode':'StreetPresentation.neutralAsphaltOutput=true; OnPreCull publishes shared uniform LUT',
            'litAsphaltLighting':'Standard PBR/GI preserved; scalar luminance is inverse-toned for the active camera display equations',
            'uniformLutSamples':32,'uniformLutHDRLuminanceRange':[2**-10,4],
            'float32BrightnessSweepSamples':512,'sunAmbientCookieAOCombinations':combinations,
            'maximumFinalSRGB8ChannelSpread':max(errors),'examples':examples,
            'addedGradeMaterialSwatches':swatches,'brightWhiteSamplesWithoutAdditionalTint':32,
            'neutralMidtoneWarmChannelRatio':float(warmed.max()/warmed.min()),
            'sourceAndImporterBytesPreserved':protected,'asphaltSourceInstallation':installation,
            'bloomPrefilter':{'softKneeBeginsAtHDR':.72,'hardThresholdHDR':1.05,'sourceContributionCap':2.5,'strengthInScene':.1,'samplesHDRToContribution':bloom_samples,'maximumDiffuseAsphaltHDRChannel':maximum_diffuse_asphalt_hdr_channel},
            'sourceTextureReads':3,'mainLightCloudShadowTextureReads':1,'totalAsphaltLightingTextureReadsWhenWeatherEnabled':4,
            'cloudShadowScope':'ForwardBase directional only; indirect/ambient unchanged; weather-disabled path skips cloud fetch',
            'extraFullScreenPasses':0,'extraPostTextureReads':0,
            'sunShaftRadialSamplesBeforeAfter':[24,12],'contactOcclusionDownsampleBeforeAfter':[3,4],
            'sunMasks':'quarter-resolution RHalf instead of source HDR format',
            'shaderModel':'3.5: dynamic fragment uniform-array lookup requires modern DX11/GL backends; Forward Standard lighting, deferred excluded',
            'limitations':['These are CPU references and saved-source checks, not Unity shader/C# compilation or a captured game frame.',
                           'The compensation models lit Standard color and Painted/saturation/Warm display grading; source texture detail/normal/AO/roughness remains intact.',
                           'Fog, bloom, antialiasing and additive shafts can tint mixed boundary/highlight pixels after material shading. They are not material-identifiable without a new mask/pass.',
                           'No GPU frame time, FPS or driver shader compilation was measured.']}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=REPO/'Reports/StreetRepair/asphalt-display-validation.json')
    args=parser.parse_args()
    output=run()
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(output,indent=2)+'\n')
    print(json.dumps(output,indent=2))
