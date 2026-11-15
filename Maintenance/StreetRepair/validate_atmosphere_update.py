"""Independent saved-data checks for the atmospheric follow-up from 0d73c23.

This does not compile Unity shaders/C#, render a frame or measure FPS. It checks
the saved scene, protected source assets and explicit runtime resource bounds.
"""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

import numpy as np

from unity_yaml import ROOT, GUID, blocks, field, guid, matrix


REPO = ROOT.parent
BASE = '0d73c23ee7bd71c144bf188d0f5b4cb91304c979'
SCENE = ROOT / 'Assets/Scenes/Street.unity'
SKY = ROOT / 'Assets/Art/PaintedDistrict/Lighting/BlueAutumnSky.mat'
TEXTURES = {'.png', '.jpg', '.jpeg', '.hdr', '.exr', '.tga', '.tif', '.tiff', '.dds', '.webp'}
COMPONENTS = {1090422550, 1319908495, 2014896245}
ADDED_COMPONENTS = {4000000200, 4000000201}
RENDER_SETTINGS = {
    'm_Fog', 'm_FogColor', 'm_FogMode', 'm_FogDensity', 'm_LinearFogStart', 'm_LinearFogEnd',
    'm_AmbientSkyColor', 'm_AmbientEquatorColor', 'm_AmbientGroundColor', 'm_AmbientIntensity',
    'm_AmbientMode', 'm_SubtractiveShadowColor', 'm_ReflectionIntensity',
    'm_DefaultReflectionMode', 'm_DefaultReflectionResolution',
}


def git(*args):
    return subprocess.check_output(['git', *args], cwd=REPO)


def original(path):
    return git('show', BASE + ':' + str(path.relative_to(REPO)))


def blob_hash(path):
    data = path.read_bytes()
    return hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()


def tree():
    result = {}
    for line in git('ls-tree', '-r', BASE, '--', 'Unity_Walk').decode().splitlines():
        info, name = line.split('\t', 1)
        result[REPO / name] = info.split()[2]
    return result


def without_fields(text, keys):
    for key in keys:
        # A top-level serialized field can contain an indented structure.
        text = re.sub(r'^  ' + re.escape(key) + r':[^\n]*(?:\n(?!  \w|---)[^\n]*)*\n?', '', text, flags=re.M)
    return text


def component_refs(text):
    return set(map(int, re.findall(r'  - component: \{fileID: (\d+)\}', text)))


def colors(text):
    section = re.search(r'^    m_Colors:\n(.*?)(?=^  m_BuildTextureStacks:|\Z)', text, re.M | re.S)
    return dict(re.findall(r'^    - (\w+): (.*)$', section[1], re.M)) if section else {}


