"""Refine a copy of the existing robot; never clear the user's Blender scene.

Rebuild in a fresh Blender process with --background --python this file.
The Unity placement manifest is recorded by the accompanying art validation.
"""
import bpy
import bmesh
import json
import math
import random
from pathlib import Path
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
REPORT = Path.home() / 'Documents/Codex/BORis/implementation/2026-10-25-robot-chassis'
PLACEMENT = ROOT / 'ArtSource/Workbench/robot_placement.json'
SOURCE = ROOT / 'ArtSource/Workbench/WorkbenchRobot.blend'
DESTINATION = ROOT / 'ArtSource/Workbench/RobotRefinedFinal.blend'
EXPORT = ROOT / 'Assets/_Project/Art/Models/WorkbenchRobot.fbx'
C = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
NAMES = ('RobotHeadModel', 'RobotBodyModel', 'RobotCoverModel', 'DriveBodyModel', 'ArmModel')
STATE = {}
PARTS = []
MATERIALS = {}


def u(p):
    return (-p[0], -p[2], p[1])


def palette():
    settings = {
        'BR_IvoryPaint': ((.69, .66, .52, 1), .46, .18),
        'BR_TealPaint': ((.13, .31, .29, 1), .49, .30),
        'BR_OrangePaint': ((.76, .245, .055, 1), .48, .22),
        'BR_Aluminum': ((.43, .48, .47, 1), .28, .86),
        'BR_DarkSteel': ((.065, .083, .087, 1), .36, .76),
        'BR_Rubber': ((.019, .027, .029, 1), .82, .0),
        'BR_ScreenGlass': ((.012, .023, .026, 1), .16, .18),
        'BR_CyanEyes': ((.075, .67, .71, 1), .24, .12),
        'BR_EdgeWear': ((.32, .34, .30, 1), .62, .67),
    }
    for name, (color, roughness, metallic) in settings.items():
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = color
        mat.use_nodes = True
        node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        node.inputs['Base Color'].default_value = color
        node.inputs['Roughness'].default_value = roughness
        node.inputs['Metallic'].default_value = metallic
        if name == 'BR_CyanEyes':
            node.inputs['Emission Color'].default_value = color
            node.inputs['Emission Strength'].default_value = 2.0
        if name == 'BR_ScreenGlass':
            node.inputs['Coat Weight'].default_value = .35
            node.inputs['Coat Roughness'].default_value = .12
        MATERIALS[name] = mat


