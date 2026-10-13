using UnityEngine;

namespace BORis
{
    [RequireComponent(typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class DirtPatch : MonoBehaviour
    {
        const int Resolution = 64;
        [SerializeField] string displayName = "Грязный участок";
        [SerializeField] bool requiresOpenCover;
        Texture2D mask;
        Material material;
        Color32[] pixels;
        int initialDirty, remainingDirty;

        public string DisplayName => displayName;
        public bool RequiresOpenCover => requiresOpenCover;
        public float Cleanliness => initialDirty == 0 ? 1f : 1f - (float)remainingDirty / initialDirty;
        public bool IsClean => Cleanliness >= .9f;
        public Texture2D Mask => mask;

        void Awake()
        {
            pixels = new Color32[Resolution * Resolution];
            for (int y = 0; y < Resolution; y++)
                for (int x = 0; x < Resolution; x++)
                {
                    float nx = (x + .5f) / Resolution * 2f - 1f;
                    float ny = (y + .5f) / Resolution * 2f - 1f;
                    int grain = (x * 37 + y * 71 + x * y * 13) % 29;
                    bool dirty = nx * nx + ny * ny < .75f + grain * .008f;
                    byte shade = (byte)(90 + grain * 2);
                    pixels[y * Resolution + x] = new Color32(shade, (byte)(shade * .72f), (byte)(shade * .4f), dirty ? (byte)255 : (byte)0);
                    if (dirty) initialDirty++;
                }
            remainingDirty = initialDirty;
            mask = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false) { name = name + " Dirt Mask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            mask.SetPixels32(pixels);
            mask.Apply(false);
            var renderer = GetComponent<MeshRenderer>();
            material = new Material(renderer.sharedMaterial);
            material.SetTexture("_DirtMask", mask);
            renderer.sharedMaterial = material;
        }

        public int CleanStroke(Vector2 from, Vector2 to, float radius)
        {
            if (!isActiveAndEnabled || !ValidUV(from) || !ValidUV(to) || !float.IsFinite(radius) || radius <= 0f) return 0;
            // shortcut: patches are planar XY meshes; use a surface-space brush when supporting curved robots.
            Vector2 size = new(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
            if (size.x <= 0f || size.y <= 0f) return 0;
            Vector2 start = Vector2.Scale(from, size), end = Vector2.Scale(to, size);
            Vector2 segment = end - start;
            if (segment.sqrMagnitude < .00000025f) return 0;
            int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(from.x, to.x) - radius / size.x) * Resolution));
            int maxX = Mathf.Min(Resolution - 1, Mathf.CeilToInt((Mathf.Max(from.x, to.x) + radius / size.x) * Resolution));
            int minY = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(from.y, to.y) - radius / size.y) * Resolution));
            int maxY = Mathf.Min(Resolution - 1, Mathf.CeilToInt((Mathf.Max(from.y, to.y) + radius / size.y) * Resolution));
            int cleaned = 0;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int index = y * Resolution + x;
                    if (pixels[index].a == 0) continue;
                    Vector2 cell = Vector2.Scale(new Vector2((x + .5f) / Resolution, (y + .5f) / Resolution), size);
                    float along = Mathf.Clamp01(Vector2.Dot(cell - start, segment) / segment.sqrMagnitude);
                    if ((cell - start - segment * along).sqrMagnitude > radius * radius) continue;
                    pixels[index].a = 0;
                    cleaned++;
                }
            if (cleaned == 0) return 0;
            remainingDirty -= cleaned;
            mask.SetPixels32(pixels);
            mask.Apply(false);
            return cleaned;
        }

        static bool ValidUV(Vector2 uv) => float.IsFinite(uv.x) && float.IsFinite(uv.y)
            && uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (mask != null) Destroy(mask);
        }
    }
}
