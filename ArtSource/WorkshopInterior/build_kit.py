"""Build the metre-scale workshop interior kit for Unity. Run in a fresh Blender session."""

import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Assets/_Project/Art"
MODEL_PATH = ASSETS / "Models/WorkshopInteriorKit.fbx"
TEXTURES = ASSETS / "Textures/WorkshopInterior"
PALETTE = {
    "WI_Concrete": ("B6B0A3", 0.0, .74),
    "WI_Paver": ("737A7A", 0.0, .83),
    "WI_Frame": ("303C43", .34, .51),
    "WI_Steel": ("788789", .50, .46),
    "WI_Enamel": ("35676B", .10, .59),
    "WI_Inset": ("253036", .04, .78),
    "WI_Timber": ("AB7B54", 0.0, .60),
    "WI_Amber": ("D7A45D", .05, .54),
    "WI_Lens": ("FFF1D0", 0.0, .32),
    "WI_Fabric": ("6E786F", 0.0, .91),
    "WI_Leaf": ("637B4C", 0.0, .77),
}
materials = {}
models = []


def xyz(point):
    # The Unity importer bakes the FBX axis conversion into the mesh.
    return (point[0], point[2], point[1])


def linear(byte):
    value = byte / 255
    return value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4


def setup():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1
    bpy.context.preferences.filepaths.save_version = 0
    for name, (rgb, metallic, roughness) in PALETTE.items():
        color = tuple(linear(int(rgb[i:i + 2], 16)) for i in (0, 2, 4)) + (1,)
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = color
        shader = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        shader.inputs["Base Color"].default_value = color
        shader.inputs["Metallic"].default_value = metallic
        shader.inputs["Roughness"].default_value = roughness
        materials[name] = mat


def finish(obj, name, material, bevel):
    obj.name = name
    obj.data.materials.append(materials["WI_" + material])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        edge = obj.modifiers.new("Edge radius", "BEVEL")
        edge.width = bevel
        edge.segments = 3 if bevel >= .035 else 2
        edge.harden_normals = True
        bpy.ops.object.modifier_apply(modifier=edge.name)
    normals = obj.modifiers.new("Weighted corners", "WEIGHTED_NORMAL")
    normals.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=normals.name)
    return obj


def box(name, position, size, material, bevel=.008):
    bpy.ops.mesh.primitive_cube_add(size=1, location=xyz(position))
    obj = bpy.context.object
    obj.dimensions = (size[0], size[2], size[1])
    return finish(obj, name, material, min(bevel, min(size) * .30))


def cone(name, position, radius_bottom, radius_top, length, material, axis="Y", sides=24):
    bpy.ops.mesh.primitive_cone_add(vertices=sides, radius1=radius_bottom, radius2=radius_top,
                                   depth=length, location=xyz(position))
    obj = bpy.context.object
    if axis == "Z":
        obj.rotation_euler.x = math.pi / 2
    bevel = min(.008, length * .13, radius_bottom * .25, radius_top * .25)
    return finish(obj, name, material, bevel if length >= .018 else 0)


def tube(name, start, end, radius, material, sides=12):
    a, b = Vector(xyz(start)), Vector(xyz(end))
    delta = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=delta.length, location=(a+b)*.5)
    obj = bpy.context.object
    obj.rotation_euler = delta.to_track_quat("Z", "Y").to_euler()
    return finish(obj, name, material, min(.007, delta.length * .12, radius * .25))


def module(name, make_parts):
    previous = set(bpy.context.scene.objects)
    make_parts()
    pieces = sorted(set(bpy.context.scene.objects) - previous, key=lambda obj: obj.name)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in pieces:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    if len(pieces) > 1:
        bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    obj.data.validate(verbose=True)
    obj.data.calc_loop_triangles()
    models.append(obj)


def cabinet():
    box("Steel carcass", (0, .45, .022), (1.016, .73, .335), "Frame")
    for x in (-.425, .425):
        for z in (-.115, .115):
            box("Leveling foot", (x, .047, z), (.095, .094, .09), "Inset", .014)
    for i, y in enumerate((.245, .456, .667)):
        box("Drawer front", (0, y, -.163), (.976, .194, .045), "Enamel", .012)
        box("Recessed handle back", (0, y+.023, -.187), (.202, .052, .016), "Inset")
        box("Pull rail", (0, y+.037, -.204), (.168, .018, .024), "Steel", .006)
        if i == 0:
            box("Inventory tag", (.36, y+.016, -.187), (.118, .040, .009), "Amber", .003)
    box("Timber worktop", (0, .839, 0), (1.052, .048, .404), "Timber", .012)


