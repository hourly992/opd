"""Reproducible Blender source for the photo-based Dunin-Gorkavich monument.

Run with Blender --background --python build_dunin_monument.py -- OUTPUT_DIRECTORY.
The model is in metres. Local -Y faces the approach and +Z is up.
"""
import argparse
import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


parser = argparse.ArgumentParser()
parser.add_argument("out", type=Path)
parser.add_argument("--preview", action="store_true")
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
args.out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
rng = random.Random(216)
scene = bpy.context.scene
model = bpy.data.collections.new("Памятник А. А. Дунину-Горкавичу")
scene.collection.children.link(model)


def collection(name):
    group = bpy.data.collections.new(name)
    model.children.link(group)
    return group


stone_group = collection("01 · Гранитные стелы и основание")
figure_group = collection("02 · Бронзовая фигура")
relief_group = collection("03 · Литьё, рельефы и надпись")
site_group = collection("04 · Мощёный подход")


def material(name, color, metal=0., rough=.5):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.)
    mat.use_nodes = True
    b = mat.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = mat.diffuse_color
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    return mat


granite = material("RoseGranite", (.39, .195, .165), rough=.39)
bronze = material("CastBronze", (.099, .075, .051), .72, .46)
edge = material("BurnishedBronze", (.18, .142, .092), .68, .42)
patina = material("BronzePatina", (.054, .051, .035), .64, .60)
lettering = material("RaisedLettering", (.52, .35, .15), .54, .43)
join = material("StoneJoint", (.14, .090, .072), rough=.78)
grout = material("PavingGrout", (.19, .175, .156), rough=.85)
pavers = [material("MemorialPaver_" + str(i), c, rough=.76) for i, c in enumerate(
    ((.40, .206, .155), (.30, .160, .127), (.35, .319, .284), (.27, .257, .228)))]

# The blend previews the same generated granite albedo used by Unity.
granite_image = args.out / "Granite_Albedo.png"
if granite_image.is_file():
    nodes = granite.node_tree.nodes
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(str(granite_image))
    tex.image.pack()
    granite.node_tree.links.new(tex.outputs["Color"], nodes.get("Principled BSDF").inputs["Base Color"])


def move(obj, group):
    for current in list(obj.users_collection):
        current.objects.unlink(obj)
    group.objects.link(obj)
    return obj


def mesh(name, verts, faces, mat, group, smooth=False):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    data.materials.append(mat)
    for face in data.polygons:
        face.use_smooth = smooth
    obj = bpy.data.objects.new(name, data)
    group.objects.link(obj)
    return obj


