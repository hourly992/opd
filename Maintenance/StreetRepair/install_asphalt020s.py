"""Install the specifically requested ambientCG Asphalt020S 2K source maps.

No downloads or fallback textures: pass the original official ZIP using --archive
or its four unchanged original JPEG maps using --maps-dir. The guard validates
all four requested source maps before touching any materials.
Original color/NormalGL/roughness/AO JPEGs are preserved byte-for-byte; runtime
AO/roughness is packed losslessly into a single PNG to keep three texture reads.
"""
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image

from unity_yaml import ROOT, GUID, field, guid, meta
from surfaces import folders

ASSET_ID = 'Asphalt020S'
PACK = 'Asphalt020S_2K-JPG'
SOURCE_URL = 'https://ambientcg.com/view?id=Asphalt020S'
DOWNLOAD_URL = 'https://ambientcg.com/get?file=Asphalt020S_2K-JPG.zip'
CDN_URL = 'https://acg-download.struffelproductions.com/file/ambientCG-Web/download/Asphalt020S_TN7SL6z0/Asphalt020S_2K-JPG.zip'
FOLDER = ROOT / 'Assets/ThirdParty/ambientCG/Asphalt020S'
MAP_NAMES = {name: PACK + '_' + suffix + '.jpg' for name, suffix in
             [('color', 'Color'), ('normalGL', 'NormalGL'), ('roughness', 'Roughness'), ('ambientOcclusion', 'AmbientOcclusion')]}
SHADER = r'''Shader "Street/Asphalt020S"
{
    Properties
    {
        _MainTex ("ambientCG Asphalt020S color", 2D) = "white" {}
        [Normal] _BumpMap ("ambientCG Asphalt020S NormalGL", 2D) = "bump" {}
        _MaskMap ("ambientCG AO (R) and roughness (G)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TileMeters ("Physical tile size in metres", Vector) = (1.85,1.85,0,0)
        _BumpStrength ("Normal strength", Range(0,1)) = 1
        _OcclusionStrength ("AO strength", Range(0,1)) = 1
        [HideInInspector] _SurfaceClass ("Asphalt class", Float) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Face culling", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #pragma multi_compile_instancing
        sampler2D _MainTex, _BumpMap, _MaskMap;
        float4 _TileMeters;
        fixed4 _Color;
        half _BumpStrength, _OcclusionStrength;
        struct Input { float3 worldPos; float3 worldNormal; INTERNAL_DATA };
        void vert(inout appdata_full v)
        {
            // Saved offline meshes can lack tangents. Supply an orthogonal basis
            // without changing geometry, normals, collision or any shared assets.
            float3 n = normalize(v.normal);
            float3 t = abs(n.y) > .8 ? float3(1,0,0) : normalize(cross(float3(0,1,0),n));
            t = normalize(t - n * dot(n,t));
            v.tangent = float4(t,1);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = normalize(WorldNormalVector(IN,float3(0,0,1)));
            float3 t = abs(n.y) > .8 ? normalize(float3(1,0,0) - n*n.x) : normalize(cross(float3(0,1,0),n));
            float3 b = normalize(cross(n,t));
            float2 uv = float2(dot(IN.worldPos,t),dot(IN.worldPos,b)) / max(_TileMeters.xy,.05);
            float3 map = UnpackNormal(tex2D(_BumpMap,uv));
            map = normalize(lerp(float3(0,0,1),map,_BumpStrength));
            float3 world = normalize(t*map.x + b*map.y + n*map.z);
            float3 tangent = normalize(WorldNormalVector(IN,float3(1,0,0)));
            float3 bitangent = normalize(WorldNormalVector(IN,float3(0,1,0)));
            o.Normal = normalize(float3(dot(world,tangent),dot(world,bitangent),dot(world,n)));
            float2 mask = tex2D(_MaskMap,uv).rg;
            o.Albedo = tex2D(_MainTex,uv).rgb * _Color.rgb;
            o.Occlusion = lerp(1,mask.r,_OcclusionStrength);
            o.Smoothness = 1-saturate(mask.g);
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Standard"
}
'''


def active_asphalt_materials():
    pending = [ROOT / 'Assets/Scenes/Street.unity']
    visited = set()
    materials = set()
    while pending:
        p = pending.pop()
        if p in visited:
            continue
        visited.add(p)
        for g in re.findall(r'guid: ([a-f0-9]{32})', p.read_text(errors='ignore')):
            q = GUID.get(g)
            if q and q.suffix == '.mat':
                text = q.read_text()
                if 'asphalt' in field(text, 'm_Name', '').lower() or float(field(text, '- _SurfaceClass', '0')) == 4:
                    materials.add(q)
            elif q and q.suffix == '.prefab' and q not in visited:
                pending.append(q)
    return sorted(materials)