def parts_rack():
    box("Shelf back", (0, .214, .117), (1.020, .424, .026), "Frame")
    for x in (-.493, .493):
        box("Shelf end", (x, .215, 0), (.038, .43, .245), "Frame")
    for y in (.015, .216, .413):
        box("Shelf deck", (0, y, -.006), (.952, .032, .246), "Frame")
    for row in range(2):
        for i in range(5):
            x = (i-2)*.189
            y = .104 + row*.200
            mat = "Amber" if (row, i) in {(0, 0), (1, 3)} else "Enamel"
            box("Parts bin", (x, y, -.020), (.175, .149, .203), mat, .013)
            box("Bin grip", (x, y+.014, -.122), (.107, .035, .020), "Inset", .005)
            box("Bin tag", (x, y-.029, -.125), (.063, .023, .009), "Concrete", .003)


def pegboard():
    box("Punched steel back", (0, .375, .015), (1.66, .75, .041), "Steel", .012)
    for i in range(19):
        for row in range(8):
            cone("Perforation mark", ((i-9)*.084, .082+row*.083, -.007), .007, .007, .006,
                 "Inset", axis="Z", sides=8)
    for x, length in ((-.47, .26), (-.26, .32), (.42, .28)):
        box("Fixed tool hook", (x, .617, -.029), (.056, .042, .055), "Inset")
        box("Tool shank", (x, .592-length*.43, -.053), (.024, length, .020), "Frame")
        box("Tool grip", (x, .535-length*.55, -.052), (.047, .112, .036), "Amber", .013)
    for x in (-.38, .03, .36):
        box("Small component cup", (x, .056, -.064), (.205, .091, .137), "Enamel", .012)
        box("Cup lip", (x, .087, -.135), (.198, .018, .018), "Frame", .005)


def extractor():
    box("Fan housing", (0, .102, -.062), (.378, .333, .192), "Enamel", .020)
    box("Intake grille recess", (0, .143, -.159), (.258, .138, .016), "Inset", .009)
    for y in (.096, .124, .152, .18):
        box("Fan louver", (0, y, -.171), (.229, .009, .017), "Steel", .003)
    route = [
        (0, -.002, -.15), (0, -.035, -.29), (0, -.068, -.46),
        (0, -.14, -.62), (0, -.24, -.72), (0, -.34, -.73)
    ]
    corrugated_hose(route)
    cone("Spun hood", (0, -.394, -.73), .146, .068, .123, "Frame")
    cone("Rolled intake rim", (0, -.457, -.73), .147, .147, .016, "Steel")
    cone("Recessed intake", (0, -.463, -.73), .130, .130, .009, "Inset")


