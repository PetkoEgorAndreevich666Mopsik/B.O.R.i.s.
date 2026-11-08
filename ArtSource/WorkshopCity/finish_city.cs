// Unity Pipeline eval_file. Regenerate the authored city meshes in the saved WorkshopPrototype scene.

var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.path != "Assets/_Project/Scenes/WorkshopPrototype.unity" || scene.isDirty || EditorApplication.isPlayingOrWillChangePlaymode)
    throw new InvalidOperationException("Open the saved workshop in Edit Mode.");

var city = GameObject.Find("TowerBlockout/CityMassing").transform;
if (city.GetComponentsInChildren<Collider>(true).Length != 0 || city.Find("Street network") == null)
    throw new InvalidOperationException("Expected the workshop city and its street network.");

const string folder = "Assets/_Project/Art/Models/WorkshopCity";
if (!AssetDatabase.IsValidFolder(folder))
    AssetDatabase.CreateFolder("Assets/_Project/Art/Models", "WorkshopCity");

const string shaderPath = "Assets/_Project/Art/Shader/WorkshopCity.shader";
AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
if (shader == null || ShaderUtil.ShaderHasError(shader))
    throw new InvalidOperationException("The city shader did not import: " +
        (shader == null ? shaderPath : string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message))));
var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/City.mat");
if (material == null)
{
    material = new Material(shader);
    AssetDatabase.CreateAsset(material, folder + "/City.mat");
}

