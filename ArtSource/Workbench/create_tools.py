"""Rebuild the prototype workbench tools with Blender 5.x, in metres.

Run: blender --background --python ArtSource/Workbench/create_tools.py
Unity coordinates are converted to Blender before mesh construction, including
the X reflection needed for Unity's left-handed FBX import. Each final mesh
keeps its gameplay origin at zero; only the optional preview is arranged.
"""
import bpy
import bmesh
import math
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Temp/WorkbenchModels"
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
for material in list(bpy.data.materials):
    bpy.data.materials.remove(material)
bpy.context.scene.unit_settings.system = "METRIC"
bpy.context.scene.unit_settings.scale_length = 1.0


def material(name, color, metal=0.0, roughness=0.45):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Metallic"].default_value = metal
    shader.inputs["Roughness"].default_value = roughness
    return mat


M = {
    "teal": material("WB_Teal", (0.10, 0.29, 0.27), 0.40, 0.37),
    "dark": material("WB_Dark", (0.035, 0.045, 0.046), 0.05, 0.60),
    "steel": material("WB_Steel", (0.48, 0.55, 0.55), 0.82, 0.28),
    "wood": material("WB_Wood", (0.42, 0.20, 0.075), 0.0, 0.49),
    "blue": material("WB_Blue", (0.035, 0.16, 0.36), 0.12, 0.35),
    "orange": material("WB_Orange", (0.91, 0.28, 0.035), 0.16, 0.42),
    "bristles": material("WB_Bristles", (0.32, 0.22, 0.10), 0.0, 0.85),
    "cyan": material("WB_Cyan", (0.035, 0.80, 0.72), 0.12, 0.27),
}


def u(p):
    return (-p[0], -p[2], p[1])


parts = []


def finish(obj, name, mat, bevel=0):
    obj.name = name
    obj.data.materials.append(M[mat])
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        modifier = obj.modifiers.new("Soft manufactured edges", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        normal = obj.modifiers.new("Weighted face normals", "WEIGHTED_NORMAL")
        normal.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=normal.name)
    parts.append(obj)
    return obj


def box(name, p, size, mat, bevel=0.001):
    bpy.ops.mesh.primitive_cube_add(size=1, location=u(p))
    obj = bpy.context.object
    obj.dimensions = (size[0], size[2], size[1])
    return finish(obj, name, mat, bevel)


def cylinder(name, p, radius, length, mat, axis="Y", vertices=16, bevel=0.0007):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius,
                                       depth=length, location=u(p))
    obj = bpy.context.object
    if axis == "X":
        obj.rotation_euler[1] = math.pi / 2
    elif axis == "Z":
        obj.rotation_euler[0] = math.pi / 2
    return finish(obj, name, mat, bevel)


