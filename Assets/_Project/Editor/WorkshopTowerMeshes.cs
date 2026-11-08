using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

static class WorkshopTowerMeshes
{
    const string Models = "Assets/_Project/Art/Models";
    const string Folder = Models + "/WorkshopTower";
    const float Width = 26f;
    const float Depth = 20f;
    const float FloorStep = 2.95f;
    const float TopFloorStep = 3.30f;
    const float BalconyDepth = 2f;
    const float DeckThickness = .27f;
    const float GuardHeight = 1.15f;

    static readonly float[] FrontBays = { -10.75f, -6.25f, 0f, 6.25f, 10.75f };

    enum Surface { Slab, Panel, Metal, Window }

    [MenuItem("BORis/Rebuild Workshop Tower Meshes")]
    public static void Rebuild()
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        if (cube == null || !cube.isReadable)
            throw new InvalidOperationException("The readable Unity cube mesh is required to bake the tower.");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder(Models, "WorkshopTower");

        Save(Lobby(cube), "Lobby");
        Save(Tier(cube, FloorStep, false), "TypicalFloor");
        Save(Tier(cube, TopFloorStep, true), "WorkshopFloor");
        Save(Tier(cube, TopFloorStep, false), "UpperFloor");
        Save(Roof(cube), "Roof");
        AssetDatabase.SaveAssets();
    }

    static void Save(Mesh mesh, string name)
    {
        mesh.name = name;
        var path = Folder + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return;
        }
        EditorUtility.CopySerialized(mesh, existing);
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    static Mesh Lobby(Mesh cube)
    {
        const float height = 4.20f;
        var tier = new Geometry(cube);
        Walls(tier, 0, height - .25f, .70f, 2.60f);
        tier.Box(Surface.Slab, -.26f, 0, -13, 0, .19f, 13);
        tier.Box(Surface.Panel, -.26f, 3.35f, -13, 0, height - .25f, 13);
        FrontPiers(tier, .19f, 3.35f);
        foreach (float center in FrontBays)
            FrontWindow(tier, center, center == 0 ? 7.72f : 4.22f, .19f, 3.35f);
        return tier.Bake();
    }

    static Mesh Tier(Mesh cube, float height, bool hasWorkshop)
    {
        const float sill = .20f;
        float head = height - .65f;
        var tier = new Geometry(cube);
        Walls(tier, -.25f, height - .25f, .72f, head - .08f);
        FrontPiers(tier, sill, head);
        tier.Box(Surface.Panel, -.26f, head, -13, 0, height - .25f, 13);
        foreach (float center in FrontBays)
        {
            bool central = center == 0;
            float width = central ? 6f : 3.5f;
            Balcony(tier, center, width);

            // The central opening and floor on level 26 are the existing playable scene.
            if (central && hasWorkshop)
                continue;

            float halfBay = central ? 4f : 2.25f;
            float start = center - halfBay;
            float end = center + halfBay;
            float left = center - width * .5f;
            float right = center + width * .5f;
            tier.Box(Surface.Slab, -.26f, -.25f, start, 0, sill, end);
            tier.Box(Surface.Panel, -.23f, sill, start + .14f, -.03f, head, left);
            tier.Box(Surface.Panel, -.23f, sill, right, -.03f, head, end - .14f);
            FrontWindow(tier, center, width, sill, head);
        }
        return tier.Bake();
    }

    static void FrontPiers(Geometry tier, float bottom, float top)
    {
        foreach (float edge in new[] { -13f, -8.5f, -4f, 4f, 8.5f, 13f })
        {
            tier.Box(Surface.Metal, -.27f, bottom, Mathf.Max(-13, edge - .14f),
                0, top, Mathf.Min(13, edge + .14f));
        }
    }

    static void FrontWindow(Geometry tier, float center, float width, float bottom, float top)
    {
        float left = center - width * .5f;
        float right = center + width * .5f;
        tier.Box(Surface.Window, -.19f, bottom, left, -.14f, top, right);
        int panes = center == 0 ? 4 : 2;
        for (int i = 0; i <= panes; i++)
        {
            float z = Mathf.Clamp(left + width * i / panes, left + .03f, right - .03f);
            tier.Box(Surface.Metal, -.14f, bottom, z - .03f, -.06f, top, z + .03f);
        }
    }

    static void Balcony(Geometry tier, float center, float width)
    {
        float left = center - width * .5f;
        float right = center + width * .5f;
        tier.Box(Surface.Slab, 0, -DeckThickness, left, BalconyDepth, 0, right);

        const float edge = .065f;
        const float bar = .03f;
        const float cap = .045f;
        float front = BalconyDepth - edge;
        float railLeft = left + edge;
        float railRight = right - edge;
        tier.Box(Surface.Metal, front - cap, GuardHeight - cap, railLeft - cap,
            front + cap, GuardHeight, railRight + cap);
        int frontSpans = Mathf.CeilToInt((railRight - railLeft) / .28f);
        for (int i = 0; i <= frontSpans; i++)
        {
            float z = Mathf.Lerp(railLeft, railRight, (float)i / frontSpans);
            tier.Box(Surface.Metal, front - bar, 0, z - bar, front + bar, GuardHeight - cap, z + bar);
        }
        foreach (float z in new[] { railLeft, railRight })
        {
            tier.Box(Surface.Metal, 0, GuardHeight - cap, z - cap, front - cap, GuardHeight, z + cap);
            int sideSpans = Mathf.CeilToInt(front / .28f);
            for (int i = 0; i < sideSpans; i++)
            {
                float x = Mathf.Lerp(edge, front, (float)i / sideSpans);
                tier.Box(Surface.Metal, x - bar, 0, z - bar, x + bar, GuardHeight - cap, z + bar);
            }
        }
    }

    static void Walls(Geometry tier, float bottom, float top, float windowBottom, float windowTop)
    {
        float halfWidth = Width * .5f;
        tier.Box(Surface.Panel, -Depth, bottom, -halfWidth, -Depth + .20f, top, halfWidth);
        tier.Box(Surface.Panel, -Depth + .20f, bottom, -halfWidth + .035f, -.26f, top, -halfWidth + .20f);
        tier.Box(Surface.Panel, -Depth + .20f, bottom, halfWidth - .20f, -.26f, top, halfWidth - .035f);
        foreach (float side in new[] { -halfWidth, halfWidth - .035f })
        {
            tier.Box(Surface.Window, -11.9f, windowBottom, side, -8.1f, windowTop, side + .035f);
            tier.Box(Surface.Metal, -10.04f, windowBottom, side - .005f, -9.96f, windowTop, side + .04f);
        }
        foreach (float z in new[] { -6.5f, 6.5f })
            tier.Box(Surface.Window, -Depth - .025f, windowBottom, z - 1.55f, -Depth, windowTop, z + 1.55f);
    }

    static Mesh Roof(Mesh cube)
    {
        var roof = new Geometry(cube);
        roof.Box(Surface.Slab, -Depth, -.25f, -13, 0, 0, 13);
        roof.Box(Surface.Slab, -Depth, 0, -13, -Depth + .22f, .60f, 13);
        roof.Box(Surface.Slab, -.22f, 0, -13, 0, .60f, 13);
        roof.Box(Surface.Slab, -Depth + .22f, 0, -13, -.22f, .60f, -12.78f);
        roof.Box(Surface.Slab, -Depth + .22f, 0, 12.78f, -.22f, .60f, 13);
        return roof.Bake();
    }

    sealed class Geometry
    {
        readonly Mesh cube;
        readonly List<CombineInstance>[] surfaces = {
            new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>(),
            new List<CombineInstance>()
        };

        public Geometry(Mesh cube) => this.cube = cube;

        public void Box(Surface surface, float x0, float y0, float z0, float x1, float y1, float z1)
        {
            var size = new Vector3(x1 - x0, y1 - y0, z1 - z0);
            if (size.x <= 0 || size.y <= 0 || size.z <= 0)
                throw new ArgumentException("Tower boxes must have positive dimensions.");
            var center = new Vector3(x0, y0, z0) + size * .5f;
            surfaces[(int)surface].Add(new CombineInstance {
                mesh = cube,
                transform = Matrix4x4.TRS(center, Quaternion.identity, size)
            });
        }

        public Mesh Bake()
        {
            var parts = new List<CombineInstance>();
            foreach (var surface in surfaces)
            {
                if (surface.Count == 0)
                    continue;
                var part = new Mesh();
                part.CombineMeshes(surface.ToArray());
                parts.Add(new CombineInstance { mesh = part });
            }
            var mesh = new Mesh();
            mesh.CombineMeshes(parts.ToArray(), false, false);
            foreach (var part in parts)
                UnityEngine.Object.DestroyImmediate(part.mesh);
            return mesh;
        }
    }
}