def prepare():
    if STATE:
        raise RuntimeError('This refinement has already been prepared.')
    original_scene = bpy.context.scene
    STATE['original_scene'] = original_scene
    STATE['original_objects'] = {o.name: tuple(tuple(row) for row in o.matrix_world) for o in original_scene.objects}
    scene = bpy.data.scenes.new('BORis Robot Refinement')
    bpy.context.window.scene = scene
    scene.unit_settings.system = original_scene.unit_settings.system
    scene.unit_settings.scale_length = 1
    scene.render.engine = original_scene.render.engine
    scene.world = bpy.data.worlds.new('Robot Neutral Studio')
    scene.world.use_nodes = True
    bg = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = (.12, .145, .16, 1)
    bg.inputs[1].default_value = .7
    working = bpy.data.collections.new('Robot Refined — Functional Parts')
    backup = bpy.data.collections.new('Robot Original Backup — Do Not Export')
    preview = bpy.data.collections.new('Unity Hardware Preview — Do Not Export')
    for collection in (working, backup, preview):
        scene.collection.children.link(collection)
    manifest = json.loads(PLACEMENT.read_text(encoding='utf-8-sig'))
    placements = {r['parent']: Matrix(r['matrix']) for r in manifest if r['parent'] in NAMES}
    with bpy.data.libraries.load(str(SOURCE), link=False) as (available, loaded):
        loaded.objects = [name for name in NAMES if name in available.objects]
    if len(loaded.objects) != 5:
        raise RuntimeError('The five existing robot meshes are required.')
    models = {}
    for obj in loaded.objects:
        name = obj.name
        backup.objects.link(obj)
        obj.name = name + '_Original'
        obj.matrix_world = C @ placements[name] @ C.inverted()
        bpy.context.view_layer.update()
        obj.hide_set(False)
        obj.hide_render = False
        clone = obj.copy()
        clone.data = obj.data.copy()
        clone.name = name
        working.objects.link(clone)
        models[name] = clone
    STATE.update(scene=scene, working=working, backup=backup, preview=preview, models=models)
    palette()
    # Keep the original material data intact; only the duplicate gets the new PBR palette.
    remap = {'WB_Ivory': 'BR_IvoryPaint', 'WB_Teal': 'BR_TealPaint', 'WB_Orange': 'BR_OrangePaint',
             'WB_Steel': 'BR_Aluminum', 'WB_Dark': 'BR_DarkSteel'}
    for obj in models.values():
        for i, mat in enumerate(obj.data.materials):
            obj.data.materials[i] = MATERIALS[remap.get(mat.name, 'BR_DarkSteel')]
    backup.hide_render = True
    backup.hide_viewport = True
    STATE['baseline_vertices'] = sum(len(o.data.vertices) for o in backup.objects)
    print(json.dumps({'prepared': list(models), 'backup_vertices': STATE['baseline_vertices'], 'original_scene': original_scene.name}))


