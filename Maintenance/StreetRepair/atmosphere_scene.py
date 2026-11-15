"""Bake the saved atmosphere settings once from PR #3 head 0d73c23.

Does not regenerate street geometry, change existing material colors, or add
cars. Runtime weather/particle resources are bounded by their components.
"""
import json
import math
import re
import subprocess

from unity_yaml import ROOT, guid, scene
from connect_parking import save_scene

BASE = '0d73c23ee7bd71c144bf188d0f5b4cb91304c979'
WEATHER_ID, DUST_ID = 4000000200, 4000000201


def set_field(bs, component, key, value, append=False):
    typ, block = bs[component]
    updated, count = re.subn(r'^  ' + re.escape(key) + r': .*$',
                             lambda _: '  ' + key + ': ' + str(value), block, flags=re.M)
    if not count and append:
        updated = block.rstrip('\n') + '\n  ' + key + ': ' + str(value) + '\n'
    else:
        assert count == 1, (component, key, count)
    bs[component] = typ, updated


def add_component(bs, component, go, name, fields):
    assert component not in bs
    script = ROOT / ('Assets/Scripts/Environment/' + name + '.cs')
    block = f'''--- !u!114 &{component}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {guid(script)}, type: 3}}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::{name}
'''
    block += ''.join(f'  {key}: {value}\n' for key, value in fields.items())
    bs[component] = 114, block
    typ, old = bs[go]
    bs[go] = typ, old.replace('  m_Layer:', f'  - component: {{fileID: {component}}}\n  m_Layer:', 1)


def apply_settings(bs):
    for key, value in {'m_Fog': 1, 'm_FogColor': '{r: 0.64, g: 0.66, b: 0.67, a: 1}',
                       'm_FogMode': 3, 'm_FogDensity': .0015,
                       'm_AmbientMode': 0, 'm_ReflectionIntensity': .9}.items():
        set_field(bs, 2, key, value)
    for key, value in {'m_Color': '{r: 1, g: 0.91, b: 0.75, a: 1}',
                       'm_Intensity': 1.1}.items():
        set_field(bs, 1321379542, key, value)
    typ, light = bs[1321379542]
    light = re.sub(r'^    m_Strength: .*$', '    m_Strength: 0.72', light, flags=re.M)
    light = re.sub(r'^    m_CustomResolution: .*$', '    m_CustomResolution: 2048', light, flags=re.M)
    bs[1321379542] = typ, light
    pitch, yaw = map(math.radians, (22, 59))
    q = (math.sin(pitch/2)*math.cos(yaw/2), math.cos(pitch/2)*math.sin(yaw/2),
         -math.sin(pitch/2)*math.sin(yaw/2), math.cos(pitch/2)*math.cos(yaw/2))
    set_field(bs, 1321379543, 'm_LocalRotation', '{' + ', '.join(f'{k}: {v:.9g}' for k,v in zip('xyzw',q)) + '}')
    set_field(bs, 1321379543, 'm_LocalEulerAnglesHint', '{x: 22, y: 59, z: 0}')
    set_field(bs, 1319908496, 'field of view', 64)
    for key, value in {'runSpeed': 6.2, 'walkBobHeight': .009, 'runBobMultiplier': 1.3}.items():
        set_field(bs, 1090422550, key, value, append=True)
    for key, value in {'warmAutumnGrade': .95, 'vignette': .09, 'bloom': .10,
                       'sunShafts': .15, 'contactShadows': .26, 'neutralAsphaltOutput': 1}.items():
        set_field(bs, 1319908495, key, value, append=True)


def run():
    source, bs, items = scene()
    baseline = subprocess.check_output(['git', 'show', BASE + ':Unity_Walk/Assets/Scenes/Street.unity'],
                                        cwd=ROOT.parent, text=True)
    if source != baseline:
        raise SystemExit('Requires unchanged saved Street.unity at ' + BASE)
    apply_settings(bs)
    sky = ROOT / 'Assets/Art/PaintedDistrict/Lighting/BlueAutumnSky.mat'
    add_component(bs, WEATHER_ID, 1321379541, 'StreetAutumnWeather', {
        'sun': '{fileID: 1321379542}',
        'sky': '{fileID: 2100000, guid: ' + guid(sky) + ', type: 2}',
        'cloudHeight': 650, 'cloudWorldSize': 1200, 'cloudVelocity': '{x: 0.98, y: 0.684}',
        'cloudCoverage': .48, 'cloudSoftness': .18, 'cloudShadowStrength': .22,
        'cookieWorldSize': 2048,
    })
    dust = ROOT / 'Assets/Shaders/StreetSunlitDust.shader'
    add_component(bs, DUST_ID, 1319908492, 'StreetAtmosphereParticles', {
        'dustShader': '{fileID: 4800000, guid: ' + guid(dust) + ', type: 3}',
        'maximumDust': 24, 'birthsPerSecond': .85, 'opacity': .055,
    })
    from leaf_crown_anchors import apply_leaf_crowns
    crowns = apply_leaf_crowns(bs, items)
    save_scene(source, bs)
    report = {'baselineCommit': BASE, 'sunElevationDegrees': 22, 'sunYawDegrees': 59,
              'fieldOfViewDegrees': 64, 'newSceneComponents': [WEATHER_ID, DUST_ID],
              'geometryRegenerated': False, 'carsAdded': False, 'leafCrowns': crowns}
    target = ROOT.parent / 'Reports/StreetRepair/atmosphere-scene-settings.json'
    target.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k:v for k,v in report.items() if k != 'leafCrowns'}, indent=2))


if __name__ == '__main__':
    run()
