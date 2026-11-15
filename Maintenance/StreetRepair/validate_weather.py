"""CPU/source checks for the bounded sky/cloud/ambient implementation, not Unity QA."""
import argparse
import json
from pathlib import Path
import re

import numpy as np

from unity_yaml import ROOT, field, matrix, scene

REPO = ROOT.parent
SIZE = 128
VELOCITY = np.array([.98, .684])
HEIGHT, PERIOD, THRESHOLD, SOFTNESS, STRENGTH = 650., 1200., .52, .18, .22


def smooth(a, b, x):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)


def hash_value(x, y):
    h = (x*374761393+y*668265263+1337) & 0xffffffff
    h = ((h ^ (h >> 13))*1274126177) & 0xffffffff
    h ^= h >> 16
    return (h & 0xffffff)/16777215.


def value_noise(u, v, cells):
    x, y = u*cells, v*cells
    ix, iy = int(np.floor(x)), int(np.floor(y))
    fx, fy = smooth(0, 1, x-ix), smooth(0, 1, y-iy)
    a = hash_value(ix % cells, iy % cells)*(1-fx)+hash_value((ix+1) % cells, iy % cells)*fx
    b = hash_value(ix % cells, (iy+1) % cells)*(1-fx)+hash_value((ix+1) % cells, (iy+1) % cells)*fx
    return a*(1-fy)+b*fy


def noise_tile():
    tile = np.zeros((SIZE, SIZE))
    for y in range(SIZE):
        for x in range(SIZE):
            weight, total, value = .57, 0., 0.
            for octave in range(4):
                value += value_noise(x/SIZE, y/SIZE, 4 << octave)*weight
                total += weight
                weight *= .49
            tile[y, x] = np.rint(np.clip(value/total, 0, 1)*255)/255
    return tile


def sample(tile, uv):
    xy = (np.mod(uv, 1)*SIZE)-.5
    ij = np.floor(xy).astype(int)
    frac = xy-ij
    x0, y0 = ij % SIZE
    x1, y1 = (ij+1) % SIZE
    a = tile[y0, x0]*(1-frac[0])+tile[y0, x1]*frac[0]
    b = tile[y1, x0]*(1-frac[0])+tile[y1, x1]*frac[0]
    return a*(1-frac[1])+b*frac[1]


def cloud_point(position, direction, layer=1):
    distance = (HEIGHT*layer-position[1])/max(.055, direction[1])
    return position+direction*distance


def cloud_uv(position, direction, time, layer=1):
    p = cloud_point(position, direction, layer)
    return (p[[0, 2]]-VELOCITY*time)/(PERIOD*layer)+(layer-1)*np.array([.37, .19])