def finish(obj, name, mat, bevel=0):
    obj.name = name
    obj.data.materials.append(MATERIALS[mat])
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if bevel:
        modifier = obj.modifiers.new('Small machined radius', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        modifier = obj.modifiers.new('Weighted surface normals', 'WEIGHTED_NORMAL')
        modifier.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    for collection in list(obj.users_collection):
        collection.objects.unlink(obj)
    STATE['working'].objects.link(obj)
    PARTS.append(obj)
    return obj


def box(name, p, size, mat, bevel=.003):
    bpy.ops.mesh.primitive_cube_add(size=1, location=u(p))
    obj = bpy.context.object
    obj.dimensions = (size[0], size[2], size[1])
    return finish(obj, name, mat, bevel)


def cylinder(name, p, radius, depth, mat, axis='Z', vertices=24, ratio=1):
    # Local Unity axes, before the existing part's assembly transform is applied.
    points = []
    for end in (-.5, .5):
        for i in range(vertices):
            a = i * math.tau / vertices
            x, y = math.cos(a) * radius, math.sin(a) * radius * ratio
            q = (x, y, end * depth) if axis == 'Z' else (x, end * depth, y) if axis == 'Y' else (end * depth, x, y)
            points.append(u(tuple(p[k] + q[k] for k in range(3))))
    faces = [tuple(range(vertices - 1, -1, -1)), tuple(range(vertices, 2 * vertices))]
    faces += [(i, (i + 1) % vertices, (i + 1) % vertices + vertices, i + vertices) for i in range(vertices)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(points, [], faces)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    for polygon in mesh.polygons:
        polygon.use_smooth = len(polygon.vertices) == 4
    obj = bpy.data.objects.new(name, mesh)
    STATE['working'].objects.link(obj)
    return finish(obj, name, mat, min(.003, depth * .18, radius * .08))


def join_into(target):
    parts = list(PARTS)
    PARTS.clear()
    for obj in parts:
        obj.matrix_world = target.matrix_world.copy()
    bpy.ops.object.select_all(action='DESELECT')
    target.select_set(True)
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.join()
    target.data.name = target.name + 'Mesh'


def screen_frame():
    def loop(w, h, radius):
        points = []
        for x, y, start in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
            cx, cy = x * (w / 2 - radius), y * (h / 2 - radius)
            for i in range(6):
                a = math.radians(start + i * 18)
                points.append((cx + math.cos(a) * radius, cy + math.sin(a) * radius))
        return points
    outer, inner = loop(.88, .72, .07), loop(.80, .64, .055)
    n = len(outer)
    vertices = [u((x, y + .015, z)) for z in (-.545, -.485) for points in (outer, inner) for x, y in points]
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces += [(i, j, n + j, n + i), (2*n + i, 3*n + i, 3*n + j, 2*n + j),
                  (i, 2*n + i, 2*n + j, j), (n + i, n + j, 3*n + j, 3*n + i)]
    mesh = bpy.data.meshes.new('Rounded screen bezel with open centre')
    mesh.from_pydata(vertices, [], faces)
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new('Open metal screen frame', mesh)
    STATE['working'].objects.link(obj)
    return finish(obj, obj.name, 'BR_Aluminum', .0015)


def scuff(p, width, height, seed=0):
    # A few edge-adjacent paint chips, with no persistent grime in repairable areas.
    rng = random.Random(seed)
    x, y, z = p
    shape = [(-.5, -.2), (-.25, -.5), (.5, -.35), (.42, .28), (-.35, .5)]
    vertices = [u((x + a * width, y + b * height * rng.uniform(.7, 1), z)) for a, b in shape]
    mesh = bpy.data.meshes.new('Localized edge paint chip')
    mesh.from_pydata(vertices, [], [tuple(range(5))])
    obj = bpy.data.objects.new('Edge wear near fastener', mesh)
    STATE['working'].objects.link(obj)
    return finish(obj, obj.name, 'BR_EdgeWear')


def maker_mark(text, p, height):
    curve = bpy.data.curves.new('Engraved maker mark', 'FONT')
    curve.body = text
    curve.size = height
    curve.extrude = .0001
    obj = bpy.data.objects.new('BR-01 maker mark', curve)
    STATE['working'].objects.link(obj)
    obj.matrix_world = Matrix(((-1, 0, 0, -p[0]), (0, 0, 1, -p[2]), (0, 1, 0, p[1]), (0, 0, 0, 1)))
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target='MESH')
    return finish(bpy.context.object, 'Small maker identification', 'BR_DarkSteel')


def enhance_head():
    head = STATE['models']['RobotHeadModel']
    # The old ear caps and rear vents share a plane: give each layer a real seating depth.
    dark, pins, vents = set(), set(), set()
    for poly in head.data.polygons:
        mat = head.data.materials[poly.material_index].name
        points = [head.data.vertices[i].co for i in poly.vertices]
        if mat == 'BR_DarkSteel' and all(abs(p.x) > .44 for p in points): dark.update(poly.vertices)
        if mat == 'BR_Aluminum' and all(abs(p.x) > .48 for p in points): pins.update(poly.vertices)
        if mat == 'BR_DarkSteel' and all(-p.y > .49 for p in points): vents.update(poly.vertices)
    for i in dark: head.data.vertices[i].co.x -= math.copysign(.012, head.data.vertices[i].co.x)
    for i in pins: head.data.vertices[i].co.x += math.copysign(.007, head.data.vertices[i].co.x)
    for i in vents: head.data.vertices[i].co.y -= .0015
    screen_frame()
    box('Recessed glass gasket', (0, .015, -.498), (.83, .67, .024), 'BR_Rubber', .065)
    box('Inset dark screen glass', (0, .015, -.510), (.79, .63, .018), 'BR_ScreenGlass', .065)
    for side in (-1, 1):
        x = side * .21
        cylinder('Eye lens metal rim', (x, .045, -.526), .103, .018, 'BR_DarkSteel', ratio=.4/.28)
        cylinder('Soft cyan eye lens', (x, .045, -.543), .0875, .014, 'BR_CyanEyes', ratio=.4/.28)
        for y in (-.33, .33):
            cylinder('Dark head fastener', (side * .441, y, -.486), .022, .014, 'BR_DarkSteel', vertices=8)
            cylinder('Hex socket in head screw', (side * .441, y, -.495), .010, .003, 'BR_Rubber', vertices=6)
    box('Quiet speaker slit', (0, -.245, -.521), (.15, .020, .004), 'BR_DarkSteel', .007)
    for i in range(3):
        box('Speaker grille detail', (-.045 + i * .045, -.245, -.524), (.008, .009, .002), 'BR_Aluminum', .002)
    cylinder('Antenna mounting seal', (.20, .497, .03), .075, .025, 'BR_Rubber', 'Y')
    cylinder('Antenna foot', (.20, .516, .03), .053, .025, 'BR_DarkSteel', 'Y')
    cylinder('Short antenna stem', (.20, .594, .03), .014, .135, 'BR_Aluminum', 'Y', 16)
    cylinder('Antenna status tip', (.20, .667, .03), .029, .030, 'BR_CyanEyes', 'Y', 16)
    for i, (x, y) in enumerate([(-.43, .39), (-.40, -.40), (.42, .37), (.43, -.35), (-.28, .454), (.25, -.45)]):
        scuff((x, y, -.4821), .035, .012, i)
    join_into(head)
    # A modest enlargement keeps the familiar silhouette but gives the face more presence.
    head.data.transform(Matrix.Diagonal((1.12, 1.05, 1.10, 1)))
    head.data.transform(Matrix.Translation((0, 0, .05)))
    head.data.update()


def enhance_body_and_cover():
    body = STATE['models']['RobotBodyModel']
    sides, rear = set(), set()
    for poly in body.data.polygons:
        if body.data.materials[poly.material_index].name != 'BR_DarkSteel': continue
        points = [body.data.vertices[i].co for i in poly.vertices]
        if all(abs(p.x) > .45 for p in points): sides.update(poly.vertices)
        if all(-p.y > .18 for p in points): rear.update(poly.vertices)
    for i in sides: body.data.vertices[i].co.x += math.copysign(.0015, body.data.vertices[i].co.x)
    for i in rear: body.data.vertices[i].co.y -= .0015
    # These replace the visual neck and make its insertion into the head explicit.
    cylinder('Continuous neck spindle', (0, .38, 0), .05, .07, 'BR_DarkSteel', 'Y', 24)
    for y, radius, depth, mat in ((.357, .096, .022, 'BR_DarkSteel'), (.383, .088, .022, 'BR_Aluminum'), (.403, .078, .018, 'BR_DarkSteel')):
        cylinder('Neck rotary coupling', (0, y, 0), radius, depth, mat, 'Y', 32)
    for side in (-1, 1):
        box('Bolted lower mounting lug', (side * .32, -.324, -.17), (.105, .025, .048), 'BR_DarkSteel', .005)
        cylinder('Lower mounting bolt', (side * .32, -.324, -.199), .011, .009, 'BR_Aluminum', vertices=8)
        for y in (-.23, .23):
            box('Panel fixing tab', (side * .405, y, -.17), (.06, .052, .035), 'BR_DarkSteel', .006)
            cylinder('Tab fixing', (side * .425, y, -.192), .011, .009, 'BR_Aluminum', vertices=8)
    # Interior has routed mounts, not decorative wires crossing the connector travel.
    box('Internal motor mounting rail', (-.20, -.23, .095), (.35, .028, .045), 'BR_DarkSteel', .004)
    box('Internal cable channel', (-.31, .01, .11), (.035, .37, .026), 'BR_Rubber', .006)
    box('Rear service hatch gasket', (0, .005, .192), (.58, .41, .005), 'BR_Rubber', .014)
    box('Rear removable cooling hatch', (0, .005, .198), (.555, .385, .006), 'BR_TealPaint', .012)
    for x in (-.235, .235):
        for y in (-.145, .155):
            cylinder('Rear hatch fixing', (x, y, .204), .011, .006, 'BR_DarkSteel', vertices=8)
    for x in (-.115, .115):
        for y in (-.09, -.045, 0, .045, .09):
            box('Recessed rear ventilation slot', (x, y, .202), (.14, .012, .002), 'BR_DarkSteel', .003)
    join_into(body)
    cover = STATE['models']['RobotCoverModel']
    for side in (-1, 1):
        box('Panel edge hinge sleeve', (side * .378, -.15, .010), (.025, .11, .040), 'BR_DarkSteel', .004)
        for y in (-.23, .23):
            cylinder('Panel fastener gasket', (side * .275, y, -.0405), .042, .002, 'BR_DarkSteel', vertices=32)
    box('Recessed finger pull dark inset', (0, -.225, -.0406), (.15, .041, .001), 'BR_DarkSteel', .008)
    box('Finger pull lower lip', (0, -.245, -.0413), (.14, .008, .004), 'BR_Aluminum', .002)
    maker_mark('BR-01', (-.070, -.169, -.0407), .020)
    for i, (x, y, width, height) in enumerate([(-.365, .26, .025, .004), (.367, -.26, .023, .005), (-.31, -.285, .032, .004),
            (.31, .285, .035, .004), (-.18, .293, .022, .003), (.18, -.293, .019, .004), (-.375, -.12, .005, .021),
            (.375, .11, .005, .019), (-.29, .26, .014, .004), (.285, -.26, .014, .003)]):
        scuff((x, y, -.04015), width, height, i + 40)
    join_into(cover)


def cable(name, points, radius, mat):
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = radius
    curve.bevel_resolution = 2
    curve.resolution_u = 6
    spline = curve.splines.new('BEZIER')
    spline.bezier_points.add(len(points) - 1)
    for point, position in zip(spline.bezier_points, points):
        point.co = u(position)
        point.handle_left_type = point.handle_right_type = 'AUTO'
    obj = bpy.data.objects.new(name, curve)
    STATE['working'].objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target='MESH')
    return finish(bpy.context.object, name, mat)


