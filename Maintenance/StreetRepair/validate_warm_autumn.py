"""Saved-data and color-invariant checks; this is not a Unity/FPS check."""
import argparse
import colorsys
import json
from pathlib import Path
import re
import subprocess

import numpy as np

from unity_yaml import ROOT, blocks
from warm_autumn import GRADE_AMOUNT, apply


BASE = 'c5d2798c790a33b65f3660241cdc443a188f64d2'
REPO = ROOT.parent


def original(path):
    return subprocess.check_output(['git', 'show', BASE + ':' + str(path.relative_to(REPO))],
                                   cwd=REPO, text=True)


def smoothstep(lo, hi, x):
    t = np.clip((x - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def run(check_saved=False):
    source = ROOT / 'Assets/Shaders/StreetPresentation.shader'
    shader = source.read_text()
    old_shader = original(source)
    runtime = ROOT / 'Assets/Scripts/Environment/StreetPresentation.cs'
    code = runtime.read_text()
    old_code = original(runtime)
    for term in ['tex2D(', 'tex2Dlod(', 'texCUBE(']:
        assert shader.count(term) == old_shader.count(term), 'Grade added a texture read'
    assert len(re.findall(r'^        Pass\s*$', shader, re.M)) == len(re.findall(r'^        Pass\s*$', old_shader, re.M))
    for term in ['Graphics.Blit(', 'RenderTexture.GetTemporary(', 'new Material(']:
        assert code.count(term) == old_code.count(term), 'Grade added a render pass/allocation'
    assert 'UNITY_BRANCH if (_WarmAutumnGrade > 0)' in shader

    original_blocks = blocks(original(ROOT / 'Assets/Scenes/Street.unity'))
    desired = dict(original_blocks)
    changes = apply(desired)
    allowed = set(changes['changedSceneBlocks'])
    assert [i for i in original_blocks if original_blocks[i] != desired[i]] == sorted(allowed)
    assert len(desired) == len(original_blocks)
    second = dict(desired)
    apply(second)
    assert desired == second, 'Warm settings are not idempotent'
    if check_saved:
        saved = blocks((ROOT / 'Assets/Scenes/Street.unity').read_text())
        assert all(saved[i] == desired[i] for i in allowed), 'Saved lighting/grade differs from the intended settings'

    # Sample the added operation in isolation. The existing display transform
    # is unchanged: preserving whites/luminance and limiting hue changes here
    # therefore bounds the effect added by this revision.
    weights = np.array([.2126, .7152, .0722])
    function = re.search(r'float3 WarmAutumnGrade\(float3 color\)(.*?)\n        }', shader, re.S)[1]
    gain = np.array([float(x) for x in re.search(r'color \* float3\(([^)]+)\)', function)[1].split(',')])
    highlight_lo, highlight_hi = map(float, re.search(r'smoothstep\(([^,]+),([^,]+),peak\)', function).groups())
    chroma_min, chroma_scale = map(float, re.search(r'lerp\(1,([^,]+),saturate\(chroma \* ([^)]+)\)\)', function).groups())

    def grade(color):
        color = np.array(color, dtype=float)
        peak, chroma = color.max(), color.max() - color.min()
        amount = GRADE_AMOUNT * (1 - smoothstep(highlight_lo, highlight_hi, peak)) * (
            1 - (1 - chroma_min) * np.clip(chroma * chroma_scale, 0, 1))
        warmed = color * gain
        warmed *= (color @ weights) / max(.00001, warmed @ weights)
        return color + (warmed - color) * amount

    whites = [[v, v, v] for v in np.linspace(.82, 1, 64)]
    whites += [[.88, .88, .85], [.94, .94, .91], [1, 1, .98]]
    assert all(np.array_equal(grade(c), c) for c in whites), 'Added grade tints a bright white highlight'
    luminance_error = 0
    for rgb in np.ndindex(9, 9, 9):
        color = np.array(rgb, dtype=float) / 8
        result = grade(color)
        assert np.isfinite(result).all()
        luminance_error = max(luminance_error, abs(result @ weights - color @ weights))
    assert luminance_error < 1e-10, 'Added grade changes luminance/exposure'
    for v in np.linspace(.02, .75, 64):
        result = grade([v, v, v])
        assert result.max() / result.min() < 1.03, 'Neutral shadows became a strong orange filter'
    samples = {'redPaver': [.52, .25, .17], 'greenPaverOrLeaf': [.16, .42, .10],
               'redLeaf': [.60, .18, .06], 'yellowLeaf': [.74, .51, .08]}
    swatches = {}
    for name, color in samples.items():
        result = grade(color)
        old_hue = colorsys.rgb_to_hsv(*color)[0] * 360
        new_hue = colorsys.rgb_to_hsv(*result)[0] * 360
        delta = min(abs(new_hue - old_hue), 360 - abs(new_hue - old_hue))
        assert delta < .3, ('Material hue shifted too far', name, delta)
        swatches[name] = {'before': color, 'after': result.tolist(), 'hueDifferenceDegrees': delta}

    changed = subprocess.check_output(['git', 'diff', '--name-only', BASE], cwd=REPO, text=True).splitlines()
    protected = [p for p in changed if p.endswith('.mat') and any(t in p.lower()
                 for t in ['paver', 'paving', '/leaves/', 'foliage', 'garagewhiteconcrete'])]
    assert not protected, ('Paving, foliage or white concrete material changed', protected)
    return {'baseCommit': BASE, 'savedSceneChecked': check_saved, **changes,
            'brightWhiteHighlightSamplesUntinted': len(whites),
            'sampledDisplayColors': 9 ** 3, 'maximumAddedGradeLuminanceError': luminance_error,
            'neutralShadowChannelRatioLimit': 1.03, 'materialSwatches': swatches,
            'pavingFoliageAndWhiteConcreteMaterialsUnchanged': True,
            'unityShaderCompilationAndFrameTimes': 'NOT RUN: Unity is unavailable'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check-saved', action='store_true')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    content = json.dumps(run(args.check_saved), indent=2) + '\n'
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(content)
    print(content, end='')