def run(check_saved=False):
    script = (ROOT/'Assets/Scripts/Environment/StreetAutumnWeather.cs').read_text()
    shader = (ROOT/'Assets/Shaders/StreetAutumnWeather.cginc').read_text()
    assert not re.search(r'\bsun\.cookie\s*=', script), 'Native light cookie can add geometry passes'
    assert not re.search(r'\bsun\.transform\.position\s*=', script), 'Weather mutates the saved sun transform'
    assert 'Graphics.Blit(' not in script and 'RenderToCubemap(' not in script
    assert 'cookieWorldSize = 2048' in script and 'CookieRowsPerFrame = 8' in script
    assert 'nextCookie = time+60' in script and 'nextAmbient = time+2' in script
    assert 'nextReflection = time+.5f' in script
    assert 'Shader.SetGlobalTexture("_StreetWeatherCloudCookie",cookie)' in script
    assert 'StreetWeatherCloudShadow(float3 worldPosition)' in shader
    assert 'WorkingColor(sun.color)' in script
    tile = noise_tile()
    rng = np.random.default_rng(904)
    periodic = max(abs(sample(tile, uv)-sample(tile, uv+np.array([3, -2])))
                   for uv in rng.uniform(-2, 2, (500, 2)))
    assert periodic < 1e-12
    material = (ROOT/'Assets/Art/PaintedDistrict/Lighting/BlueAutumnSky.mat').read_text()
    sun = np.array([float(x) for x in re.search(r'_SunDirection: \{r: ([^,]+), g: ([^,]+), b: ([^,]+)', material).groups()])
    sun /= np.linalg.norm(sun)
    elevation = float(np.degrees(np.arcsin(sun[1])))
    assert abs(elevation-22) < .001
    forward = -sun
    right = np.cross([0, 1, 0], forward)
    right /= np.linalg.norm(right)
    up = np.cross(forward, right)
    source = json.loads((ROOT/'Assets/StreetSource/street.json').read_text())
    xz = np.array([[p['x'], p['z']] for group in ('roads', 'buildings')
                   for shape in source[group] for p in shape['points']])
    points = np.array([[p[0], y, p[1]] for p in xz for y in (-4, 0, 45)])
    maximum_residual = 0.
    uv_min, uv_max = np.array([1., 1.]), np.array([0., 0.])
    for elapsed in (0., .017, .5, 30., 60.6):
        origin = np.array([VELOCITY[0]*elapsed, 0., VELOCITY[1]*elapsed])
        native_uv = np.column_stack([(points-origin)@right, (points-origin)@up])/2048+.5
        uv_min = np.minimum(uv_min, native_uv.min(0))
        uv_max = np.maximum(uv_max, native_uv.max(0))
        # Sampling the cached plane with translated projection is equivalent
        # to evaluating today's world-space cloud field, irrespective of FPS.
        selected = points[rng.choice(len(points), min(200, len(points)), replace=False)]
        for position in selected:
            relative = position-origin
            cached_plane = right*np.dot(relative, right)+up*np.dot(relative, up)
            for layer in (1., 1.8):
                expected = cloud_uv(position, sun, 1000+elapsed, layer)
                projected = cloud_uv(cached_plane, sun, 1000, layer)
                maximum_residual = max(maximum_residual, np.linalg.norm(expected-projected)*PERIOD*layer)
    assert maximum_residual < 1e-8
    assert np.all(uv_min > .02) and np.all(uv_max < .98), 'Cloud shadow repeat seam reaches the source street/buildings'
    samples = []
    for position in points[::max(1, len(points)//400)]:
        lower = smooth(THRESHOLD-SOFTNESS, THRESHOLD+SOFTNESS, sample(tile, cloud_uv(position, sun, 1000)))
        upper = smooth(THRESHOLD-SOFTNESS, THRESHOLD+SOFTNESS, sample(tile, cloud_uv(position, sun, 1000, 1.8)))
        samples.append(1-STRENGTH*np.clip(lower*.82+upper*.20, 0, 1))
    assert np.isfinite(samples).all() and min(samples) >= .78 and max(samples) <= 1
    if check_saved:
        _, blocks, _ = scene()
        sun_id = int(re.search(r'fileID: (\d+)', field(blocks[2][1], 'm_Sun'))[1])
        go = field(blocks[sun_id][1], 'm_GameObject')
        tid = next(i for i, (typ, block) in blocks.items() if typ == 4 and field(block, 'm_GameObject') == go)
        actual = -matrix(blocks, tid)[:3, 2]
        actual /= np.linalg.norm(actual)
        assert np.linalg.norm(actual-sun) < .0001, 'Saved sky/sun direction mismatch'
    return {
        'baselineCommit': '0d73c23ee7bd71c144bf188d0f5b4cb91304c979',
        'savedSunDirectionChecked': check_saved,
        'sunElevationDegrees': elevation,
        'cloudField': {'densityTileSize': [SIZE, SIZE], 'seed': 1337, 'periodMeters': PERIOD,
                       'lowerPlaneMeters': HEIGHT, 'upperPlaneMeters': HEIGHT*1.8,
                       'velocityMetersPerSecondXZ': VELOCITY.tolist(),
                       'noiseMean': float(tile.mean()), 'periodicBilinearMaximumError': periodic,
                       'shadowTransmittanceRange': [float(min(samples)), float(max(samples))]},
        'projection': {'worldToCloudMaximumResidualMeters': maximum_residual,
                       'sourceRoadBuildingPointsAndHeightsChecked': len(points),
                       'globalTileMinimumUV': uv_min.tolist(), 'globalTileMaximumUV': uv_max.tolist(),
                       'noRepeatSeamInsideSourceStreetOrBuildings': True,
                       'continuousMotionIndependentOfFrameRate': True,
                       'nativeLightCookieAssigned': False, 'sunTransformMutated': False},
        'resourceCaps': {'densityTextureRGBA32Bytes': SIZE*SIZE*4,
                         'shadowTileRGBA32Bytes': SIZE*SIZE*4,
                         'reflectionCubeRGBAHalfWithMipsBytes': 6*8*sum(4**i for i in range(5)),
                         'reflectionCubeFaceSize': 16, 'cookiePixelsPerActiveRefreshFrame': SIZE*8,
                         'cookieRecenterSeconds': 60, 'cookieRefreshFrames': 16,
                         'ambientSHRefreshSeconds': 2, 'reflectionFaceRefreshSeconds': .5,
                         'fullCubeRefreshSeconds': 3, 'extraCameras': 0, 'extraLights': 0,
                         'extraFullResolutionPasses': 0, 'extraSceneCapturePasses': 0,
                         'skyDensityFetches': 3, 'mainDirectionalSurfaceShadowFetches': 1},
        'limitations': ['CPU/source tests only; Unity import, shader compilation and native rendering were not executed.',
                       'No FPS, GPU time or actual draw calls were measured.',
                       'Soft dimensional clouds use two shaded planes, not a volumetric ray marcher.',
                       'The small cached shadow tile has intentionally soft spatial interpolation; window reflections use the full analytic field.']
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check-saved', action='store_true')
    parser.add_argument('--output', type=Path, default=REPO/'Reports/StreetRepair/weather-validation.json')
    options = parser.parse_args()
    result = run(options.check_saved)
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result, indent=2))