var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
var cube = template.GetComponent<MeshFilter>().sharedMesh;
var cubeVertices = cube.vertices;
var cubeNormals = cube.normals;
var cubeIndices = cube.triangles;
UnityEngine.Object.DestroyImmediate(template);
var points = new List<Vector3>();
var colors = new List<Color32>();
var triangles = new List<int>();
var random = new System.Random(267);
const float street = -75f;
const float firstFloor = -70.5f;
const float floorStep = 3.1f;
float Random01() => (float)random.NextDouble();
Color C(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
Color Shade(Color color, float value) => new Color(color.r * value, color.g * value, color.b * value, 1);
Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

void Box(Vector3 min, Vector3 max, Color color)
{
    int start = points.Count;
    for (int i = 0; i < cubeVertices.Length; i++)
    {
        Vector3 n = cubeNormals[i];
        float face = n.y > .5f ? 1.17f : n.y < -.5f ? .68f : n.x < -.5f ? 1.00f : .84f;
        points.Add(min + Vector3.Scale(cubeVertices[i] + Vector3.one * .5f, max - min));
        colors.Add(Shade(color, face));
    }
    foreach (int index in cubeIndices)
        triangles.Add(start + index);
}

void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
{
    int start = points.Count;
    points.Add(a); points.Add(b); points.Add(c); points.Add(d);
    for (int i = 0; i < 4; i++) colors.Add(color);
    triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
}

Color Window(bool office)
{
    float pick = Random01();
    if (pick < .61f) return Color.Lerp(C(0x263c56), C(0x53728c), Random01() * .68f);
    if (pick < .75f || office) return Color.Lerp(C(0x7293a8), C(0xb1cdd3), Random01());
    return Color.Lerp(C(0xb38160), C(0xf4c68a), Random01());
}

void Building(float x, float z, float width, float depth, int floors, int variant, int detail)
{
    float x1 = x + width, z1 = z + depth, top = firstFloor + floors * floorStep;
    bool office = variant == 1;
    var wall = C(variant == 0 ? 0x617383 : office ? 0x40586f : 0x776e75);
    var cap = C(0x364658);
    Box(V(x, firstFloor, z), V(x1, top, z1), wall);
    Box(V(x - 1.4f, street, z - 1.3f), V(x1 + 1.4f, firstFloor, z1 + 1.3f), Shade(wall, .75f));
    Box(V(x - .25f, top - .32f, z - .24f), V(x1 + .25f, top + .29f, z1 + .24f), cap);
    Box(V(x + width * .24f, top, z + depth * .25f), V(x + width * .68f, top + 2.2f, z + depth * .72f), cap);
    if (office || detail == 1)
    {
        Box(V(x + width * .32f, top + 2.2f, z + depth * .40f),
            V(x + width * .58f, top + 4.3f, z + depth * .63f), Shade(wall, .89f));
        Box(V(x + width * .45f, top + 4.3f, z + depth * .5f),
            V(x + width * .45f + .15f, top + 7.1f, z + depth * .5f + .15f), cap);
        Box(V(x + width * .45f - .12f, top + 7.1f, z + depth * .5f - .12f),
            V(x + width * .45f + .27f, top + 7.38f, z + depth * .5f + .27f), C(0xe58178));
    }

    int bays = detail == 0 ? 8 : detail == 1 ? 7 : 5;
    int stride = detail == 2 ? 2 : 1;
    for (int floor = 0; floor < floors; floor += stride)
    {
        float y = firstFloor + floor * floorStep + .79f;
        float height = office ? 1.9f : 1.53f;
        if (detail == 0 || (detail == 1 && (office || floor % 3 == 0)))
            Box(V(x - .08f, y - .28f, z - .08f), V(x1 + .08f, y - .13f, z1 + .08f), Shade(wall, .69f));

        for (int bay = 0; bay < bays; bay++)
        {
            float centerZ = z + depth * (bay + .5f) / bays;
            float centerX = x + width * (bay + .5f) / bays;
            float hz = depth / bays * (office ? .43f : .30f);
            float hx = width / bays * (office ? .43f : .30f);
            float face = x - .1f;
            var tint = Window(office);
            Quad(V(face,y,centerZ-hz), V(face,y,centerZ+hz), V(face,y+height,centerZ+hz), V(face,y+height,centerZ-hz), tint);
            if (detail == 0)
            {
                float trim = face - .015f;
                Quad(V(trim,y,centerZ-.038f), V(trim,y,centerZ+.038f),
                    V(trim,y+height,centerZ+.038f), V(trim,y+height,centerZ-.038f), cap);
                if (!office && bay % 3 != 0)
                    Box(V(x-.46f, y-.22f, centerZ-hz-.09f), V(x, y-.10f, centerZ+hz+.09f), Shade(wall, .9f));
            }
            Quad(V(centerX-hx,y,z-.10f), V(centerX-hx,y+height,z-.10f), V(centerX+hx,y+height,z-.10f), V(centerX+hx,y,z-.10f), Shade(Window(office), .87f));
            Quad(V(centerX+hx,y,z1+.10f), V(centerX+hx,y+height,z1+.10f), V(centerX-hx,y+height,z1+.10f), V(centerX-hx,y,z1+.10f), Shade(Window(office), .91f));
        }
    }
    if (detail == 0)
        for (int bay = 0; bay < 6; bay++)
        {
            float zz = z + (bay + .5f) * depth / 6;
            Quad(V(x-1.41f,street+.32f,zz-1.3f), V(x-1.41f,street+.32f,zz+1.3f),
                V(x-1.41f,firstFloor-.55f,zz+1.3f), V(x-1.41f,firstFloor-.55f,zz-1.3f),
                Color.Lerp(C(0x50677d), C(0xd29a62), Random01()));
        }
}

int triangleCount = 0;
var meshes = new List<Mesh>();
void Finish(string name)
{
    var mesh = new Mesh { name = name };
    if (points.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
    mesh.SetVertices(points); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
    mesh.RecalculateBounds();
    string path = folder + "/" + name + ".asset";
    var previous = AssetDatabase.LoadAssetAtPath<Mesh>(path);
    if (previous == null)
        AssetDatabase.CreateAsset(mesh, path);
    else
    {
        EditorUtility.CopySerialized(mesh, previous);
        EditorUtility.SetDirty(previous);
        UnityEngine.Object.DestroyImmediate(mesh);
        mesh = previous;
    }
    triangleCount += triangles.Count / 3;
    meshes.Add(mesh);
    points.Clear(); colors.Clear(); triangles.Clear();
}

// Reuse the U2 street positions. Box vertex colors supply the dusk palette and illuminated details.
var streetRoot = city.Find("Street network");
var streetSurfaces = streetRoot.GetComponentsInChildren<MeshRenderer>(true);
foreach (var renderer in streetSurfaces)
{
    string label = renderer.name;
    var color = C(label == "Ground" ? 0x353f4c :
        label.Contains("mark") || label.Contains("stripe") ? 0xb4a397 :
        label.Contains("sidewalk") ? 0x596675 : 0x2e3846);
    Box(renderer.bounds.min, renderer.bounds.max, color);
}
for (int i = -6; i < 7; i++)
{
    float z = i * 15f + 4f;
    if (z < -26 && z > -55) continue;
    foreach (float x in new[] { 9.45f, 32.4f })
    {
        Box(V(x-.09f,street,z-.09f), V(x+.09f,street+5.1f,z+.09f), C(0x414851));
        float end = x < 15 ? x+1.65f : x-1.65f;
        Box(V(Mathf.Min(x,end),street+4.9f,z-.13f), V(Mathf.Max(x,end),street+5.08f,z+.13f), C(0x667582));
        Box(V(end-.28f,street+4.82f,z-.24f), V(end+.28f,street+4.94f,z+.24f), C(0xf3c482));
    }
    float lane = i % 2 == 0 ? 17.1f : 25.1f;
    float zz = z + 3.5f;
    Box(V(lane-.91f,street+.15f,zz-2.0f), V(lane+.91f,street+.90f,zz+2.0f), C(i % 3 == 0 ? 0x97676d : 0x607e90));
    Box(V(lane-.72f,street+.90f,zz-.92f), V(lane+.72f,street+1.28f,zz+.73f), C(0x9aabba));
    float direction = lane < 20 ? 1 : -1;
    for (int side = -1; side <= 1; side += 2)
    {
        float xx = lane + side * .59f;
        float front = zz + direction * 2f, back = zz - direction * 2f;
        Box(V(xx-.16f,street+.50f,front-.14f), V(xx+.16f,street+.69f,front+.14f), C(0xf9dda7));
        Box(V(xx-.17f,street+.48f,back-.13f), V(xx+.17f,street+.68f,back+.13f), C(0xeb7880));
    }
}
Finish("Street");

// Separate building meshes keep whole-building frustum culling and share one material.
var buildings = new (string name, float x, float z, float width, float depth, int floors, int variant, int detail)[]
{
    ("North residential",36,16,26,27,25,0,0), ("South residential",36,-83,28,25,28,2,0),
    ("Corner block",37,-28,27,22,10,2,0), ("North low block",36,71,26,23,14,0,0),
    ("South end",94,-135,25,28,31,1,1), ("Avenue apartments",94,-80,28,26,29,0,1),
    ("Central apartments",95,4,27,24,32,2,1), ("North office",94,55,32,25,23,1,1),
    ("South office",163,-144,37,30,40,1,2), ("Stepped landmark",164,-85,33,30,47,1,2),
    ("Central block",170,-9,30,29,37,0,2), ("East residences",172,52,26,28,39,2,2),
    ("Northern office",166,108,32,31,31,1,2), ("Distant office south",256,-110,43,31,52,1,2),
    ("Distant tower",247,-22,32,31,45,0,2), ("Distant office north",264,59,36,32,50,1,2),
    ("Outer skyline south",369,-192,48,38,41,1,2), ("Outer skyline center",352,-41,43,44,57,0,2),
    ("Outer skyline north",397,122,46,40,46,1,2), ("Outer skyline gap",330,-100,39,43,41,0,2),
    ("Relay tower",415,-87,40,42,55,1,2)
};
foreach (var building in buildings)
{
    Building(building.x, building.z, building.width, building.depth, building.floors, building.variant, building.detail);
    Finish(building.name);
}

Undo.IncrementCurrentGroup();
int undo = Undo.GetCurrentGroup();
try
{
    // Retain editable street primitives as the input for regenerating the street batch.
    Undo.RecordObject(streetRoot.gameObject, "Archive street construction");
    streetRoot.gameObject.SetActive(false);
    foreach (var group in city.Cast<Transform>().Where(t => t != streetRoot).ToArray())
        Undo.DestroyObjectImmediate(group.gameObject);
    foreach (var mesh in meshes)
    {
        var item = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
        Undo.RegisterCreatedObjectUndo(item, "Install city batch");
        item.transform.SetParent(city, false);
        item.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = item.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }
    if (city.GetComponentsInChildren<Renderer>().Length != meshes.Count)
        throw new InvalidOperationException("Unexpected city renderer count.");
    AssetDatabase.SaveAssets();
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene))
        throw new InvalidOperationException("Failed to save workshop city.");
    Undo.CollapseUndoOperations(undo);
    return $"Built {buildings.Length} buildings, {meshes.Count} active renderers and {triangleCount} triangles.";
}
catch
{
    Undo.RevertAllDownToGroup(undo);
    throw;
}
