"""Editable, metre-scale workbench kit. Run with Blender --background --python."""
import bpy
import json
import math
import random
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "ArtSource/Workbench/WorkbenchFurniture.blend"
EXPORT = ROOT / "Assets/_Project/Art/Models/WorkbenchFurniture.fbx"
REPORT = ROOT / "Temp/WorkbenchModels/furniture-model-report.json"
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
for material in list(bpy.data.materials):
    bpy.data.materials.remove(material)
bpy.context.scene.unit_settings.system = "METRIC"
bpy.context.scene.unit_settings.scale_length = 1
bpy.context.preferences.filepaths.save_version = 0

PALETTE = {
    "WB_Teal": ((.16, .36, .35, 1), .5, .35),
    "WB_Dark": ((.045, .06, .065, 1), .7, .05),
    "WB_Steel": ((.47, .54, .55, 1), .3, .8),
    "WB_Wood": ((.48, .255, .105, 1), .55, 0),
    "WB_Orange": ((.88, .31, .055, 1), .4, .15),
    "WB_Cyan": ((.12, .8, .8, 1), .3, .15),
}
MATERIALS = {}
for name, (color, roughness, metallic) in PALETTE.items():
    material = bpy.data.materials.new(name)
    material.diffuse_color = color
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metallic
    if name == "WB_Cyan":
        shader.inputs["Emission Color"].default_value = color
        shader.inputs["Emission Strength"].default_value = .6
    MATERIALS[name] = material


def xyz(unity):
    return (-unity[0], -unity[2], unity[1])


def finish(obj, name, material, bevel=0):
    obj.name = name
    obj.data.materials.append(MATERIALS[material])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        modifier = obj.modifiers.new("Machined edge radius", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        normals = obj.modifiers.new("Corner normals", "WEIGHTED_NORMAL")
        normals.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=normals.name)
    return obj


def box(name, position, size, material, bevel=.005):
    bpy.ops.mesh.primitive_cube_add(size=1, location=xyz(position))
    obj = bpy.context.object
    obj.dimensions = (size[0], size[2], size[1])
    return finish(obj, name, material, bevel)


def cylinder(name, position, radius, depth, material, axis="Y", vertices=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth,
                                     location=xyz(position))
    obj = bpy.context.object
    if axis == "Z":
        obj.rotation_euler.x = math.pi / 2
    elif axis == "X":
        obj.rotation_euler.y = math.pi / 2
    return finish(obj, name, material, min(radius * .12, depth * .15))


def line(name, points, radius, material):
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 1
    curve.bevel_depth = radius
    curve.bevel_resolution = 0
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for point, value in zip(spline.points, points):
        point.co = (*xyz(value), 1)
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    obj.data.materials.append(MATERIALS[material])
    return obj


MODELS = []


def group(name, objects, normalize=False):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    if normalize:
        bounds = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
        low = Vector(tuple(min(p[i] for p in bounds) for i in range(3)))
        high = Vector(tuple(max(p[i] for p in bounds) for i in range(3)))
        center = (low + high) / 2
        extent = high - low
        for vertex in obj.data.vertices:
            vertex.co = Vector(tuple((vertex.co[i] - center[i]) / extent[i] for i in range(3)))
    obj.data.name = name + "Mesh"
    obj.data.validate(verbose=True)
    obj.data.update()
    bpy.context.view_layer.update()
    MODELS.append(obj)
    return obj


parts = []
for i in range(4):
    z = -.7125 + i * .475
    parts.append(box("Hardwood plank", (0, .999, z), (3.4, .178, .471), "WB_Wood", .012))
random.seed(24)
for i in range(25):
    z = random.uniform(-.89, .89)
    start = random.uniform(-1.6, .7)
    length = random.uniform(.25, .85)
    points = [(start + j * length / 6, 1.0884,
               z + math.sin(j * .8 + i) * .0025) for j in range(7)]
    parts.append(line("Restrained wood grain", points, .00055, "WB_Dark"))
for x in (-1.42, 1.42):
    for z in (-.70, .70):
        parts.append(box("Square steel leg", (x, .475, z), (.11, .85, .11), "WB_Teal", .009))
        parts.append(box("Rubber foot", (x, .025, z), (.19, .05, .18), "WB_Dark", .012))
        parts.append(box("Leg collar", (x, .81, z), (.15, .16, .15), "WB_Steel", .008))
for z in (-.73, .73):
    parts.append(box("Apron rail", (0, .856, z), (3.06, .108, .09), "WB_Teal", .008))
    parts.append(box("Lower tie rail", (0, .19, z), (2.84, .07, .07), "WB_Teal", .006))
for x in (-1.42, 1.42):
    parts.append(box("Side apron", (x, .856, 0), (.1, .108, 1.46), "WB_Teal", .008))
    parts.append(box("Side foot brace", (x, .19, 0), (.07, .07, 1.4), "WB_Teal", .006))
for x in (-1.64, 1.64):
    for z in (-.89, .89):
        parts.append(box("Corner protector", (x, 1.077, z), (.116, .026, .116), "WB_Steel", .007))
        parts.append(cylinder("Flush corner bolt", (x, 1.088, z), .012, .004, "WB_Dark", vertices=12))
for z in (-.942, .942):
    parts.append(box("Steel edge trim", (0, .965, z), (3.37, .042, .016), "WB_Dark", .004))
group("WorkbenchModel", parts)

# The central pad is the highest surface: the robot's existing floor stays unchanged.
parts = [box("Pedestal chassis", (0, -.007, 0), (1.04, .076, .68), "WB_Teal", .025),
         box("Steel upper plate", (0, .03, 0), (.998, .014, .638), "WB_Steel", .012),
         box("Non slip center pad", (0, .039, 0), (.855, .012, .50), "WB_Dark", .013)]