def enhance_chassis():
    body = STATE['models']['RobotBodyModel']
    # The cover seats on a 1.5 mm seal; the original cleaning area stays unobstructed.
    for side in (-1, 1):
        box('Cover seating side ledge', (side * .39, 0, -.181), (.025, .62, .018), 'BR_Aluminum', .003)
        box('Compressed side gasket', (side * .392, 0, -.1908), (.012, .60, .0015), 'BR_Rubber', .0005)
        for y in (-.23, .23):
            box('Cover captive nut outrigger', (side * .335, y, -.173), (.14, .045, .030), 'BR_DarkSteel', .005)
            cylinder('Threaded cover mounting boss', (side * .275, y, -.179), .024, .026, 'BR_Aluminum', vertices=16)
            cylinder('Recess in captive cover nut', (side * .275, y, -.1925), .008, .0015, 'BR_Rubber', vertices=12)
            box('Captive panel alignment tongue', (side * .31, y, -.21), (.010, .016, .045), 'BR_Aluminum', .001)
    for y in (-.300, .300):
        box('Cover seating cross ledge', (0, y, -.181), (.755, .024, .018), 'BR_Aluminum', .003)
        box('Compressed horizontal gasket', (0, y, -.1908), (.76, .011, .0015), 'BR_Rubber', .0005)

    # Two coaxial studs and a shelf support the removable cartridge from the rear and below.
    for x, y in ((-.210, .080), (.040, -.110)):
        cylinder('Chassis motor mounting foot', (x, y, .105), .037, .012, 'BR_DarkSteel', vertices=16)
        cylinder('Drive threaded stand off', (x, y, .050), .022, .120, 'BR_Aluminum', vertices=16)
        cylinder('Drive resilient seating ring', (x, y, -.007), .029, .010, 'BR_Rubber', vertices=16)
    box('Drive cartridge lower shelf', (-.085, -.193, .04), (.39, .016, .14), 'BR_DarkSteel', .003)
    for x in (-.24, .07):
        box('Shelf bracket to chassis floor', (x, -.235, .055), (.025, .090, .11), 'BR_Aluminum', .003)
    # The cassette docks axially into a fixed transmission; it can still be pulled forwards.
    cylinder('Rear motor transmission housing', (-.04, -.03, .055), .064, .11, 'BR_TealPaint', vertices=24)
    cylinder('Cartridge rear docking coupling', (-.04, -.03, -.006), .032, .014, 'BR_DarkSteel', vertices=16)
    cylinder('Fixed shoulder transmission housing', (.49, .20, .055), .066, .11, 'BR_TealPaint', vertices=24)
    cable('Enclosed drive to shoulder transmission', [(-.04, -.03, .055), (.49, .20, .055)], .018, 'BR_DarkSteel')

    # Connector travel remains clear in front of the fixed rear-mounted carrier.
    box('Connector carrier plate', (.235, -.03, -.018), (.16, .18, .014), 'BR_TealPaint', .008)
    for x in (.175, .295):
        for y in (-.095, .035):
            cylinder('Connector carrier stand off', (x, y, .0495), .009, .121, 'BR_Aluminum', vertices=12)
            cylinder('Connector carrier front bolt', (x, y, -.027), .010, .005, 'BR_DarkSteel', vertices=8)
    # The service PCB rests on the floor bracket, clear of the chassis bottom frame.
    box('Service board floor angle', (-.06, -.275, -.04), (.395, .012, .14), 'BR_DarkSteel', .003)
    for x in (-.255, .135):
        box('PCB upright edge carrier', (x, -.235, -.075), (.012, .10, .012), 'BR_Aluminum', .002)
    for x in (-.23, .11):
        cylinder('PCB upper fixing', (x, -.20, -.100), .007, .005, 'BR_DarkSteel', vertices=8)
        box('PCB fixing backing tab', (x, -.20, -.077), (.030, .025, .010), 'BR_Aluminum', .002)
    for side in (-1, 1):
        offset = side * .005
        cable('Clipped fixed power harness', [(.235+offset, -.03, -.025), (.31+offset, -.03, .07),
              (.31+offset, -.13, .07), (.31+offset, -.23, .07), (.11+offset, -.23, .07),
              (.11+offset, -.23, -.081)], .0035, 'BR_OrangePaint' if side == 1 else 'BR_Rubber')
    for x, y in ((.31, -.10), (.31, -.20), (.19, -.23)):
        box('Rear harness clip foot', (x, y, .091), (.035, .018, .044), 'BR_DarkSteel', .003)
        box('Harness retaining saddle', (x, y, .067), (.026, .018, .010), 'BR_Aluminum', .002)
    join_into(body)

    # A rigid two-pole terminal moves with the cartridge and disengages from the sliding plug.
    drive = STATE['models']['DriveBodyModel']
    box('Cartridge terminal insulator', (.17, -.015, .039), (.035, .090, .018), 'BR_Rubber', .004)
    for y in (-.040, .010):
        box('Terminal rear root', (.2025, y, .034), (.065, .012, .009), 'BR_Aluminum', .002)
        box('Terminal folded neck', (.23, y, -.0055), (.012, .012, .079), 'BR_Aluminum', .002)
        box('Plug engagement blade', (.2575, y, -.045), (.055, .012, .009), 'BR_Aluminum', .002)
    join_into(drive)