def active(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def bevel(obj, width, segments=2):
    mod = obj.modifiers.new("Кромка", "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.harden_normals = True
    active(obj)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def box(name, center, dims, mat, group, chamfer=0.):
    half = [d / 2 for d in dims]
    verts = [(center[0]+x*half[0], center[1]+y*half[1], center[2]+z*half[2])
             for x, y, z in ((-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                             (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1))]
    faces = ((3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7))
    obj = mesh(name, verts, faces, mat, group)
    return bevel(obj, chamfer) if chamfer else obj


def plate(name, outline, front, back, mat, group, chamfer):
    verts = [(x, y, z) for y in (front, back) for x, z in outline]
    count = len(outline)
    faces = [tuple(range(count)), tuple(range(2*count-1, count-1, -1))]
    for i in range(count):
        j = (i + 1) % count
        faces.append((i, j, j+count, i+count))
    return bevel(mesh(name, verts, faces, mat, group), chamfer)


def oval(name, point, size, mat, group=figure_group, segments=20):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=12, location=point)
    obj = bpy.context.object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return move(obj, group)


def pipe(name, centers, size, mat, group=figure_group, sides=12):
    points = tuple(map(Vector, centers))
    verts, faces = [], []
    for i, c in enumerate(points):
        axis = (points[min(i+1, len(points)-1)] - points[max(i-1, 0)]).normalized()
        right = axis.cross(Vector((0., 1., 0.))).normalized()
        if right.length < .1:
            right = axis.cross(Vector((1., 0., 0.))).normalized()
        back = axis.cross(right).normalized()
        p, q = size[i] if isinstance(size[i], tuple) else (size[i], size[i])
        for j in range(sides):
            t = math.tau * j / sides
            verts.append(tuple(c + math.cos(t)*p*right + math.sin(t)*q*back))
    for i in range(len(points)-1):
        for j in range(sides):
            a = i * sides + j
            b = i * sides + (j+1) % sides
            faces.append((a, b, b+sides, a+sides))
    faces.extend((tuple(reversed(range(sides))), tuple(range(len(verts)-sides, len(verts)))))
    return mesh(name, verts, faces, mat, group, True)


def combine(objects, group, name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    if len(objects) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    active(obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return move(obj, group)


def map_stone(obj):
    # Continuous tile scale in metres across granite parts, with directional UVs on bevels.
    uv = obj.data.uv_layers.active or obj.data.uv_layers.new()
    for face in obj.data.polygons:
        axis = max(range(3), key=lambda i: abs(face.normal[i]))
        axes = [i for i in range(3) if i != axis]
        for i in face.loop_indices:
            point = obj.data.vertices[obj.data.loops[i].vertex_index].co
            uv.data[i].uv = [point[j] / .52 for j in axes]


# Footing and segmented pedestal. The photographic left slab has a rounded shoulder and a straight inside edge.
box("Базовая гранитная плита", (0, .055, .090), (.90, .69, .10), granite, stone_group, .018)
box("Нижний цоколь", (0, .06, .162), (.67, .56, .048), granite, stone_group, .010)
segments = ((.183, .390), (.393, .630), (.633, .88), (.883, 1.13), (1.133, 1.35))
for i, (low, high) in enumerate(segments):
    box("Розовый гранит · блок " + str(i+1), (.01,.067,(low+high)/2),
        (.548,.455,high-low), granite, stone_group, .004)
for z in (.391,.631,.881,1.131):
    box("Шов камня", (.01,.067,z),(.536,.444,.0038),join,stone_group)

left = [(-.718,.63),(-.718,2.97)]
for i in range(1, 17):
    a = math.pi - math.pi * .5 * i/16
    left.append((-.38 + .338*math.cos(a), 2.97 + .30*math.sin(a)))
left += [(-.198,3.26),(-.198,.66),(-.285,.64)]
plate("Высокая гранитная стела", left, .055, .299, granite, stone_group, .012)
plate("Короткая гранитная стела",
      [(.31,.73),(.32,1.80),(.48,1.91),(.627,1.95),(.613,.98),(.55,.84),(.44,.76)],
      .10,.288,granite,stone_group,.009)
box("Каменная связка",(-.018,.13,1.34),(.61,.41,.16),granite,stone_group,.012)

# Jacket shape, sleeves and the stiff service collar are shaped separately before the main casting is welded.
torso_rings = [(1.36,.184,.122,.017),(1.43,.211,.143,.011),(1.60,.186,.142,.008),
               (1.74,.180,.130,.006),(1.94,.204,.153,.008),(2.09,.238,.163,.018),
               (2.23,.251,.141,.028),(2.33,.219,.114,.035),(2.40,.092,.082,.047)]
verts, faces = [], []
for z, xw, depth, py in torso_rings:
    for n in range(48):
        a = math.tau*n/48
        x = math.sin(a)*xw + .014*(z-1.7)
        y = py - math.cos(a) * depth * (1 + .045 * math.cos(a*3))
        verts.append((x,y,z+.008*math.sin(a)))
for i in range(len(torso_rings)-1):
    for j in range(48):
        a, b = 48*i+j,48*i+(j+1)%48
        faces.append((a,b,b+48,a+48))
faces.extend((tuple(range(47,-1,-1)),tuple(range(len(verts)-48,len(verts)))))
torso = mesh("Мундир · основа", verts, faces, bronze, figure_group, True)
active(torso)
subd = torso.modifiers.new("Плавная форма мундира", "SUBSURF")
subd.levels = 2
bpy.ops.object.modifier_apply(modifier=subd.name)
sleeve_l = pipe("Правый рукав", [(-.181,.021,2.275),(-.242,.018,2.215),(-.278,.008,2.095),
        (-.301,-.005,1.92),(-.332,-.018,1.747),(-.350,-.063,1.60),(-.335,-.090,1.465)],
        [(.089,.088),(.095,.098),(.083,.088),(.071,.078),(.064,.068),(.059,.057),(.051,.054)],bronze,sides=28)
sleeve_r = pipe("Левый рукав",[(.195,.030,2.281),(.253,.043,2.216),(.280,.052,2.084),
        (.297,.048,1.927),(.302,.026,1.774),(.332,-.09,1.656),(.359,-.165,1.618)],
        [(.08,.09),(.086,.090),(.073,.080),(.065,.071),(.055,.065),(.052,.055),(.046,.051)],bronze,sides=28)
welded = combine([torso,sleeve_l,sleeve_r],figure_group,"Литая форма мундира")
active(welded)
welded.data.remesh_voxel_size = .005
bpy.ops.object.voxel_remesh()
for face in welded.data.polygons:
    face.use_smooth = True
smooth = welded.modifiers.new("Литой переход плеч и рукавов", "SMOOTH")
smooth.factor, smooth.iterations = .51, 4
bpy.ops.object.modifier_apply(modifier=smooth.name)

pipe("Шея",[(.059,.026,2.32),(.061,.026,2.401),(.069,.015,2.482),(.071,-.004,2.54)],
     [(.072,.073),(.071,.070),(.059,.062),(.053,.053)],bronze,sides=32)
pipe("Воротник мундира",[(.044,.016,2.333),(.044,.015,2.389),(.059,.019,2.449)],
     [(.087,.087),(.085,.083),(.074,.076)],bronze,sides=34)

# An anatomical ring surface gives the cast portrait a continuous brow, nose, mouth and jaw.
head_z = [2.449,2.470,2.497,2.530,2.56,2.602,2.648,2.688,2.722,2.753,2.772,2.780]
head_w = [.021,.049,.068,.078,.088,.095,.097,.098,.084,.063,.034,.003]
head_front = [-.085,-.115,-.126,-.129,-.119,-.110,-.109,-.107,-.095,-.071,-.050,-.014]
head_back = [.007,.046,.067,.083,.096,.105,.107,.103,.085,.066,.033,-.008]
center_x = .060


def interpolate(xs, ys, x):
    for i in range(1,len(xs)):
        if x <= xs[i]:
            t = (x-xs[i-1])/(xs[i]-xs[i-1])
            return (1-t)*ys[i-1]+t*ys[i]
    return ys[-1]


def bump(x,z,cx,cz,sx,sz):
    return math.exp(-((x-cx)/sx)**2 - ((z-cz)/sz)**2)


def head_point(a,z):
    x = interpolate(head_z,head_w,z)*math.sin(a)
    f,b = interpolate(head_z,head_front,z),interpolate(head_z,head_back,z)
    y = (f+b)/2-(b-f)/2*math.cos(a)
    face = max(0.,math.cos(a))**1.2
    y -= face*(.048*bump(x,z,0.,2.578,.021,.022) +
               .025*bump(x,z,0.,2.606,.018,.038) +
               .010*bump(x,z,0.,2.528,.040,.012) +
               .009*bump(abs(x),z,.056,2.568,.024,.018) +
               .015*bump(abs(x),z,.038,2.623,.031,.012))
    y += face*(.016*bump(abs(x),z,.038,2.605,.026,.014) +
               .008*bump(x,z,0.,2.521,.047,.005))
    yaw = math.radians(7)
    return (center_x+math.cos(yaw)*x-math.sin(yaw)*y,
            math.sin(yaw)*x+math.cos(yaw)*y-.015,z)


def on_face(x,z,raise_by=0.):
    width = interpolate(head_z,head_w,z)
    u = math.asin(max(-1.,min(1.,x/width)))
    a,b,c = head_point(u,z)
    return a,b-raise_by,c


vertices, quads = [], []
for i in range(75):
    z = head_z[0]+(head_z[-1]-head_z[0])*i/74
    vertices += [head_point(math.tau*j/80,z) for j in range(80)]
for i in range(74):
    for j in range(80):
        a,b=i*80+j,i*80+(j+1)%80
        quads.append((a,b,b+80,a+80))
quads.extend((tuple(range(79,-1,-1)),tuple(range(len(vertices)-80,len(vertices)))))
portrait = mesh("Портрет · лоб, нос, скулы и челюсть",vertices,quads,bronze,figure_group,True)
active(portrait)
smoothing = portrait.modifiers.new("Смягчение линий литья", "SMOOTH")
smoothing.factor, smoothing.iterations = .43, 3
bpy.ops.object.modifier_apply(modifier=smoothing.name)

for sign in (-1, 1):
    x = center_x+sign*.092
    oval("Ухо", (x,-.014,2.591),(.022,.025,.037),bronze)
    pipe("Завиток ушной раковины",[(x,-.034,2.570),(x+sign*.013,-.032,2.590),
          (x+sign*.010,-.031,2.615),(x,-.034,2.621)],[.004,.005,.004,.003],edge,sides=8)
    oval("Скульптурное веко",on_face(sign*.037,2.604,.001),(.021,.008,.008),bronze)
    pipe("Линия брови",[on_face(sign*a,z,.002) for a,z in
         ((.019,2.620),(.033,2.626),(.054,2.623),(.069,2.615))],[.003,.0046,.0035,.001],patina,sides=8)
    pipe("Усы",[on_face(sign*a,z,.004) for a,z in
         ((.003,2.550),(.018,2.546),(.033,2.539),(.046,2.534))],[.006,.008,.005,.0015],patina,sides=10)
pipe("Линия губ",[on_face(x,z,.001) for x,z in
     ((-.032,2.519),(-.016,2.523),(0.,2.520),(.016,2.521),(.030,2.516))],
     [.0012,.0023,.002,.0021,.0012],patina,sides=7)
for side in (-1, 1):
    for index in range(13):
        a = side*(1.27+index*.128)
        baseline = 2.624 + .018*math.cos(index*.16)
        centers = [Vector(head_point(a+side*.18*t,baseline*(1-t)+2.758*t))
                   for t in (.08,.29,.47,.65,.85,.94)]
        pipe("Рельеф волос у висков",centers,[.0007,.0019,.0021,.0017,.0012,.0005],bronze,sides=6)

plate("Лацкан слева",[(-.111,2.307),(-.076,2.394),(-.039,2.392),(.091,2.229),(.025,2.223)],
      -.097,-.065,bronze,figure_group,.010)
plate("Лацкан справа",[(.035,2.273),(.113,2.397),(.144,2.351),(.131,2.280),(.074,2.246)],
      -.116,-.077,bronze,figure_group,.010)
pipe("Борт мундира",[(.082,-.104,2.28),(.031,-.152,2.193),(.026,-.160,2.03),
     (.022,-.146,1.871),(.030,-.148,1.714),(.034,-.137,1.532)],
     [.004,.005,.004,.0045,.003,.003],edge,sides=8)
for i in range(7):
    z=2.29-i*.093
    y=-.169 if i in (2,3) else -.149
    oval("Пуговица мундира",( .083 if i<2 else .058,y,z),(.013,.006,.013),edge,segments=16)
for sign in (-1,1):
    stripe=box("Погон", (sign*.165,.009,2.339),(.145,.058,.022),edge,figure_group,.008)
    pipe("Шов кармана",[(sign*.119,-.149,2.035),(sign*.170,-.129,2.021),(sign*.199,-.106,2.016)],
         [.0036,.0036,.0021],patina,sides=8)
    pipe("Складка мундира",[(sign*.142,-.124,1.467),(sign*.120,-.145,1.557),
         (sign*.136,-.143,1.645),(sign*.169,-.109,1.716)],[.001,.008,.005,.001],bronze,sides=9)
pipe("Кромка ремня",[(0,.021,1.689),(0,.021,1.729),(0,.022,1.770)],
     [(.181,.137),(.191,.149),(.181,.138)],patina,sides=64)
box("Пряжка",(.034,-.136,1.734),(.064,.020,.072),edge,figure_group,.007)
box("Поле пряжки",(.034,-.149,1.735),(.042,.008,.051),patina,figure_group,.005)
box("Язычок пряжки",(.033,-.155,1.735),(.008,.005,.043),edge,figure_group,.002)

for name, points in (("Манжета правого рукава",[(-.346,-.075,1.58),(-.341,-.088,1.528),(-.336,-.093,1.485)]),
                     ("Манжета левого рукава",[(.316,-.076,1.68),(.341,-.126,1.639),(.360,-.161,1.621)])):
    pipe(name,points,[.058,.062,.054],bronze,sides=24)

# Hands, right-side hanging scroll, and the bronze inscription panel.
oval("Правая кисть",(-.34,-.122,1.411),(.044,.043,.079),bronze)
for i in range(4):
    x, z = -.374+.017*i, 1.366+(.012 if i in (0,3) else 0)
    pipe("Пальцы правой руки",[(x,-.143,z+.046),(x+.005,-.156,z),(x+.008,-.147,z-.019)],
         [.014,.012,.009],bronze,sides=9)
pipe("Большой палец правой руки",[(-.309,-.109,1.447),(-.300,-.155,1.419),(-.320,-.171,1.394)],
     [.017,.015,.012],bronze,sides=9)
oval("Левая кисть",(.388,-.168,1.617),(.062,.035,.045),bronze)
for i in range(4):
    x = .365 + .018*i
    pipe("Пальцы левой руки",[(x,-.164,1.642),(x+.014,-.196,1.619),(x+.006,-.193,1.597)],
         [.013,.012,.009],bronze,sides=8)
pipe("Свиток у правой руки",[(-.433,.010,1.716),(-.399,-.073,1.596),(-.358,-.122,1.457),
     (-.323,-.153,1.333),(-.246,-.202,1.222)],[.034,.037,.038,.040,.034],bronze,relief_group,24)
pipe("Валик свитка",[(-.322,-.153,1.334),(-.270,-.183,1.255),(-.242,-.206,1.217)],
     [.040,.039,.033],edge,relief_group,20)
plate("Бронзовая плита с надписью",[(-.31,1.135),(.289,1.135),(.339,1.540),(.281,1.577),
          (.179,1.566),(-.052,1.486),(-.23,1.398),(-.303,1.330)],
      -.172,-.066,patina,relief_group,.012)
font_path = Path("C:/Windows/Fonts/timesbd.ttf")
if not font_path.is_file():
    raise FileNotFoundError(font_path)
font = bpy.data.fonts.load(str(font_path))
inscription = ("ИССЛЕДОВАТЕЛЬ", "СЕВЕРА", "АЛЕКСАНДР", "АЛЕКСАНДРОВИЧ", "ДУНИН-ГОРКАВИЧ")
for i, line in enumerate(inscription):
    curve = bpy.data.curves.new(line,"FONT")
    curve.font,curve.body,curve.align_x = font,line,"CENTER"
    curve.size,curve.extrude,curve.bevel_depth,curve.resolution_u = .031,.00085,.00042,4
    curve.materials.append(lettering)
    label = bpy.data.objects.new(line,curve)
    relief_group.objects.link(label)
    label.location = (.024,-.174,1.386-.045*i)
    label.rotation_euler.x = math.pi/2
    active(label)
    bpy.ops.object.convert(target="MESH")


def leaf_spray(x,z,scale,side):
    y=.030
    stem=[(x+side*.023*t,y-.033*math.sin(math.pi*t),z+scale*t) for t in (0,.23,.47,.70,.91,1)]
    pipe("Центральная жилка растительного рельефа",stem,
         [scale*r for r in (.048,.038,.034,.027,.020,.005)],bronze,relief_group,9)
    for i in range(9):
        t=.12+.091*i
        z0=z+scale*t
        taper=(1-t)**.7
        for d in (-1,1):
            x0=x+side*.023*t
            dx=d*scale*(.11+.15*taper)
            points=[(x0,y-.015,z0),(x0+.41*dx,y-.035,z0+scale*.048),
                    (x0+.75*dx,y-.024,z0+scale*.094),(x0+dx,y+.012,z0+scale*.17)]
            pipe("Лепесток бронзового рельефа",points,
                 [(.012,.008),(.017*taper+.002,.017),(.011*taper+.002,.013),(.002,.003)],
                 bronze if (i+(d>0))%3 else edge,relief_group,9)


plate("Основа высокого рельефа",
    [(-.534,.93),(-.502,2.04),(-.533,2.48),(-.488,2.94),(-.344,3.09),
     (-.194,3.13),(-.155,2.91),(-.173,2.59),(-.190,2.22),(-.285,1.83),(-.356,1.15)],
    .013,.081,patina,relief_group,.012)
plate("Основа правого рельефа",
    [(.297,.78),(.320,1.236),(.383,1.580),(.495,1.842),(.588,1.902),
     (.644,1.753),(.616,1.410),(.584,1.024),(.477,.832)],
    .013,.107,patina,relief_group,.012)
for x,z,scale,side in ((-.39,1.14,.59,1),(-.36,1.62,.60,-1),(-.35,2.15,.65,1),
                      (-.322,2.62,.48,-1),(.482,.83,.59,1),(.499,1.32,.55,-1)):
    leaf_spray(x,z,scale,side)

# The rectangular approach uses a small common set of coloured, slightly chamfered bricks.
box("Ложе мощения", (0,-1.10,.002),(2.42,3.46,.071),grout,site_group,.009)
for side in (-1,1):
    for i in range(10):
        box("Бордюр у цветника",(1.215*side,-2.675+i*.344,.022),(.081,.34,.116),
            grout,site_group,.006)
for j in range(27):
    for i in range(14):
        end = 2.34/14
        x = -1.17 + i*end
        x += end*.50 if j%2 else 0
        width = min(end-.006,1.167-x)
        if width < .02:
            continue
        mat = rng.choices(pavers, weights=(41,24,23,12),k=1)[0]
        box("Плитка", (x+width*.5,-2.748+j*.126,.037+rng.uniform(-.0015,.0015)),
            (width,.120,.027),mat,site_group,.003)
    if j%2:
        box("Крайний подрез",(-1.130,-2.748+j*.126,.037),(.076,.12,.027),
            pavers[j%4],site_group,.003)

for obj in stone_group.objects:
    map_stone(obj)

names = {stone_group:"Granite",figure_group:"Portrait",relief_group:"Reliefs",site_group:"Approach"}
bpy.ops.wm.save_as_mainfile(filepath=str(args.out / "Dunin-Gorkavich.blend"),compress=True)


def decimate(obj, ratio):
    active(obj)
    modifier = obj.modifiers.new("Игровой LOD", "DECIMATE")
    modifier.ratio = ratio
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)


decimate(welded,.14)
export_meshes = []
low_meshes = []
for group, title in names.items():
    obj = combine([o for o in group.objects if o.type == "MESH"],group,title)
    export_meshes.append(obj)
    if title == "Approach":
        decimate(obj,.58)
    low = obj.copy()
    low.data = obj.data.copy()
    low.name = title+"Low"
    group.objects.link(low)
    decimate(low,{"Granite":.66,"Portrait":.29,"Reliefs":.18,"Approach":.16}[title])
    low_meshes.append(low)
bpy.ops.object.select_all(action="DESELECT")
for obj in export_meshes + low_meshes:
    obj.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(args.out/"Dunin-Gorkavich.fbx"),use_selection=True,
    object_types={"MESH"},axis_forward="-Z",axis_up="Y",add_leaf_bones=False,path_mode="STRIP",
    use_triangles=True)
triangles = sum(sum(len(poly.vertices)-2 for poly in obj.data.polygons) for obj in export_meshes)
low_triangles = sum(sum(len(poly.vertices)-2 for poly in obj.data.polygons) for obj in low_meshes)
print(f"MONUMENT_MODEL_READY meshes={len(export_meshes)} triangles={triangles} lod1={low_triangles}",flush=True)
for obj in low_meshes:
    bpy.data.objects.remove(obj,do_unlink=True)

if args.preview:
    # Isolated studio render to inspect the cast face and plaque before bringing them into Unity.
    ground_mat=material("Review ground",(.19,.175,.145),rough=.82)
    box("Review ground",(0,0,-.12),(200,200,.1),ground_mat,scene.collection)
    world=bpy.data.worlds.new("Soft exterior")
    world.use_nodes=True
    world.node_tree.nodes["Background"].inputs[0].default_value=(.49,.60,.70,1)
    world.node_tree.nodes["Background"].inputs[1].default_value=.45
    scene.world=world
    bpy.ops.object.light_add(type="AREA",location=(-3,-4,6))
    sun=bpy.context.object
    sun.data.energy,sun.data.shape,sun.data.size=630,"DISK",4
    sun.rotation_euler=(Vector((0,0,1.7))-sun.location).to_track_quat("-Z","Y").to_euler()
    bpy.ops.object.camera_add(location=(1.65,-6.5,2.58))
    cam=bpy.context.object
    cam.rotation_euler=(Vector((0,-.24,1.72))-cam.location).to_track_quat("-Z","Y").to_euler()
    cam.data.lens=49
    scene.camera=cam
    scene.render.engine="CYCLES"
    scene.cycles.samples=32
    scene.cycles.use_denoising=True
    scene.render.resolution_x,scene.render.resolution_y=810,1000
    scene.render.resolution_percentage=100
    scene.render.filepath=str(args.out/"model-preview.png")
    bpy.ops.render.render(write_still=True)