for x in (-.461, .461):
    for z in (-.281, .281):
        parts.append(cylinder("Countersunk pedestal bolt", (x, .043, z), .016, .004, "WB_Dark", vertices=12))
for x in (-.5, .5):
    parts.append(box("Corner bumper", (x, -.014, 0), (.04, .044, .36), "WB_Dark", .01))
for x in (-.34, .34):
    parts.append(box("Status inset", (x, .003, -.337), (.11, .018, .006), "WB_Cyan", .003))
group("ServicePlatformModel", parts)


def tray(name, width, depth):
    # Build in real proportions before normalization so Unity restores real edge radii.
    height = .11
    wall = .024
    floor_y = -.045
    pieces = [box("Tray floor", (0, floor_y, 0), (width - .01, .02, depth - .01), "WB_Wood", .01),
              box("Dark foam liner", (0, -.027, 0), (width - .065, .016, depth - .065), "WB_Dark", .008)]
    for x in (-width / 2 + wall / 2, width / 2 - wall / 2):
        pieces.append(box("Tray side wall", (x, -.004, 0), (wall, height - .008, depth), "WB_Wood", .009))
        pieces.append(box("Metal rim cap", (x, .048, 0), (wall + .002, .014, depth - .018), "WB_Steel", .005))
    for z in (-depth / 2 + wall / 2, depth / 2 - wall / 2):
        pieces.append(box("Tray end wall", (0, -.004, z), (width - 2 * wall, height - .008, wall), "WB_Wood", .009))
        pieces.append(box("Metal rim cap", (0, .048, z), (width - 2 * wall, .014, wall + .002), "WB_Steel", .005))
    for x in (-width / 2 + wall / 2, width / 2 - wall / 2):
        for z in (-depth / 2 + .033, depth / 2 - .033):
            pieces.append(cylinder("Tray rivet", (x, .053, z), .006, .004, "WB_Dark", vertices=8))
    pieces.append(box("Recessed front label", (0, .002, -depth / 2 - .0005), (width * .42, .035, .003), "WB_Teal", .003))
    return group(name, pieces, normalize=True)


tray("PartsTrayModel", .57, .45)
tray("CoverTrayModel", .95, .68)

parts = [box("Diagnostic base", (0, -.002, 0), (.6, .068, .32), "WB_Teal", .018),
         box("Top socket bed", (0, .034, .017), (.53, .012, .24), "WB_Steel", .008),
         box("Socket rubber mat", (0, .038, .015), (.42, .004, .19), "WB_Dark", .006),
         box("Control strip", (0, .03, -.139), (.44, .02, .036), "WB_Dark", .005)]
for x in (-.245, .245):
    for z in (-.105, .105):
        parts.append(box("Instrument rubber foot", (x, -.035, z), (.075, .01, .065), "WB_Dark", .005))
        parts.append(cylinder("Top casing screw", (x, .039, z), .007, .002, "WB_Dark", vertices=8))
for i in range(5):
    parts.append(box("Recessed ventilation", (-.12 + i * .06, -.006, -.159), (.032, .018, .002), "WB_Dark", .002))
parts.append(box("Live status light", (-.16, .039, -.139), (.036, .002, .014), "WB_Cyan", .003))
parts.append(box("Orange diagnostic key", (.16, .038, -.139), (.038, .004, .024), "WB_Orange", .004))
group("DriveStandModel", parts, normalize=True)

report = {"coordinate_system": "Unity X,Y,Z converted to Blender -X,-Z,Y for Unity FBX handedness", "models": {}}
bpy.context.view_layer.update()
for obj in MODELS:
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    unity = [(-point.x, point.z, -point.y) for point in corners]
    low = [round(min(point[i] for point in unity), 6) for i in range(3)]
    high = [round(max(point[i] for point in unity), 6) for i in range(3)]
    obj.data.calc_loop_triangles()
    report["models"][obj.name] = {"bounds_min": low, "bounds_max": high,
        "triangles": len(obj.data.loop_triangles), "vertices": len(obj.data.vertices),
        "materials": [material.name for material in obj.data.materials]}
    assert obj.location.length < .000001 and all(abs(scale - 1) < .000001 for scale in obj.scale)
assert set(report["models"]) == {"WorkbenchModel", "ServicePlatformModel", "PartsTrayModel", "CoverTrayModel", "DriveStandModel"}
assert report["models"]["WorkbenchModel"]["bounds_max"][1] <= 1.090001
assert report["models"]["ServicePlatformModel"]["bounds_max"][1] <= .045001
for name in ("PartsTrayModel", "CoverTrayModel", "DriveStandModel"):
    assert report["models"][name]["bounds_min"] == [-.5, -.5, -.5]
    assert report["models"][name]["bounds_max"] == [.5, .5, .5]
assert sum(model["triangles"] for model in report["models"].values()) < 18000
REPORT.parent.mkdir(parents=True, exist_ok=True)
REPORT.write_text(json.dumps(report, indent=2))
SOURCE.parent.mkdir(parents=True, exist_ok=True)
EXPORT.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="DESELECT")
for obj in MODELS:
    obj.select_set(True)
bpy.context.view_layer.objects.active = MODELS[0]
bpy.context.scene.render.engine = "CYCLES"
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
bpy.ops.export_scene.fbx(filepath=str(EXPORT), use_selection=True, object_types={"MESH"},
                         axis_forward="-Z", axis_up="Y", global_scale=1,
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         use_space_transform=True, bake_space_transform=True,
                         mesh_smooth_type="FACE", add_leaf_bones=False,
                         bake_anim=False, path_mode="AUTO")
print("FURNITURE_REPORT", json.dumps(report))