def enhance_arm():
    arm = STATE['models']['ArmModel']
    inset = set()
    for poly in arm.data.polygons:
        if arm.data.materials[poly.material_index].name != 'BR_TealPaint': continue
        if all(abs(arm.data.vertices[i].co.y) > .48 and -arm.data.vertices[i].co.x < .1 for i in poly.vertices):
            inset.update(poly.vertices)
    for i in inset: arm.data.vertices[i].co.y += math.copysign(.003, arm.data.vertices[i].co.y)
    # The original beam remains the arm. A compact parallel-jaw gripper attaches to its end.
    cylinder('Wrist bearing seal', (.48, 0, 0), .36, .085, 'BR_Rubber', 'X')
    cylinder('Wrist turned collar', (.54, 0, 0), .34, .055, 'BR_Aluminum', 'X')
    box('Parallel jaw actuator', (.665, 0, 0), (.19, .67, .78), 'BR_TealPaint', .055)
    box('Jaw sliding crosshead', (.765, 0, 0), (.070, .53, .76), 'BR_DarkSteel', .025)
    for side in (-1, 1):
        z = side * .30
        box('Gripper finger root', (.875, 0, z), (.23, .40, .16), 'BR_Aluminum', .032)
        box('Angled gripping toe', (.995, 0, side * .23), (.07, .40, .20), 'BR_DarkSteel', .020)
        box('Inner rubber contact pad', (.995, 0, side * .1175), (.05, .29, .025), 'BR_Rubber', .009)
        cylinder('Gripper finger pivot', (.82, -.22, z), .070, .035, 'BR_DarkSteel', 'Y', 16)
    cylinder('Actuator inspection cap', (.665, 0, -.402), .14, .018, 'BR_DarkSteel', ratio=.7, vertices=16)
    join_into(arm)
    # The shoulder has its own unscaled local frame and stays on the existing Z-axis pivot.
    obj = cylinder('Rotary shoulder bearing housing', (0, 0, -.012), .0975, .17, 'BR_TealPaint', vertices=32)
    PARTS.remove(obj)
    obj.name = 'RobotShoulderModel'
    obj.matrix_world = C @ Matrix.Translation((.49, 1.723, .09)) @ C.inverted()
    for z, radius, depth, mat in ((-.107, .106, .025, 'BR_Rubber'), (-.125, .096, .025, 'BR_Aluminum'),
                                (-.144, .072, .018, 'BR_DarkSteel'), (-.156, .031, .012, 'BR_Aluminum')):
        cylinder('Shoulder concentric bearing', (0, 0, z), radius, depth, mat, vertices=32)
    for i in range(4):
        a = i * math.tau / 4 + math.pi / 4
        cylinder('Shoulder casing bolt', (math.cos(a) * .073, math.sin(a) * .073, -.142), .010, .009, 'BR_DarkSteel', vertices=8)
    join_into(obj)
    STATE['models'][obj.name] = obj