def texture_meta(p, normal=False, scalar=False, runtime_limit=2048):
    template = ROOT / 'Assets/ThirdParty/PolyHaven/RoadSurfaces2K/asphalt_01' / ('Normal.jpg.meta' if normal else 'Albedo.jpg.meta')
    text = template.read_text()
    g = hashlib.md5(str(p.relative_to(ROOT)).encode()).hexdigest()
    text = re.sub(r'^guid: .*', 'guid: ' + g, text, flags=re.M)
    text = re.sub(r'sRGBTexture: \d', 'sRGBTexture: ' + str(int(not normal and not scalar)), text)
    text = re.sub(r'textureType: \d+', 'textureType: ' + ('1' if normal else '0'), text)
    # All three runtime textures are shared across every road/drive/ramp finish.
    text = re.sub(r'maxTextureSize: \d+', 'maxTextureSize: ' + str(runtime_limit), text)
    text = re.sub(r'aniso: \d+', 'aniso: 8', text)
    Path(str(p) + '.meta').write_text('\n'.join(line.rstrip() for line in text.splitlines()) + '\n')


def apply_material(p, paths, shader):
    text = p.read_text()
    text = re.sub(r'  m_Shader: .*', f'  m_Shader: {{fileID: 4800000, guid: {guid(shader)}, type: 3}}', text)
    tex = '    m_TexEnvs:\n'
    for key, path in [('_MainTex', paths['color']), ('_BumpMap', paths['normalGL']), ('_MaskMap', paths['packedMask'])]:
        tex += f'    - {key}:\n        m_Texture: {{fileID: 2800000, guid: {guid(path)}, type: 3}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n'
    text = re.sub(r'    m_TexEnvs:.*?(?=    m_Ints:)', tex, text, flags=re.S)
    # Keep material names/GUIDs, and surface classification used by parking QA.
    text = re.sub(r'    m_Floats:.*?(?=    m_Colors:)', '''    m_Floats:
    - _BumpStrength: 1
    - _OcclusionStrength: 1
    - _SurfaceClass: 4
    - _Cull: 2
''', text, flags=re.S)
    text = re.sub(r'    m_Colors:.*?(?=  m_BuildTextureStacks:)', '''    m_Colors:
    - _TileMeters: {r: 1.85, g: 1.85, b: 0, a: 0}
    - _Color: {r: 1, g: 1, b: 1, a: 1}
''', text, flags=re.S)
    text = text.replace('m_EnableInstancingVariants: 0', 'm_EnableInstancingVariants: 1')
    p.write_text('\n'.join(line.rstrip() for line in text.splitlines()) + '\n')


def pack_ao_roughness(ao, roughness):
    ao = np.asarray(ao)
    roughness = np.asarray(roughness)
    ao = ao[:, :, 0] if ao.ndim == 3 else ao
    roughness = roughness[:, :, 0] if roughness.ndim == 3 else roughness
    if ao.shape != roughness.shape or ao.ndim != 2:
        raise ValueError('AO and roughness must have matching pixel dimensions')
    packed = np.zeros((*ao.shape, 3), dtype=np.uint8)
    packed[:, :, 0] = ao
    packed[:, :, 1] = roughness
    return packed


def decode_source_maps(contents):
    """Decode every expected original JPEG before allowing asset writes."""
    import io
    arrays = {}
    for key, name in MAP_NAMES.items():
        with Image.open(io.BytesIO(contents[key])) as im:
            if im.format != 'JPEG' or im.size != (2048, 2048):
                raise ValueError('Expected original 2048 x 2048 JPEG map: ' + name + ' ' + str((im.format, im.size)))
            arrays[key] = np.array(im.convert('RGB'))
    return arrays


def read_archive(archive):
    # Fully decode first. A partial/HTML/incorrect ZIP must not alter the scene.
    contents = {}
    with zipfile.ZipFile(archive) as z:
        for key, name in MAP_NAMES.items():
            matches = [n for n in z.namelist() if Path(n).name == name]
            if len(matches) != 1:
                raise ValueError('Original requested map missing or ambiguous: ' + name)
            contents[key] = z.read(matches[0])
    return contents, decode_source_maps(contents)


