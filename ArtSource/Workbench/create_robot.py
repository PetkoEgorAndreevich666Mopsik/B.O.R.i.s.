"""Editable robot visual shells. Run with Blender --background --python."""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "ArtSource/Workbench/WorkbenchRobot.blend"
EXPORT = ROOT / "Assets/_Project/Art/Models/WorkbenchRobot.fbx"
REPORT = ROOT / "Temp/WorkbenchModels/robot-model-report.json"
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
    "WB_Orange": ((.88, .31, .055, 1), .4, .15),
    "WB_Ivory": ((.72, .74, .63, 1), .48, .2),
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
    MATERIALS[name] = material


def xyz(unity):
    return (-unity[0], -unity[2], unity[1])


def finish(obj, name, material, bevel):
    obj.name = name
    obj.data.materials.append(MATERIALS[material])
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    modifier = obj.modifiers.new("Soft machined edges", "BEVEL")
    modifier.width = bevel
    modifier.segments = 3
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    modifier = obj.modifiers.new("Weighted corner normals", "WEIGHTED_NORMAL")
    modifier.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def box(name, position, size, material, bevel=.006):
    bpy.ops.mesh.primitive_cube_add(size=1, location=xyz(position))
    obj = bpy.context.object
    obj.dimensions = (size[0], size[2], size[1])
    return finish(obj, name, material, bevel)


def cylinder(name, position, radius, depth, material, axis="Z", vertices=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth,
                                     location=xyz(position))
    obj = bpy.context.object
    if axis == "Z":
        obj.rotation_euler.x = math.pi / 2
    elif axis == "X":
        obj.rotation_euler.y = math.pi / 2
    return finish(obj, name, material, min(.003, depth * .15, radius * .1))


ASSETS = []


def group(name, parts):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + "Mesh"
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    obj.data.validate(verbose=True)
    obj.data.update()
    bpy.context.view_layer.update()
    ASSETS.append(obj)
    return obj


parts = [box("Ivory head housing", (0, 0, 0), (.96, 1, .96), "WB_Ivory", .075)]
parts.append(box("Rear service panel", (0, 0, .482), (.79, .81, .036), "WB_Teal", .045))
for side in (-1, 1):
    parts.append(cylinder("Recessed ear ring", (side * .474, -.015, .025), .22, .052, "WB_Dark", "X", 24))
    parts.append(cylinder("Teal ear cover", (side * .488, -.015, .025), .175, .024, "WB_Teal", "X", 24))
    parts.append(cylinder("Ear centre pin", (side * .497, -.015, .025), .065, .006, "WB_Steel", "X", 16))
    for y in (-.33, .33):
        parts.append(cylinder("Front housing screw", (side * .441, y, -.479), .020, .006, "WB_Steel", vertices=8))
for y in (-.19, -.09, .01, .11):
    parts.append(box("Rear cooling recess", (0, y, .497), (.39, .029, .006), "WB_Dark", .003))
group("RobotHeadModel", parts)

parts = [
    box("Teal back shell", (0, 0, .15), (.84, .62, .08), "WB_Teal", .012),
    box("Left open cavity wall", (-.42, 0, 0), (.08, .62, .38), "WB_Teal", .012),
    box("Right open cavity wall", (.42, 0, 0), (.08, .62, .38), "WB_Teal", .012),
    box("Ivory top frame", (0, .34, 0), (.92, .06, .38), "WB_Ivory", .012),
    box("Ivory bottom frame", (0, -.31, 0), (.92, .06, .38), "WB_Ivory", .012),
]
for side in (-1, 1):
    for y in (-.245, .245):
        parts.append(cylinder("Front frame fixing", (side * .42, y, -.185), .017, .008, "WB_Steel", vertices=8))
    for y in (-.12, -.08, -.04, 0, .04, .08):
        parts.append(box("Flush side cooling slot", (side * .458, y, .02), (.004, .012, .135), "WB_Dark", .002))
    parts.append(box("Side shell seam", (side * .458, .20, .02), (.004, .009, .22), "WB_Dark", .001))
parts.append(box("Rear panel seam", (0, .20, .188), (.64, .009, .004), "WB_Dark", .001))
group("RobotBodyModel", parts)

parts = [
    box("Metal cover rim", (0, 0, .006), (.8, .62, .068), "WB_Steel", .020),
    box("Orange flat service panel", (0, 0, -.003), (.778, .598, .074), "WB_Orange", .018),
]
group("RobotCoverModel", parts)

# The existing screw heads need their original front seating plane at z=-.06.
parts = [box("Orange mounting plate", (0, 0, .05), (.38, .34, .02), "WB_Orange", .014)]
for x, y in ((-.125, .095), (.125, -.095)):
    parts.append(cylinder("Orange screw boss", (x, y, 0), .055, .12, "WB_Orange", vertices=16))
    parts.append(cylinder("Steel seating washer", (x, y, -.057), .039, .006, "WB_Steel", vertices=16))
