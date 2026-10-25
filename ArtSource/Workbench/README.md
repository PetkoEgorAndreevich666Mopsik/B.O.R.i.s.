# Workbench models

Editable Blender sources and their reproducible generators:

- `WorkbenchFurniture.blend` / `create_furniture.py`: workbench, service platform, parts and cover trays, diagnostic stand.
- `WorkbenchTools.blend` / `create_tools.py`: screwdriver, brush, soldering iron, soldering station and tool holder.
- `WorkbenchRobot.blend` / `create_robot.py`: head housing, open body frame, service cover, drive mounting assembly and arm.

Run a generator from the repository root with Blender 5.2:

```text
blender --background --python ArtSource/Workbench/create_furniture.py
blender --background --python ArtSource/Workbench/create_tools.py
blender --background --python ArtSource/Workbench/create_robot.py
```

The scripts save editable sources here and FBX files in `Assets/_Project/Art/Models/`. Geometry and bounds assertions run before export. Tools also render a temporary studio preview. Tool and robot collections isolate overlapping zero-origin assets while editing; furniture meshes can be isolated by name in Blender.

Coordinates are authored in Unity metres and converted to Blender `(-X, -Z, Y)`, including Unity FBX handedness. The exported axes are already baked: keep Unity's **Bake Axis Conversion disabled**. Head/arm, trays and stand use normalized bounds; the existing prefab transforms restore their intended dimensions. Preserve object names, material slots, bounds and working pivots when re-exporting.

The prefabs reference the FBX meshes directly. URP materials are in `Assets/_Project/Art/Materials/Workbench/`; Blender shaders and studio lights are not runtime dependencies. The meshes have no colliders. Existing handles, fasteners, cleaning meshes/UVs, solder seams, anchors and animation pivots remain the gameplay geometry.

These are prototype models with bevels, material separation and modeled details, without baked wear textures or LOD variants.