def profile(name, rings, mat, segments=16):
    """Y-axis turned part, (height, radius), with flat closed ends."""
    vertices = [u((radius * math.cos(i * math.tau / segments), height,
                    radius * math.sin(i * math.tau / segments)))
                for height, radius in rings for i in range(segments)]
    faces = [tuple(reversed(range(segments)))]
    for ring in range(len(rings) - 1):
        for i in range(segments):
            a = ring * segments + i
            b = ring * segments + (i + 1) % segments
            faces.append((a, b, b + segments, a + segments))
    faces.append(tuple(range((len(rings) - 1) * segments, len(rings) * segments)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    finish(obj, name, mat)
    for polygon in mesh.polygons:
        polygon.use_smooth = len(polygon.vertices) == 4
    return obj


def join_asset(name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(mesh, faces=list(mesh.faces))
    mesh.to_mesh(obj.data)
    mesh.free()
    parts.clear()
    return obj


# The tip remains y=0; the original Handle collider is centred at y=.23.
box("Flat blade", (0, 0.008, 0), (0.014, 0.016, 0.005), "steel", 0.0006)
cylinder("Hex shaft", (0, 0.086, 0), 0.008, 0.14, "steel", vertices=8)
profile("Blue moulded grip", [(0.153, .016), (.162, .024), (.175, .021),
                              (.192, .022), (.210, .028), (.251, .027),
                              (.266, .024), (.272, .018)], "blue")
cylinder("Grip guard", (0, .163, 0), .024, .007, "dark")
cylinder("End cap", (0, .269, 0), .024, .006, "blue")
for angle in (0, math.pi / 2, math.pi, math.pi * 1.5):
    pad = box("Rubber grip inset", (.025 * math.sin(angle), .229,
                                     .025 * math.cos(angle)),
              (.023, .047, .004), "dark", .0015)
    pad.rotation_euler[2] = -angle
box("Screwdriver orange index", (0, .264, -.0235), (.012, .003, .002), "orange", .0004)
screwdriver = join_asset("ScrewdriverModel")

# The brush's contact face stays at z=0 and the Handle collider at (0,.09,.07).
# Individual low-poly tapered tufts leave visible gaps instead of a solid block.
handle = profile("Turned wooden brush handle", [(0.025, .015), (.041, .015),
                  (.063, .019), (.100, .025), (.148, .023), (.174, .017),
                  (.182, .006)], "wood", segments=12)
handle.location = u((0, 0, .07))
box("Brush ferrule", (0, .026, .049), (.086, .026, .052), "steel", .003)
box("Ferrule seam", (0, .017, .0215), (.072, .003, .002), "dark", .0005)
for row in range(3):
    for col in range(9):
        x = (col - 4) * .0095
        z = .005 + row * .011
        bottom = -.024 + .003 * ((col + row) % 3)
        bpy.ops.mesh.primitive_cone_add(vertices=5, radius1=.0035,
            radius2=.005, depth=.024-bottom, location=u((x, (bottom+.024)/2, z)))
        obj = bpy.context.object
        finish(obj, "Bristle tuft", "bristles")
brush = join_asset("BrushModel")

# Compact station body fits the existing .56 x .24 x .42 box and collider.
for x in (-.23, .23):
    for z in (-.16, .16):
        box("Rubber station foot", (x, .012, z), (.06, .024, .07), "dark", .006)
box("Steel station plinth", (0, .040, 0), (.555, .034, .415), "steel", .006)
box("Teal station enclosure", (0, .14, .012), (.54, .20, .386), "teal", .018)
box("Inset front fascia", (0, .136, -.192), (.483, .151, .018), "dark", .011)
box("Display bezel", (-.105, .157, -.205), (.231, .078, .011), "steel", .005)
box("Temperature screen", (-.105, .157, -.212), (.208, .054, .007), "dark", .003)
# A simple seven-segment 350 is geometry and remains legible without textures.
segments = {
    "3": "abgcd", "5": "afgcd", "0": "abcdef"
}
for digit, x in zip("350", (-.162, -.105, -.048)):
    coords = {"a": (0, .019, True), "g": (0, 0, True), "d": (0, -.019, True),
              "b": (.016, .0095, False), "c": (.016, -.0095, False),
              "e": (-.016, -.0095, False), "f": (-.016, .0095, False)}
    for segment in segments[digit]:
        dx, dy, horizontal = coords[segment]
        box("Temperature segment", (x + dx, .157 + dy, -.217),
            (.027, .0035, .0015) if horizontal else (.0035, .0155, .0015), "cyan", .0004)
cylinder("Temperature dial rim", (.155, .139, -.210), .050, .021, "steel", "Z", 24)
cylinder("Temperature dial", (.155, .139, -.229), .041, .025, "dark", "Z", 24)
box("Dial index", (.155, .168, -.244), (.005, .012, .002), "orange", .0005)
for i in range(7):
    angle = math.radians(-130 + i * 43)
    x = .155 + math.sin(angle) * .061
    y = .139 + math.cos(angle) * .061
    mark = box("Dial tick", (x, y, -.204), (.003, .008, .002), "steel", .0003)
    mark.rotation_euler[1] = -angle
cylinder("Power button bezel", (-.19, .082, -.206), .018, .012, "steel", "Z")
cylinder("Orange power button", (-.19, .082, -.215), .012, .008, "orange", "Z")
for z in (-.11, -.062, -.014, .034, .082, .13):
    box("Side ventilation recess", (.271, .15, z), (.003, .085, .017), "dark", .002)
for x in (-.235, .235):
    for y in (.074, .201):
        cylinder("Fascia fastener", (x, y, -.205), .008, .006, "steel", "Z", 8)
        box("Fastener slot", (x, y, -.209), (.008, .0015, .001), "dark", .0002)
box("Top protective panel", (0, .242, .01), (.30, .005, .24), "dark", .007)
station = join_asset("SolderingStationModel")

# Iron origin is the hot contact point; Handle's picking volume is preserved.
profile("Iron hot tip", [(0, .0028), (.009, .0045), (.036, .006)], "steel", 12)
profile("Heating barrel", [(.034, .006), (.043, .010), (.060, .010), (.064, .014)], "steel")
profile("Heat collar", [(.060, .019), (.066, .023), (.074, .023), (.079, .018)], "teal")
profile("Iron rubber grip", [(.074, .017), (.084, .021), (.167, .020),
                              (.188, .016), (.201, .014)], "dark")
for y in (.086, .094, .102, .110):
    cylinder("Grip ridge", (0, y, 0), .022, .003, "dark", vertices=16, bevel=.0004)
cylinder("Grip orange marker", (0, .177, 0), .019, .005, "orange")
profile("Cable strain relief", [(.198, .010), (.206, .012), (.213, .009),
                                 (.218, .007)], "dark", 12)
iron = join_asset("SolderingIronModel")

# This shared rack matches the previously verified parked tool coordinates.
box("Rack rubber feet", (0, -.011, 0), (.53, .028, .26), "dark", .008)
box("Rack enamel base", (0, .009, 0), (.55, .032, .28), "teal", .009)
box("Rack wood top", (0, .027, 0), (.51, .012, .245), "wood", .006)
# Screwdriver tip sits inside a shallow cup; shaft passes between the supports.
box("Tip cup base", (-.15, .038, -.05), (.074, .012, .074), "dark", .004)
for x in (-.181, -.119):
    box("Tip cup side", (x, .050, -.05), (.012, .020, .074), "teal", .002)
for z in (-.081, -.019):
    box("Tip cup end", (-.15, .050, z), (.050, .020, .012), "teal", .002)
box("Screwdriver dock rear", (-.15, .103, -.01), (.106, .14, .034), "teal", .006)
for x in (-.187, -.113):
    box("Open shaft guide", (x, .145, -.065), (.018, .035, .063), "dark", .003)
box("Brush clip upright", (.11, .153, .102), (.070, .24, .032), "teal", .005)
box("Brush clip back pad", (.11, .245, .099), (.100, .050, .022), "dark", .004)
for x in (.065, .155):
    box("Brush clip jaw", (x, .245, .055), (.020, .05, .06), "dark", .003)
    cylinder("Clip fixing screw", (x, .245, .021), .005, .004, "steel", "Z", 8)
for x in (-.23, .23):
    cylinder("Rack mounting screw", (x, .035, -.09), .008, .003, "steel", vertices=8)
rack = join_asset("ToolRackModel")

assets = [screwdriver, brush, iron, station, rack]
report = {"units": "metres", "source_coordinates": "Unity X,Y,Z", "meshes": []}
for obj in assets:
    assert obj.location.length < 1e-8, obj.name
    assert all(math.isfinite(v) for vertex in obj.data.vertices for v in vertex.co), obj.name
    assert len(obj.data.materials) and all(obj.data.materials), obj.name
    obj.data.calc_loop_triangles()
    vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    coords = [(-v.x, v.z, -v.y) for v in vertices]
    lower = [min(v[i] for v in coords) for i in range(3)]
    upper = [max(v[i] for v in coords) for i in range(3)]
    report["meshes"].append({"name": obj.name, "bounds_min": lower,
        "bounds_max": upper, "triangles": len(obj.data.loop_triangles),
        "materials": [m.name for m in obj.data.materials]})
report["total_triangles"] = sum(m["triangles"] for m in report["meshes"])
assert report["total_triangles"] < 15000, report["total_triangles"]
(OUT / "tools-model-report.json").write_text(json.dumps(report, indent=2))

bpy.ops.object.select_all(action="DESELECT")
for obj in assets:
    obj.select_set(True)
bpy.context.view_layer.objects.active = screwdriver
bpy.ops.export_scene.fbx(filepath=str(ROOT / "Assets/_Project/Art/Models/WorkbenchTools.fbx"),
    use_selection=True, object_types={"MESH"}, axis_forward="-Z", axis_up="Y",
    apply_unit_scale=True, bake_space_transform=True, use_mesh_modifiers=True,
    mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="AUTO")

# Store each asset in a named collection so zero-origin models can be edited alone.
for obj in assets:
    collection = bpy.data.collections.new(obj.name.removesuffix("Model"))
    bpy.context.scene.collection.children.link(collection)
    for current in list(obj.users_collection):
        current.objects.unlink(obj)
    collection.objects.link(obj)
    obj.hide_set(obj != station)
    obj.hide_render = obj != station
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "ArtSource/Workbench/WorkbenchTools.blend"))

# A separate disposable studio arrangement is only for checking the authored art.
for obj in assets:
    obj.hide_set(False)
    obj.hide_render = False
locations = {screwdriver: (-.45, .17, -.12), brush: (-.34, .17, -.12),
             iron: (.32, .11, -.07), station: (.03, 0, .20), rack: (-.38, .025, -.12)}
for obj, p in locations.items():
    obj.location = u(p)
station.location.x += .03
iron.rotation_euler = (math.radians(35), 0, math.radians(15))
floor = box("Preview floor", (0, -.025, .05), (2, .04, 1.6), "wood", .01)
world = bpy.context.scene.world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (.14, .18, .20, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = .7
for p, energy, size in [((-.5, 1.5, -.6), 180, 1.2), ((1, 1, .1), 120, 1), ((0, 1, 1), 150, 1)]:
    bpy.ops.object.light_add(type="AREA", location=u(p))
    light = bpy.context.object
    light.data.energy = energy
    light.data.shape = "DISK"
    light.data.size = size
    light.rotation_euler = (Vector(u((0, .10, 0))) - light.location).to_track_quat("-Z", "Y").to_euler()
bpy.ops.object.camera_add(location=u((.85, .80, -1.25)))
camera = bpy.context.object
camera.rotation_euler = (Vector(u((-.04, .12, .02))) - camera.location).to_track_quat("-Z", "Y").to_euler()
camera.data.type = "ORTHO"
camera.data.ortho_scale = 1.22
scene = bpy.context.scene
scene.camera = camera
scene.render.engine = "CYCLES"
scene.cycles.samples = 32
scene.render.resolution_x = 1400
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = str(OUT / "tools-preview.png")
bpy.ops.render.render(write_still=True)
print("WORKBENCH_TOOLS_REPORT=" + json.dumps(report))