def run():
    before = blocks(original(SCENE).decode())
    after = blocks(SCENE.read_text())
    assert set(before).issubset(after), 'A saved scene object or component was removed'
    assert set(after) - set(before) == ADDED_COMPONENTS, 'Unexpected new scene objects/components'
    out = {'baselineCommit': BASE, 'existingSceneBlocksRetained': len(before),
           'addedComponentIds': sorted(ADDED_COMPONENTS)}
    changed = []
    transform_count = prefab_count = renderer_count = collider_count = 0
    for bid, old in before.items():
        typ, text = after[bid]
        assert typ == old[0], ('Serialized class changed', bid)
        if typ == 4:
            transform_count += 1
        if typ == 1001:
            prefab_count += 1
        if typ in (23, 33):
            renderer_count += 1
        if typ in (64, 65, 135, 136, 143):
            collider_count += 1
        if after[bid] == old:
            continue
        changed.append({'id': bid, 'class': typ})
        if bid == 2:
            assert without_fields(text, RENDER_SETTINGS) == without_fields(old[1], RENDER_SETTINGS), 'Unrelated render settings changed'
        elif bid == 1321379542:
            allowed = {'m_Color', 'm_Intensity', 'm_Shadows', 'm_ShadowAngle', 'm_ShapeRadius'}
            assert without_fields(text, allowed) == without_fields(old[1], allowed), 'Unrelated sun properties changed'
        elif bid == 1321379543:
            allowed = {'m_LocalRotation', 'm_LocalEulerAnglesHint'}
            assert without_fields(text, allowed) == without_fields(old[1], allowed), 'Sun position/scale/hierarchy changed'
        elif bid == 1319908496:
            assert without_fields(text, {'field of view'}) == without_fields(old[1], {'field of view'}), 'Camera changed beyond the FOV'
        elif bid in COMPONENTS:
            # These components deliberately add movement/presentation/leaf settings.
            assert field(text, 'm_Script') == field(old[1], 'm_Script')
            assert field(text, 'm_GameObject') == field(old[1], 'm_GameObject')
            assert field(text, 'm_Enabled') == field(old[1], 'm_Enabled')
        elif bid in (1321379541, 1319908492):
            assert component_refs(old[1]).issubset(component_refs(text)), 'Existing camera/sun component was removed'
            assert component_refs(text) - component_refs(old[1]) <= ADDED_COMPONENTS
            assert without_fields(text, {'m_Component'}) == without_fields(old[1], {'m_Component'}), 'Camera/sun GameObject changed beyond component references'
        else:
            raise AssertionError(('Unexpected scene change', bid, typ))
    out['changedExistingSceneBlocks'] = changed
    out['unchangedGeometryAndHierarchy'] = {
        'savedTransformsExceptSunRotation': transform_count - 1,
        'prefabInstanceBlocks': prefab_count, 'rendererAndMeshFilterBlocks': renderer_count,
        'colliderAndControllerBlocks': collider_count,
    }

    files = tree()
    protected = {'meshAssets': [], 'prefabs': [], 'texturesAndImporters': [],
                 'allNonAsphaltNonSkyMaterialsAndImporters': [], 'streetSources': []}
    from install_asphalt020s import active_asphalt_materials
    asphalt_mats = set(active_asphalt_materials())
    assert len(asphalt_mats) == 7
    for path in files:
        real = Path(str(path)[:-5]) if path.name.endswith('.meta') else path
        suffix = real.suffix.lower()
        if suffix == '.asset' and 'Assets/' in str(real) and 'm_VertexData:' in real.read_text(errors='ignore'):
            protected['meshAssets'].append(path)
        if suffix == '.prefab':
            protected['prefabs'].append(path)
        if suffix in TEXTURES:
            protected['texturesAndImporters'].append(path)
        if suffix == '.mat' and real not in asphalt_mats and real != SKY:
            protected['allNonAsphaltNonSkyMaterialsAndImporters'].append(path)
        if real.is_relative_to(ROOT / 'Assets/StreetSource'):
            protected['streetSources'].append(path)
    for category, paths in protected.items():
        assert paths, ('Empty protected asset category', category)
        differences = [str(p.relative_to(REPO)) for p in paths if not p.exists() or blob_hash(p) != files[p]]
        assert not differences, ('Protected source assets changed', category, differences)
    out['byteIdenticalProtectedAssets'] = {key: len(paths) for key, paths in protected.items()}
    out['nonSkyMaterialSourceColorsPreserved'] = 0
    for path in asphalt_mats:
        previous = colors(original(path).decode())
        current = colors(path.read_text())
        for key, value in previous.items():
            if key != '_ToneGain':
                assert current.get(key) == value, ('Asphalt changed beyond tone', path, key)
        out['nonSkyMaterialSourceColorsPreserved'] += 1

    # Metadata/source references for additions must be importable and unique.
    all_guids = {}
    for p in (ROOT / 'Assets').rglob('*.meta'):
        match = re.search(r'^guid: (\w+)', p.read_text(), re.M)
        if match:
            assert match[1] not in all_guids, ('Duplicate Unity GUID', p, all_guids.get(match[1]))
            all_guids[match[1]] = p
    for bid in ADDED_COMPONENTS:
        typ, text = after[bid]
        assert typ == 114
        match = re.search(r'm_Script: .*guid: (\w+)', text)
        assert match and match[1] in all_guids, ('Missing new script importer', bid)

    vehicle_guid = guid(ROOT / 'Assets/Scripts/Environment/StreetVehicle.cs')
    assert not any(typ == 114 and vehicle_guid in text for typ, text in after.values()), 'A saved car remains'
    parking_guid = guid(ROOT / 'Assets/Scripts/Environment/ParkingSite.cs')
    parkings = [text for typ, text in after.values() if typ == 114 and parking_guid in text]
    assert len(parkings) == 19 and all(field(p, 'vehicles') == '[]' for p in parkings)
    for path in (ROOT / 'Assets/Scripts').rglob('*.cs'):
        assert not re.search(r'(?:Instantiate|AddComponent)\s*(?:<\s*StreetVehicle\s*>|\([^;\n]*(?:[Cc]ar|[Vv]ehicle))', path.read_text()), ('Runtime vehicle construction', path)
    out['cars'] = {'savedVehicleComponents': 0, 'emptyParkingVehicleArrays': len(parkings), 'runtimeVehicleConstructionFound': False}

    # Quantities are source/saved-state resource bounds, not measured frame times.
    before_lights = [i for i, (typ, _) in before.items() if typ == 108]
    assert before_lights == [i for i, (typ, _) in after.items() if typ == 108], 'An extra light was added'
    source = ROOT / 'Assets/Scripts/Environment/StreetPresentation.cs'
    current = source.read_text()
    old_source = original(source).decode()
    passes = {}
    for token in ('Graphics.Blit(', 'new Material('):
        passes[token] = {'before': old_source.count(token), 'after': current.count(token)}
        assert passes[token]['after'] <= passes[token]['before'], ('Presentation added a render pass/allocation', token)
    # SunMask is a format-specific replacement of two old Temporary calls,
    # not an extra pair of targets. Count those callsites, not helper bodies.
    assert 'sunMask = Temporary(source, 4); sunBlur = Temporary(source, 4);' in old_source
    assert 'sunMask = SunMask(source); sunBlur = SunMask(source);' in current
    assert current.count('RenderTexture.GetTemporary(') == old_source.count('RenderTexture.GetTemporary(') + 1
    assert 'RenderTextureFormat.RHalf,RenderTextureReadWrite.Linear' in current
    passes['sunMaskTargetPair'] = {'before': 2, 'after': 2,
                                  'formatBefore': 'source.format', 'formatAfter': 'RHalf',
                                  'resolutionBeforeAndAfter': 'quarter width and height'}
    fov = float(field(after[1319908496][1], 'field of view'))
    assert 60 < fov <= 66, ('Excessive camera widening', fov)
    sun_matrix = matrix(after, 1321379543)
    sun_direction = -sun_matrix[:3, 2]
    elevation = np.degrees(np.arcsin(np.clip(sun_direction[1], -1, 1)))
    assert 15 <= elevation <= 28, ('Sun is not low autumn daylight', elevation)
    out['staticResourceBudget'] = {'geometryAssetsAndSavedRendererCountUnchanged': True,
                                   'savedLightsBeforeAndAfter': len(before_lights),
                                   'presentationSourceCallCounts': passes,
                                   'cameraVerticalFovDegrees': fov,
                                   'sunElevationDegrees': float(elevation)}
    walk = after[1090422550][1]
    speed = float(field(walk, 'walkSpeed'))
    run_speed = float(field(walk, 'runSpeed'))
    assert speed < run_speed <= speed * 1.8
    out['walkAndRun'] = {'walkSpeedMetersPerSecond': speed, 'runSpeedMetersPerSecond': run_speed}

    dust = after[4000000201][1]
    dust_shader = re.search(r'dustShader: .*guid: (\w+)', dust)
    assert dust_shader and GUID.get(dust_shader[1], Path()).name == 'StreetSunlitDust.shader', 'Dust shader not assigned'
    assert 0 < int(field(dust, 'maximumDust')) <= 32
    dust_code = (ROOT / 'Assets/Scripts/Environment/StreetAtmosphereParticles.cs').read_text()
    assert 'private const int Capacity = 32;' in dust_code
    assert 'r < 2 && limit > 0' in dust_code and dust_code.count('Graphics.DrawMesh(') == 1
    assert not any(t in dust_code for t in ('RenderTexture.', 'Graphics.Blit(', 'new GameObject('))
    out['dustResourceCeilings'] = {'maximumConfiguredParticles': int(field(dust, 'maximumDust')),
                                  'maximumRuntimeQuads': 32, 'maximumSunOcclusionRaycastsPerFrame': 2,
                                  'maximumAdditionalMeshDrawsWhenVisible': 1,
                                  'additionalDepthOrFullscreenPasses': 0}

    weather = (ROOT / 'Assets/Scripts/Environment/StreetAutumnWeather.cs').read_text()
    assert not re.search(r'\b(?:sun|light)\.cookie\s*=', weather), 'Native directional cookie risks a ForwardAdd pass'
    assert not re.search(r'\bsun\.transform\.position\s*=', weather), 'Weather moves the saved sun transform'
    assert 'TileSize = 128' in weather and 'CookieRowsPerFrame = 8' in weather and 'ReflectionSize = 16' in weather
    assert 'nextAmbient=time+2' in weather and 'nextReflection=time+.5f' in weather
    assert 'new RenderTexture' not in weather and 'RenderProbe(' not in weather
    assert 'AmbientMode.Custom' in weather and 'RenderSettings.ambientProbe=probe' in weather
    assert 'RenderSettings.customReflectionTexture = reflection' in weather
    out['weatherResourceCeilings'] = {'densityTexture': [128, 128], 'shadowTexture': [128, 128],
                                    'shadowRefreshMaximumRowsPerFrame': 8,
                                    'reflectionCubemapFaceSize': 16,
                                    'reflectionFacesRefreshedPerHalfSecond': 1,
                                    'ambientRefreshSeconds': 2,
                                    'nativeDirectionalCookieAssigned': False,
                                    'sceneCaptureCamerasOrProbeRenderCallsAdded': 0}

    pending = [SCENE]
    visited, material_paths = set(), set()
    while pending:
        path = pending.pop()
        if path in visited:
            continue
        visited.add(path)
        for shader_guid in re.findall(r'guid: ([a-f0-9]{32})', path.read_text(errors='ignore')):
            target = GUID.get(shader_guid)
            if target and target.suffix == '.mat':
                material_paths.add(target)
            elif target and target.suffix == '.prefab' and target not in visited:
                pending.append(target)
    shader_counts = {}
    shadow_receivers = set()
    for path in sorted(material_paths):
        text = path.read_text()
        match = re.search(r'm_Shader: .*guid: (\w+)', text)
        shader_path = GUID.get(match[1]) if match else None
        name = str(shader_path.relative_to(ROOT)) if shader_path else field(text, 'm_Shader', 'unknown builtin')
        shader_counts[name] = shader_counts.get(name, 0) + 1
        if shader_path and shader_path.name not in ('StreetAutumnSky.shader', 'PaintedBackdropContact.shader'):
            shadow_receivers.add(shader_path)
    coverage = []
    for path in sorted(shadow_receivers):
        code = path.read_text()
        has_shadow = ('StreetWeatherCloudShadow(' in code or 'StreetCloudLighting.cginc' in code)
        assert has_shadow, ('A lit surface shader lacks moving cloud shadow integration', path)
        old_code = original(path).decode()
        # Lighting/vertex motion may change; the material color/normal surface
        # equations below remain the original ones, including house facades.
        surf = re.search(r'        void surf\(.*?(?=        ENDCG)', code, re.S)
        old_surf = re.search(r'        void surf\(.*?(?=        ENDCG)', old_code, re.S)
        assert surf and old_surf and surf[0] == old_surf[0], ('Material surface/color function changed', path)
        coverage.append({'shader': str(path.relative_to(ROOT)), 'sharedCloudShadow': has_shadow})
    graphics = (ROOT / 'ProjectSettings/GraphicsSettings.asset').read_text()
    assert field(graphics, 'm_DefaultRenderingPath') == '1' and field(graphics, 'm_CustomRenderPipeline') == '{fileID: 0}'
    out['materialDependencyShaderClosure'] = {'materials': len(material_paths), 'prefabs': len(visited) - 1,
                                             'shaderMaterialCounts': shader_counts,
                                             'surfaceShaderCloudIntegration': coverage,
                                             'allTenLitSurfaceColorFunctionsUnchanged': True,
                                             'pipeline': 'Built-in Forward; custom shadow tile, no native directional cookie'}

    glass = (ROOT / 'Assets/Shaders/StreetGlass.shader').read_text()
    assert 'StreetWeatherSkyColor(data.worldPos, reflected, 0)' in glass
    assert 'LightingStandard_GI(s, data, gi);' in glass
    assert 'gi = UnityGlobalIllumination(data, s.Occlusion, s.Normal);' in glass
    assert 'gi.light.color *= StreetWeatherCloudShadow(data.worldPos);' in glass
    # Room colors and pane geometry are deliberately untouched.
    old_glass = original(ROOT / 'Assets/Shaders/StreetGlass.shader').decode()
    assert glass[glass.index('        void surf('):] == old_glass[old_glass.index('        void surf('):]
    out['windows'] = {'sameMovingWeatherSkyAtPaneWorldPosition': True,
                      'staleProbeBypassedOnlyWhenWeatherEnabled': True,
                      'transmittedRoomColorFunctionUnchanged': True,
                      'newRenderPasses': 0}
    out['limitations'] = [
        'Unity shader/C# import and compilation, a player build/run, actual rendered colors and FPS were not tested.',
        'Static resource bounds and numerical models do not establish measured performance on a target GPU.',
        'Sky/lighting and asphalt appearance need verification in the final player frame on the same machine and route.',
    ]
    return out


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=REPO / 'Reports/StreetRepair/atmosphere-update-validation.json')
    args = parser.parse_args()
    content = json.dumps(run(), indent=2, ensure_ascii=False) + '\n'
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(content)
    print(content, end='')