def preview_hardware():
    manifest = json.loads(PLACEMENT.read_text(encoding='utf-8-sig'))
    suppressed = {'Faceplate', 'Eye', 'Speaker', 'Neck', 'ShoulderBearing', 'ArmCuff', 'ReleasedBand', 'CheckStroke'}
    for row in manifest:
        if row['asset'] != 'Library/unity default resources' or row['name'] in suppressed:
            continue
        if row['parent'] in {'CoverSocket', 'DriveSocket'}:
            continue
        kind = row['mesh']
        if kind == 'Cube':
            bpy.ops.mesh.primitive_cube_add(size=1)
        elif kind == 'Cylinder':
            bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=.5, depth=2)
        elif kind == 'Sphere':
            bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=.5)
        else:
            continue
        obj = bpy.context.object
        obj.name = 'Unity_' + row['parent'] + '_' + row['name']
        for collection in list(obj.users_collection):
            collection.objects.unlink(obj)
        STATE['preview'].objects.link(obj)
        obj.matrix_world = C @ Matrix(row['matrix']) @ C.inverted()
        if row.get('solder'):
            color = row['materials'][0]['color']
            name = 'Preview_' + row['materials'][0]['name']
            mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
            mat.diffuse_color = color
            mat.use_nodes = True
            shader = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
            shader.inputs['Base Color'].default_value = color
            obj.data.materials.append(mat)
        else:
            obj.data.materials.append(MATERIALS['BR_DarkSteel' if row['name'] == 'ScrewHead' else 'BR_Rubber' if row['name'] == 'ScrewSlot' else 'BR_Aluminum'])
    # The existing platform remains a separate static support, never part of the export.
    row = next(r for r in manifest if r['parent'] == 'ServicePlatformModel')
    with bpy.data.libraries.load(str(ROOT / 'ArtSource/Workbench/WorkbenchFurniture.blend'), link=False) as (available, loaded):
        loaded.objects = ['ServicePlatformModel']
    platform = loaded.objects[0]
    STATE['preview'].objects.link(platform)
    platform.matrix_world = C @ Matrix(row['matrix']) @ C.inverted()
    platform.hide_set(False)
    platform.hide_render = False