def corrugated_hose(route):
    controls = [Vector(xyz(point)) for point in route]
    rings = [a.lerp(b, i/12) for a, b in zip(controls, controls[1:]) for i in range(12)]
    rings.append(controls[-1])
    side = Vector((1, 0, 0))
    sides = 12
    vertices = []
    for i, center in enumerate(rings):
        tangent = (rings[min(i+1,len(rings)-1)] - rings[max(0,i-1)]).normalized()
        normal = tangent.cross(side)
        radius = .061 + .005*math.cos(i*2*math.pi/3)
        for j in range(sides):
            angle = j*2*math.pi/sides
            vertices.append(center + radius*(math.cos(angle)*side + math.sin(angle)*normal))
    faces, uvs = [], []
    for i in range(len(rings)-1):
        for j in range(sides):
            k = (j+1)%sides
            faces.append((i*sides+j, i*sides+k, (i+1)*sides+k, (i+1)*sides+j))
            u0, u1 = j/sides, (j+1)/sides
            v0, v1 = i/(len(rings)-1), (i+1)/(len(rings)-1)
            uvs.append(((u0,v0),(u1,v0),(u1,v1),(u0,v1)))
    start = tuple(reversed(range(sides)))
    end = tuple(range((len(rings)-1)*sides,len(rings)*sides))
    for face in (start,end):
        faces.append(face)
        uvs.append(tuple((.5+.48*math.cos(j*2*math.pi/sides),.5+.48*math.sin(j*2*math.pi/sides)) for j in face))
    mesh = bpy.data.meshes.new("Corrugated extraction hose")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    uv = mesh.uv_layers.new()
    for face, corners in zip(mesh.polygons,uvs):
        for index, coord in zip(face.loop_indices,corners):
            uv.uv[index].vector = coord
    obj = bpy.data.objects.new("Corrugated extraction hose", mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(materials["WI_Frame"])


def pendant():
    box("Luminaire case", (0, .056, 0), (1.66, .117, .338), "Frame", .022)
    for z in (-.075, .075):
        box("Linear diffuser", (0, -.005, z), (1.492, .021, .094), "Lens", .008)
    for x in (-.58, .58):
        cone("Stem", (x, .243, 0), .017, .017, .292, "Frame")
        box("Ceiling mounting plate", (x, .387, 0), (.100, .026, .128), "Frame")


def power_strip():
    box("Distribution strip", (0, .045, .004), (.952, .097, .082), "Frame")
    for x in (-.33, -.12, .09, .30):
        box("Socket face", (x, .045, -.044), (.102, .062, .017), "Steel", .005)
        for dx in (-.018, .018):
            cone("Recessed outlet", (x+dx, .045, -.055), .008, .008, .009, "Inset", "Z", 12)
    box("Supply indicator", (.429, .045, -.042), (.031, .030, .018), "Amber", .003)


def sofa():
    for x in (-.60, .60):
        for z in (-.235, .235):
            box("Sofa foot", (x, .067, z), (.080, .135, .085), "Frame", .017)
    box("Upholstered base", (0, .211, .007), (1.47, .192, .690), "Fabric", .045)
    box("Back shell", (0, .426, .261), (1.454, .466, .197), "Fabric", .063)
    for x in (-.662, .662):
        box("Rolled arm", (x, .358, -.027), (.162, .302, .650), "Fabric", .060)
    for x in (-.287, .287):
        box("Seat cushion", (x, .355, -.040), (.573, .136, .523), "Fabric", .055)
        box("Back cushion", (x, .494, .167), (.564, .296, .154), "Fabric", .053)
    pillow = box("Ochre throw cushion", (.451, .477, .081), (.280, .247, .119), "Amber", .041)
    pillow.rotation_euler.y = -.16


def floor_lamp():
    cone("Weighted lamp foot", (0, .027, 0), .161, .126, .054, "Frame")
    cone("Lamp pole", (0, .665, 0), .016, .016, 1.262, "Steel")
    cone("Spun lamp shade", (0, 1.331, 0), .172, .093, .217, "Amber")
    cone("Lamp diffuser", (0, 1.227, 0), .152, .152, .018, "Lens")
    cone("Shade finial", (0, 1.463, 0), .037, .030, .058, "Frame")


def tea_console():
    box("Cupboard shell", (0, .422, .017), (.662, .737, .335), "Frame")
    box("Inset door", (0, .399, -.156), (.592, .635, .042), "Enamel", .013)
    for x in (-.204, -.147, -.090, -.033, .024, .081, .138, .195):
        box("Vertical door rib", (x, .396, -.181), (.019, .540, .016), "Frame", .005)
    box("Cupboard pull", (.229, .575, -.195), (.080, .025, .030), "Steel", .006)
    box("Countertop", (0, .827, 0), (.686, .045, .381), "Timber", .014)
    box("Recessed plinth", (0, .038, .045), (.605, .076, .242), "Inset")
    cone("Kettle", (-.145, .923, .016), .090, .076, .148, "Concrete")
    cone("Kettle lid", (-.145, 1.004, .016), .078, .066, .022, "Steel")
    cone("Lid knob", (-.145, 1.024, .016), .024, .020, .022, "Frame")
    tube("Kettle spout", (-.150, .913, -.048), (-.151, .990, -.125), .024, "Concrete")
    tube("Kettle handle outer", (-.073, .876, .027), (-.042, .954, .042), .017, "Frame")
    tube("Kettle handle top", (-.042, .954, .042), (-.082, .982, .035), .016, "Frame")
    for x,z in ((.092,-.040), (.225,.062)):
        cone("Tea mug", (x, .894, z), .042, .046, .094, "Amber" if x < .15 else "Concrete")
        cone("Mug inset", (x, .942, z), .038, .038, .005, "Inset")
        tube("Mug grip", (x+.038, .873, z), (x+.063, .921, z), .010, "Concrete")


def crate():
    box("Transit crate shell", (0, .161, 0), (.555, .320, .405), "Enamel", .021)
    box("Rimmed transit lid", (0, .331, 0), (.575, .044, .419), "Frame", .012)
    for x in (-.212, .212):
        box("Pressed rib", (x, .163, -.205), (.047, .245, .023), "Frame", .007)
        box("Over-center catch", (x, .284, -.217), (.045, .062, .025), "Steel", .006)
    box("Carrier label", (.067, .192, -.205), (.139, .068, .014), "Amber", .004)
    box("Carrier handle", (-.076, .191, -.209), (.095, .029, .019), "Inset", .004)


def planter():
    box("Powder-coated planter", (0, .166, 0), (.365, .332, .358), "Paver", .025)
    box("Planter lip", (0, .321, 0), (.377, .037, .369), "Frame", .012)
    box("Soil surface", (0, .340, 0), (.322, .011, .317), "Inset", .008)
    for angle, y in ((.3,.57), (1.65,.64), (2.7,.52), (4.06,.69), (5.25,.59)):
        vx, vz = math.cos(angle), math.sin(angle)
        start = (vx*.028, .345, vz*.027)
        tip = (vx*.171, y, vz*.171)
        tube("Leaf stem", start, tip, .009, "Leaf")
        a, b = Vector(xyz(start)), Vector(xyz(tip))
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1, location=a.lerp(b, .77))
        leaf = bpy.context.object
        leaf.scale = (.054, .013, .109)
        leaf.rotation_euler = (b-a).to_track_quat("Z", "Y").to_euler()
        finish(leaf, "Wax leaf", "Leaf", 0)


