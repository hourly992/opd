"""Apply restrained autumn daylight and the existing final-pass warm grade.

The caller owns scene serialization. No material/texture assets, skybox,
exposure, light pose, shadow setting, lamp or camera geometry is changed.
The optional grade adds arithmetic to the existing presentation pass only;
there are no extra render targets, texture reads, lights, blits or components.
"""
import re

from unity_yaml import ROOT, field, guid


SUN_COLOR = (1.0, .948, .840, 1.0)
AMBIENT_COLORS = {
    # Replace the cool blue fill with a nearly neutral, faintly warm sky fill.
    'm_AmbientSkyColor': (.805, .800, .785, 1.0),
    # Restrain the former brown ground/equator fill so whites stay light.
    'm_AmbientEquatorColor': (.735, .715, .680, 1.0),
    'm_AmbientGroundColor': (.520, .475, .420, 1.0),
}
GRADE_AMOUNT = .35


def _color(value):
    return '{' + ', '.join(f'{k}: {v:g}' for k, v in zip('rgba', value)) + '}'


def _set(block, key, value, allow_new=False):
    pattern = r'^(  ' + re.escape(key) + r':) .*?$'
    result, count = re.subn(pattern, lambda m: m[1] + ' ' + value, block, flags=re.M)
    if count == 0 and allow_new:
        return block.rstrip('\n') + f'\n  {key}: {value}\n'
    assert count == 1, ('Expected one serialized property', key, count)
    return result


def apply(bs):
    """Mutate only RenderSettings, its sun color and presentation grade blocks."""
    render_settings = [(i, b) for i, (typ, b) in bs.items() if typ == 104]
    assert len(render_settings) == 1, 'Expected one saved RenderSettings block'
    render_id, block = render_settings[0]
    sun_id = int(re.search(r'fileID: (\d+)', field(block, 'm_Sun'))[1])
    assert bs[sun_id][0] == 108 and field(bs[sun_id][1], 'm_Type') == '1'
    presentation_guid = guid(ROOT / 'Assets/Scripts/Environment/StreetPresentation.cs')
    presentations = [(i, b) for i, (typ, b) in bs.items()
                     if typ == 114 and presentation_guid in b]
    assert presentations, 'No StreetPresentation component in the saved scene'
    before = {i: b for i, b in render_settings + presentations}
    before[sun_id] = bs[sun_id][1]
    changes = []
    for key, value in AMBIENT_COLORS.items():
        previous = field(block, key)
        block = _set(block, key, _color(value))
        changes.append({'block': render_id, 'property': key, 'before': previous, 'after': _color(value)})
    bs[render_id] = (104, block)
    light = bs[sun_id][1]
    previous = field(light, 'm_Color')
    bs[sun_id] = (108, _set(light, 'm_Color', _color(SUN_COLOR)))
    changes.append({'block': sun_id, 'property': 'm_Color', 'before': previous, 'after': _color(SUN_COLOR)})
    for component_id, block in presentations:
        previous = field(block, 'warmAutumnGrade', '0')
        bs[component_id] = (114, _set(block, 'warmAutumnGrade', f'{GRADE_AMOUNT:g}', allow_new=True))
        changes.append({'block': component_id, 'property': 'warmAutumnGrade',
                        'before': previous, 'after': f'{GRADE_AMOUNT:g}'})
    # Fail if future edits to this helper accidentally change exposure, shadow
    # intensity, fog, skybox or other unrelated serialized settings.
    for component_id, old in before.items():
        current = bs[component_id][1]
        keys = AMBIENT_COLORS if component_id == render_id else (
            ['m_Color'] if component_id == sun_id else ['warmAutumnGrade'])
        for key in keys:
            old = re.sub(r'^  ' + re.escape(key) + r': .*\n?', '', old, flags=re.M)
            current = re.sub(r'^  ' + re.escape(key) + r': .*\n?', '', current, flags=re.M)
        assert old == current, ('Warm autumn helper changed an unrelated field', component_id)
    return {'changedSceneBlocks': sorted(before), 'settings': changes,
            'extraRenderPasses': 0, 'extraTextureSamples': 0,
            'exposureShadowPoseFogSkyboxAndMaterialAssetsPreserved': True}


if __name__ == '__main__':
    import argparse
    import json
    from unity_yaml import scene
    from connect_parking import save_scene
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply-saved', action='store_true', required=True)
    parser.parse_args()
    source, saved, _ = scene()
    result = apply(saved)
    save_scene(source, saved)
    print(json.dumps(result, indent=2))