def enhance():
    if STATE.get('enhanced'):
        raise RuntimeError('Already refined: reload the saved source instead of layering another copy.')
    enhance_head()
    enhance_body_and_cover()
    enhance_arm()
    enhance_chassis()
    preview_hardware()
    STATE['enhanced'] = True
    print('Refined the existing head, body, detachable cover and arm; added one shoulder visual.')


def validate():
    assert len(STATE['backup'].objects) == 5
    assert sum(len(o.data.vertices) for o in STATE['backup'].objects) == STATE['baseline_vertices']
    for name, matrix in STATE['original_objects'].items():
        assert tuple(tuple(row) for row in STATE['original_scene'].objects[name].matrix_world) == matrix, name
    rows = []
    manifest = json.loads(PLACEMENT.read_text(encoding='utf-8-sig'))
    placements = {r['parent']: Matrix(r['matrix']) for r in manifest if r['parent'] in NAMES}
    for obj in STATE['models'].values():
        if obj.name in NAMES:
            expected = C @ placements[obj.name] @ C.inverted()
            assert all(abs(obj.matrix_world[y][x] - expected[y][x]) < 1e-6 for y in range(4) for x in range(4)), obj.name + ': pose differs from Unity anchor'
        assert all(math.isfinite(c) for vertex in obj.data.vertices for c in vertex.co)
        assert all(obj.data.materials)
        obj.data.calc_loop_triangles()
        coords = [(-v.co.x, v.co.z, -v.co.y) for v in obj.data.vertices]
        lo = [min(p[i] for p in coords) for i in range(3)]
        hi = [max(p[i] for p in coords) for i in range(3)]
        rows.append({'name': obj.name, 'triangles': len(obj.data.loop_triangles), 'min': lo, 'max': hi,
                     'materials': [m.name for m in obj.data.materials]})
    count = sum(row['triangles'] for row in rows)
    assert count < 45000, count
    report = {'status': 'passed', 'triangles': count, 'backup_preserved': True, 'unrelated_scene_preserved': True, 'meshes': rows}
    REPORT.mkdir(parents=True, exist_ok=True)
    (REPORT / 'blender-validation.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report))
    return report


def save_export():
    validate()
    poses = {o: o.matrix_world.copy() for o in STATE['models'].values()}
    bpy.ops.object.select_all(action='DESELECT')
    try:
        for obj in poses:
            obj.matrix_world = Matrix.Identity(4)
            obj.select_set(True)
        bpy.context.view_layer.objects.active = STATE['models']['RobotHeadModel']
        bpy.ops.export_scene.fbx(filepath=str(EXPORT), use_selection=True, object_types={'MESH'},
            axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True,
            use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False, path_mode='AUTO')
    finally:
        for obj, matrix in poses.items():
            obj.matrix_world = matrix
    bpy.ops.wm.save_as_mainfile(filepath=str(DESTINATION))
    print('Saved refined editable source and Unity FBX: ' + str(DESTINATION))


if __name__ == '__main__':
    prepare()
    enhance()
    save_export()