def read_maps_dir(directory):
    """Read only the four exact filenames from an extracted original source set."""
    directory = Path(directory)
    if not directory.is_dir():
        raise NotADirectoryError('Original map directory missing: ' + str(directory))
    contents = {key: (directory / name).read_bytes() for key, name in MAP_NAMES.items()}
    return contents, decode_source_maps(contents)


def install(archive=None, maps_dir=None):
    if (archive is None) == (maps_dir is None):
        raise ValueError('Provide exactly one original ZIP archive or original maps directory')
    contents, arrays = read_archive(archive) if archive is not None else read_maps_dir(maps_dir)
    materials = active_asphalt_materials()
    assert len(materials) >= 7, 'Expected all existing road/drive/ramp/edge materials'
    folders(FOLDER)
    paths = {}
    for key, data in contents.items():
        p = FOLDER / MAP_NAMES[key]
        p.write_bytes(data)
        texture_meta(p, normal=key == 'normalGL', scalar=key in ('roughness', 'ambientOcclusion'),
                     runtime_limit=1024 if key == 'normalGL' else 2048)
        paths[key] = p
    packed = pack_ao_roughness(arrays['ambientOcclusion'], arrays['roughness'])
    p = FOLDER / 'AO_Roughness.png'
    Image.fromarray(packed).save(p)
    texture_meta(p, scalar=True, runtime_limit=1024)
    paths['packedMask'] = p
    shader = ROOT / 'Assets/Shaders/StreetAsphalt020S.shader'
    shader.write_text(SHADER)
    meta(shader, importer='ShaderImporter')
    manifest = dict(asset=ASSET_ID, source=SOURCE_URL, download=DOWNLOAD_URL, resolvedDownload=CDN_URL,
                    license='CC0-1.0', licenseUrl='https://docs.ambientcg.com/license/',
                    legalCode='https://creativecommons.org/publicdomain/zero/1.0/',
                    physicalTileMeters=[1.85, 1.85], sourceResolution=[2048, 2048],
                    runtimeImportLimits=dict(color=2048, normalGL=1024, packedMask=1024),
                    runtimeLevelZeroTexels=2048*2048 + 2*1024*1024,
                    sourceDelivery='user-provided ZIP of original maps' if archive is not None else 'user-provided original maps',
                    archiveFileName=archive.name if archive is not None else None,
                    archiveSHA256=hashlib.sha256(archive.read_bytes()).hexdigest() if archive is not None else None,
                    archiveHashMeaning='Checksum of the supplied input ZIP, which may be a four-map repack; not a claimed checksum of the complete official package.',
                    originalFileHashes={MAP_NAMES[key]: hashlib.sha256(data).hexdigest() for key,data in contents.items()},
                    files={k:dict(file=p.name, sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for k,p in paths.items()},
                    packing='AO_Roughness.png R=original AO red channel, G=original roughness red channel, B=0; no resampling/repainting.',
                    materialPaths=[str(p.relative_to(ROOT)) for p in materials])
    p = FOLDER / 'SOURCE.json'
    p.write_text(json.dumps(manifest, indent=2) + '\n');meta(p, importer='TextScriptImporter', fileid=4900000)
    p = FOLDER / 'SOURCE-LICENSE.md'
    p.write_text('''# ambientCG Asphalt 020 S

Source: https://ambientcg.com/view?id=Asphalt020S
Official archive: https://ambientcg.com/get?file=Asphalt020S_2K-JPG.zip
License: Creative Commons CC0 1.0 Universal (CC0-1.0).
Official license statement: https://docs.ambientcg.com/license/
Legal code: https://creativecommons.org/publicdomain/zero/1.0/

Created using Asphalt 020 S from ambientCG.com, licensed under the Creative
Commons CC0 1.0 Universal License. The source permits copying, modifying and
redistributing the raw maps, including commercial game projects.

The four original 2K JPEG maps are preserved byte-for-byte. NormalGL uses the
OpenGL/+Y convention (Unity normal importer, no green-channel flip). The shared
runtime mask file stores source AO in red and source roughness in green, without
resampling its pixels. The original JPEG files remain 2048 x 2048. Unity imports
color at up to 2048, and normal plus packed mask at up to 1024 to keep the runtime
texture budget below the former asphalt set. It uses three texture reads (color, normal, packed AO/roughness),
physical repeat 1.85 × 1.85 m, mipmaps, repeat wrap and instancing. Source AO and
roughness remain beside it. Unreferenced originals are not runtime dependencies.
SOURCE.json records the original file SHA-256 checksums and delivery method.
When a ZIP was supplied, its name and checksum identify that input container,
which may be a user-created four-map repack rather than the full official ZIP.
With separately supplied original maps archiveSHA256 is null. No other texture was selected or
synthesized for this replacement.
''')
    meta(p, importer='TextScriptImporter', fileid=4900000)
    for p in materials:
        apply_material(p, paths, shader)
    # Native metadata templates use empty values with trailing spaces. Keep new
    # source assets reproducible and clean without rewriting unrelated metadata.
    for p in [FOLDER, FOLDER.parent, shader, FOLDER/'SOURCE.json', FOLDER/'SOURCE-LICENSE.md']:
        mp=Path(str(p)+'.meta')
        if mp.exists():
            mp.write_text('\n'.join(line.rstrip() for line in mp.read_text().splitlines())+'\n')
    print(json.dumps(manifest, indent=2))


def verify():
    """Read-only installation verification; never writes to Unity or the scene."""
    manifest_path = FOLDER / 'SOURCE.json'
    if not manifest_path.is_file():
        raise FileNotFoundError('Asphalt020S has not been installed: ' + str(manifest_path))
    manifest = json.loads(manifest_path.read_text())
    assert manifest['asset'] == ASSET_ID and manifest['license'] == 'CC0-1.0'
    assert manifest['source'] == SOURCE_URL and manifest['download'] == DOWNLOAD_URL
    assert manifest['physicalTileMeters'] == [1.85, 1.85]
    arrays = {}
    for key, record in manifest['files'].items():
        p = FOLDER / record['file']
        assert hashlib.sha256(p.read_bytes()).hexdigest() == record['sha256'], ('Source checksum mismatch', p)
        with Image.open(p) as im:
            assert im.size == (2048, 2048), (p, im.size)
            arrays[key] = np.array(im.convert('RGB'))
        text = Path(str(p) + '.meta').read_text()
        expected_limit = 1024 if key in ('normalGL', 'packedMask') else 2048
        assert 'maxTextureSize: ' + str(expected_limit) in text and 'enableMipMap: 1' in text, p
        scalar = key in ('ambientOcclusion', 'roughness', 'packedMask')
        assert 'sRGBTexture: ' + str(int(not scalar and key != 'normalGL')) in text, p
        assert 'textureType: ' + ('1' if key == 'normalGL' else '0') in text, p
    for key in MAP_NAMES:
        assert manifest['files'][key]['file'] == MAP_NAMES[key]
        if 'originalFileHashes' in manifest:
            assert manifest['originalFileHashes'][MAP_NAMES[key]] == manifest['files'][key]['sha256'], \
                ('Installed map differs from the supplied original bytes', key)
    expected = pack_ao_roughness(arrays['ambientOcclusion'], arrays['roughness'])
    assert np.array_equal(arrays['packedMask'], expected), 'Packed AO/roughness differs from original maps'
    shader = ROOT / 'Assets/Shaders/StreetAsphalt020S.shader'
    assert shader.read_text() == SHADER, 'Shader differs from reproducible installer source'
    assert SHADER.count('tex2D(') == 3, 'Runtime shader sampler budget changed'
    materials = active_asphalt_materials()
    for p in materials:
        text = p.read_text()
        assert f'guid: {guid(shader)}' in field(text, 'm_Shader'), p
        assert field(text, '- _SurfaceClass') == '4', p
        assert field(text, 'm_EnableInstancingVariants') == '1', p
        for key in ('color', 'normalGL', 'packedMask'):
            assert guid(FOLDER / manifest['files'][key]['file']) in text, (p, key)
    out = dict(asset=ASSET_ID, originalMapsVerified=4, packedPixelCount=2048*2048,
               runtimeTextureReads=3, activeAsphaltMaterialsVerified=len(materials),
               runtimeImportLimits=dict(color=2048, normalGL=1024, packedMask=1024),
               runtimeLevelZeroTexels=2048*2048 + 2*1024*1024,
               unityImportAndGPUFrameTimes='NOT RUN by this saved-data verifier')
    print(json.dumps(out, indent=2))
    return out


def self_test():
    """Exercise channel order and lossless packing with clearly synthetic data."""
    folder = ROOT.parent / 'work/asphalt020s-packing-test'
    folder.mkdir(parents=True, exist_ok=True)
    ao = np.array([[[0, 99, 66], [17, 88, 55], [255, 77, 44]],
                   [[63, 11, 22], [128, 33, 44], [201, 55, 66]]], dtype=np.uint8)
    rough = np.array([[[255, 1, 2], [200, 3, 4], [42, 5, 6]],
                      [[0, 7, 8], [127, 9, 10], [14, 11, 12]]], dtype=np.uint8)
    expected = np.array([[[0, 255, 0], [17, 200, 0], [255, 42, 0]],
                         [[63, 0, 0], [128, 127, 0], [201, 14, 0]]], dtype=np.uint8)
    packed = pack_ao_roughness(ao, rough)
    assert np.array_equal(packed, expected), 'AO/roughness channel order or values changed'
    assert np.array_equal(pack_ao_roughness(ao[:, :, 0], rough[:, :, 0]), expected)
    path = folder / 'synthetic-packing-test.png'
    Image.fromarray(packed).save(path)
    with Image.open(path) as im:
        assert np.array_equal(np.array(im), expected), 'PNG storage changed channel values'
    try:
        pack_ao_roughness(ao, rough[:1])
    except ValueError:
        pass
    else:
        raise AssertionError('Mismatched map dimensions were not rejected')
    print(json.dumps(dict(syntheticDataOnly=True, channelOrder='R=AO, G=roughness, B=0',
                          exactPNGPixelRoundTrip=True, mismatchedDimensionsRejected=True), indent=2))
    source_dir = folder / 'SYNTHETIC_PREFLIGHT_ONLY_DO_NOT_INSTALL'
    source_dir.mkdir(exist_ok=True)
    (source_dir / 'DO_NOT_INSTALL_SYNTHETIC_TEST_DATA.txt').write_text(
        'Synthetic parser test fixtures, not ambientCG source maps. Never install these into Unity.\n')
    for i, name in enumerate(MAP_NAMES.values()):
        Image.new('RGB', (2048, 2048), (37+i, 71+i, 103+i)).save(source_dir / name, format='JPEG')
    contents, decoded = read_maps_dir(source_dir)
    assert set(contents) == set(decoded) == set(MAP_NAMES)
    assert all(contents[key] == (source_dir / name).read_bytes() for key,name in MAP_NAMES.items())
    # The unchanged ZIP mode must decode the same four source bytes.
    archive_path = folder / 'SYNTHETIC_PREFLIGHT_ONLY.zip'
    with zipfile.ZipFile(archive_path, 'w') as z:
        for key, data in contents.items():
            z.writestr(MAP_NAMES[key], data)
    zip_contents, zip_arrays = read_archive(archive_path)
    assert zip_contents == contents
    assert all(np.array_equal(zip_arrays[key], decoded[key]) for key in MAP_NAMES)
    key = 'roughness'
    target = source_dir / MAP_NAMES[key]
    original = target.read_bytes()
    for bad_data, error_type in [(None, FileNotFoundError), (b'not a JPEG', OSError)]:
        if bad_data is None:
            target.unlink()
        else:
            target.write_bytes(bad_data)
        try:
            read_maps_dir(source_dir)
        except error_type:
            pass
        else:
            raise AssertionError('Invalid original map was not rejected: ' + str(error_type))
        target.write_bytes(original)
    Image.new('RGB', (16, 16), 'red').save(target, format='JPEG')
    try:
        read_maps_dir(source_dir)
    except ValueError:
        pass
    else:
        raise AssertionError('Incorrect original map resolution was not rejected')
    target.write_bytes(original)
    print(json.dumps(dict(syntheticDataOnly=True, mapsDirectoryPreflight=True, zipModePreservesSameBytes=True,
                          missingCorruptAndWrongSizeMapsRejected=True, unityAssetsWritten=False), indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument('--archive', type=Path, help='Install the original official 2K-JPG ZIP')
    modes.add_argument('--maps-dir', type=Path, help='Install the four unchanged original 2K JPEG maps by their exact filenames')
    modes.add_argument('--verify', action='store_true', help='Verify installed source maps/materials without writing assets')
    modes.add_argument('--verify-archive', type=Path, help='Decode/check an archive without installing it')
    modes.add_argument('--self-test', action='store_true', help='Test packing on synthetic pixels, only inside work/')
    args = parser.parse_args()
    if args.self_test:
        self_test()
    elif args.verify:
        verify()
    elif args.verify_archive:
        contents, arrays = read_archive(args.verify_archive)
        print(json.dumps(dict(archiveValid=True, originalMapCount=len(contents), sourceResolution=[2048,2048]), indent=2))
    else:
        install(archive=args.archive, maps_dir=args.maps_dir)
        verify()