def write_texture(name, pixels):
    height, width, _ = pixels.shape
    image = bpy.data.images.new(name, width, height, alpha=True)
    image.pixels.foreach_set(pixels.astype(np.float32).ravel())
    image.file_format = "PNG"
    image.filepath_raw = str(TEXTURES / (name + ".png"))
    image.save()


def textures():
    TEXTURES.mkdir(exist_ok=True)
    grain = np.random.default_rng(26).normal(0, 1, (512,512))
    u, v = np.meshgrid(np.fft.fftfreq(512), np.fft.fftfreq(512))
    spectrum = np.fft.fft2(grain)
    mineral_color = .79 + .012*grain
    for radius, contrast in ((3.6,.010), (14,.014), (47,.018)):
        kernel = np.exp(-2*np.pi**2*radius**2*(u*u+v*v))
        aggregate = np.fft.ifft2(spectrum*kernel).real
        mineral_color += contrast*aggregate/aggregate.std()
    mineral = np.ones((512,512,4))
    mineral[:,:,:3] = np.clip(mineral_color, .64, .92)[:,:,None]
    write_texture("MineralGrain", mineral)

    u = np.linspace(0, 16*np.pi, 256, endpoint=False)
    flute = np.ones((32,256,4))
    ridge = .5 + .5*np.cos(u)
    flute[:,:,:3] = (.57 + .40*ridge**3)[None,:,None]
    flute[:,:,3] = .64 + .35*ridge
    write_texture("FlutedGlass", flute)


def main():
    setup()
    for name, make_parts in (
        ("WallPanel", lambda: box("Concrete liner", (0,0,0), (1,1,.034), "Concrete", .008)),
        ("BalconyPaver", lambda: box("Chamfered paver", (0,0,0), (.644,.022,.744), "Paver", .004)),
        ("DrawerCabinet", cabinet), ("PartsRack", parts_rack), ("Pegboard", pegboard),
        ("Extractor", extractor), ("TaskPendant", pendant), ("PowerStrip", power_strip),
        ("Sofa", sofa), ("FloorLamp", floor_lamp), ("TeaConsole", tea_console),
        ("CargoCrate", crate), ("Planter", planter),
    ):
        module(name, make_parts)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in models:
        obj.select_set(True)
    result = bpy.ops.export_scene.fbx(filepath=str(MODEL_PATH), use_selection=True, object_types={"MESH"},
                                     axis_forward="-Z", axis_up="Y", global_scale=1,
                                     apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                                     bake_anim=False, use_mesh_modifiers=True)
    if result != {"FINISHED"}:
        raise RuntimeError(f"FBX export returned {result}")
    textures()

    if "--" in sys.argv:
        evidence = Path(sys.argv[sys.argv.index("--")+1])
        data = [{"name": obj.name, "triangles": len(obj.data.loop_triangles),
                 "dimensions_unity": [round(obj.dimensions[i], 4) for i in (0,2,1)],
                 "materials": [mat.name for mat in obj.data.materials]} for obj in models]
        (evidence / "kit-inventory.json").write_text(json.dumps(data, indent=2)+"\n", encoding="utf-8")
        bpy.ops.wm.save_as_mainfile(filepath=str(evidence / "workshop-interior-kit.blend"))


if __name__ == "__main__":
    main()
