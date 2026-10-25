# Workbench models

Editable Blender sources and their reproducible generators:

- `WorkbenchFurniture.blend` / `create_furniture.py`: workbench, service platform, parts and cover trays, diagnostic stand.
- `WorkbenchTools.blend` / `create_tools.py`: screwdriver, brush, soldering iron, soldering station and tool holder.
- `WorkbenchRobot.blend` / `create_robot.py`: original robot meshes, retained as the baseline and backup.
- `RobotRefinedFinal.blend` / `refine_robot.py`: refinement of that baseline with a recessed face, antenna, shoulder bearing, wrist and gripper, rear service hatch, material separation and selective edge wear.

Run a generator from the repository root with Blender 5.2:

```text
blender --background --python ArtSource/Workbench/create_furniture.py
blender --background --python ArtSource/Workbench/create_tools.py
blender --background --python ArtSource/Workbench/refine_robot.py
```

The scripts save editable sources here and FBX files in `Assets/_Project/Art/Models/`. Geometry and bounds assertions run before export. Tools also render a temporary studio preview. Tool and robot collections isolate overlapping zero-origin assets while editing; furniture meshes can be isolated by name in Blender.

The refined robot is the current Unity version. `create_robot.py` recreates the **old** baseline and overwrites the same FBX; run `refine_robot.py` afterwards if the baseline is regenerated. Rebuild in a fresh Blender process so canonical FBX names are available; open `RobotRefinedFinal.blend` for manual edits. The refiner duplicates the five existing meshes into a separate scene, keeps their originals in a hidden backup collection, and preserves unrelated scene objects. `robot_placement.json` records the original Unity assembly transforms so the Blender file displays the assembled robot while its FBX retains the existing part origins. Export only the six functional meshes; hardware, platform and studio objects are previews.

Coordinates are authored in Unity metres and converted to Blender `(-X, -Z, Y)`, including Unity FBX handedness. The exported axes are already baked: keep Unity's **Bake Axis Conversion disabled**. Head/arm, trays and stand use normalized bounds; the existing prefab transforms restore their intended dimensions. Preserve object names, material slots, bounds and working pivots when re-exporting.

For manual edits in the assembled refined file, export only the six functional objects after temporarily resetting their object transforms to identity **without applying transforms to mesh data**. Bake the export axes (`-Z` forward, `Y` up), then undo the reset to restore the assembled view. Exporting their displayed assembly transforms would apply Unity's placement twice.

The prefabs reference the FBX meshes directly. Furniture/tool URP materials are in `Assets/_Project/Art/Materials/Workbench/`; the robot uses nine independent `BR_*` materials in `Assets/_Project/Art/Materials/Robot/`. Blender shaders and studio lights are not runtime dependencies. Preserve the cyan eye material's `_EMISSION` keyword and emissive lightmap flag when validating URP materials.

The refined robot has 44,220 triangles across six functional mesh objects. Do not refit the head/arm wrapper transforms to the new bounds: their antenna and gripper intentionally extend the original silhouette. `RobotShoulderModel` is an identity-transform child of the existing `ShoulderPivot`. Legacy face/neck/cuff renderers are disabled, while their objects and every gameplay component remain. All six order variants inherit the shared base prefab. The new meshes have no colliders. Existing handles, fasteners, cleaning meshes/UVs, solder seams and animation pivots remain the gameplay geometry.

The second iteration seats the cover on a 1.5 mm gasket and supports its released position with four alignment tongues. The cartridge docks on two rear studs and a lower shelf, with a fixed enclosed transmission to the shoulder. Its two terminal blades move with the cartridge and disengage from the sliding plug. The socket carrier, service board floor bracket and clipped power harness stay on the chassis when the drive is removed.

The yellow `TopRim`/`BottomRim`/`LeftRim`/`RightRim` renderers under `CoverSocket` and `DriveSocket` are disabled. Their parent transforms and placement colliders remain enabled so installing parts still works.

The installation poses below are in robot-local metres. Keep each installed part aligned to its matching anchor; do not move spare parts in their trays. MaintenanceRobot has its own installed WorkingDrive position override. SolderJoint is raised 20 mm above its former position so its board clears the chassis floor, with 5 mm below the drive.

| Root | X | Y | Z |
|---|---:|---:|---:|
| Cover / CoverSocket | 0 | 1.523 | -0.1315 |
| Installed drive / DriveSocket | -0.085 | 1.508 | 0.030 |
| Connector | 0.235 | 1.493 | 0.020 |
| SolderJoint | -0.060 | 1.293 | -0.005 |

These are prototype models with bevels, material separation and modeled details, without baked wear textures or LOD variants. The gripper is a visual part of the existing animated arm; its jaws do not have a separate closing animation.