parts.append(cylinder("Steel motor can", (.045, -.015, -.012), .089, .096, "WB_Steel", vertices=32))
for z in (-.038, -.014, .010):
    parts.append(cylinder("Motor cooling seam", (.045, -.015, z), .0895, .006, "WB_Dark", vertices=32))
parts.append(box("Motor identification recess", (-.145, -.115, .037), (.045, .035, .007), "WB_Dark", .004))
group("DriveBodyModel", parts)

parts = [
    box("Ivory arm shell", (-.15, 0, 0), (.7, 1, 1), "WB_Ivory", .09),
    box("Teal arm cuff", (.30, 0, 0), (.20, 1, 1), "WB_Teal", .055),
    box("Ivory end cap", (.45, 0, 0), (.1, .98, .98), "WB_Ivory", .045),
]
for z in (-.493, .493):
    parts.append(box("Arm inset panel", (-.14, 0, z), (.39, .56, .014), "WB_Teal", .055))
group("ArmModel", parts)

limits = {
    "RobotHeadModel": ((-.5, -.5, -.5), (.5, .5, .5)),
    "RobotBodyModel": ((-.46, -.34, -.19), (.46, .37, .19)),
    "RobotCoverModel": ((-.4, -.31, -.04), (.4, .31, .04)),
    "DriveBodyModel": ((-.19, -.17, -.06), (.19, .17, .06)),
    "ArmModel": ((-.5, -.5, -.5), (.5, .5, .5)),
}
report = {"units": "metres except normalized head and arm", "source_coordinates": "Unity X,Y,Z", "meshes": []}
for obj in ASSETS:
    assert obj.location.length < 1e-7, obj.name
    assert all(math.isfinite(c) for vertex in obj.data.vertices for c in vertex.co), obj.name
    assert len(obj.data.materials) and all(obj.data.materials), obj.name
    obj.data.calc_loop_triangles()
    coords = [(-v.co.x, v.co.z, -v.co.y) for v in obj.data.vertices]
    lower = [min(v[i] for v in coords) for i in range(3)]
    upper = [max(v[i] for v in coords) for i in range(3)]
    lo, hi = limits[obj.name]
    assert all(lower[i] >= lo[i] - 1e-5 and upper[i] <= hi[i] + 1e-5 for i in range(3)), (obj.name, lower, upper)
    report["meshes"].append({"name": obj.name, "bounds_min": lower, "bounds_max": upper,
        "triangles": len(obj.data.loop_triangles), "materials": [m.name for m in obj.data.materials]})
report["total_triangles"] = sum(m["triangles"] for m in report["meshes"])
assert report["total_triangles"] < 15000, report["total_triangles"]
report["integration"] = {
    "RobotHeadModel": "Replace original Head renderer only; retain Head transform, Faceplate, eyes and Speaker. Model normalized to unit cube.",
    "RobotBodyModel": "Parent at Body local origin with unit scale. Hide Back, Left, Right, Top, Bottom renderers only; retain Neck, FrameDirt, Head and colliders. Open negative-Z cavity.",
    "RobotCoverModel": "Parent at Cover local origin with unit scale. Hide CoverPanel renderer only; retain all four screws, dirt patches and InnerFace.",
    "DriveBodyModel": "Parent at RepairPart local origin with unit scale for both drives. Hide DriveBody renderer only; keep screws, DriveCore, MotorEndCap and DriveShaft.",
    "ArmModel": "Replace Arm renderer only on its existing transform. Model normalized to unit cube; retain ArmCuff and shoulder pivot."
}
report["risks"] = ["FBX has no colliders; retain existing interaction colliders and pivots.",
    "Head faceplate and arm cuff are intentionally supplied by existing children; do not hide their renderers.",
    "Drive screw bosses keep the original z=-.06 front plane; existing caps/core/shaft intentionally finish the motor front."]
REPORT.parent.mkdir(parents=True, exist_ok=True)
REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
bpy.ops.object.select_all(action="DESELECT")
for obj in ASSETS:
    obj.select_set(True)
bpy.context.view_layer.objects.active = ASSETS[0]
bpy.ops.export_scene.fbx(filepath=str(EXPORT), use_selection=True, object_types={"MESH"},
    axis_forward="-Z", axis_up="Y", apply_unit_scale=True, bake_space_transform=True,
    use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="AUTO")

for obj in ASSETS:
    collection = bpy.data.collections.new(obj.name.removesuffix("Model"))
    bpy.context.scene.collection.children.link(collection)
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    collection.objects.link(obj)
    obj.hide_set(obj.name != "RobotBodyModel")
    obj.hide_render = obj.name != "RobotBodyModel"
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
print("WORKBENCH_ROBOT_REPORT=" + json.dumps(report))
