using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BORis
{
    public enum DirtProjection { XY, XZ, ZY }
    public enum DirtPatternMode { Procedural, FixedTexture, RandomTexture }
    public enum DirtPatternChannel { Auto, Alpha, Luminance }

    public sealed class DirtPatch : MonoBehaviour
    {
        const int MinResolution = 64;
        static readonly int MaskId = Shader.PropertyToID("_DirtMask");
        static readonly int BoundsMinId = Shader.PropertyToID("_DirtBoundsMin");
        static readonly int BoundsSizeId = Shader.PropertyToID("_DirtBoundsSize");
        static readonly int ProjectionId = Shader.PropertyToID("_DirtProjection");
        static readonly int WorldToDirtId = Shader.PropertyToID("_DirtWorldToLocal");
        static readonly int SurfaceDirectionId = Shader.PropertyToID("_DirtSurfaceDirectionWS");
        static readonly int NormalThresholdId = Shader.PropertyToID("_DirtNormalThreshold");

        [Header("Gameplay")]
        [SerializeField, Tooltip("Название зоны в интерфейсе ремонта.")] string displayName = "Грязный участок";
        [SerializeField, Tooltip("Эту зону можно чистить только после снятия крышки.")] bool requiresOpenCover;

        [Header("Surface projection")]
        [SerializeField, Tooltip("Материал шейдера BORis/RepairDirt.")] Material overlayMaterial;
        [SerializeField, Tooltip("Видимые меши, на геометрию которых проецируется грязь.")] MeshRenderer[] surfaceRenderers;
        [SerializeField, Tooltip("Плоскость проекции: XY для передней/задней стороны, ZY для боковины, XZ для верха/низа.")] DirtProjection projection;
        [SerializeField, Tooltip("Локальный поворот проектора. В плоскости XY рисунок вращает Z, в XZ — Y, в ZY — X; остальные углы наклоняют проектор на диагональную поверхность.")] Vector3 projectionRotation;
        [SerializeField, Tooltip("Локальное направление наружной нормали загрязнённой стороны.")] Vector3 surfaceDirection = Vector3.back;
        [SerializeField, Range(.1f, .99f), Tooltip("Насколько строго нормаль полигона должна смотреть в Surface Direction.")] float normalThreshold = .72f;

        [Header("Mask shape (normalized 0..1)")]
        [SerializeField, Tooltip("Центр пятна внутри проекционной области.")] Vector2 maskCenter = new(.5f, .5f);
        [SerializeField, Tooltip("Ширина и высота пятна относительно проекционной области.")] Vector2 maskSize = new(.4f, .4f);
        [SerializeField, Tooltip("Основной цвет и прозрачность грязи.")] Color dirtColor = new(.44f, .31f, .16f, .9f);
        [SerializeField, Range(0f, .5f), Tooltip("Разброс светлых и тёмных точек внутри пятна.")] float colorVariation = .25f;
        [SerializeField, Range(0f, .3f), Tooltip("Неровность края пятна.")] float edgeNoise = .16f;
        [SerializeField, Range(64, 512), Tooltip("Разрешение runtime-маски. 256 обычно достаточно.")] int maskResolution = 256;

        [Header("Pattern source")]
        [SerializeField, Tooltip("Procedural создаёт случайное органическое пятно; FixedTexture берёт выбранную текстуру; RandomTexture случайно выбирает одну из списка.")] DirtPatternMode patternMode;
        [SerializeField, Tooltip("Набор чёрно-белых или прозрачных масок грязи.")] Texture2D[] dirtPatterns;
        [SerializeField, Min(0), Tooltip("Индекс текстуры для режима FixedTexture.")] int fixedPatternIndex;
        [SerializeField, Tooltip("Auto использует Alpha, если в нём есть прозрачность, иначе яркость изображения.")] DirtPatternChannel patternChannel;
        [SerializeField, Tooltip("Инвертировать выбранный канал маски.")] bool invertPattern;
        [SerializeField, Tooltip("0 — новый случайный вариант при каждом создании; другое число делает выбор воспроизводимым.")] int patternSeed;

        readonly List<GameObject> overlayObjects = new();
        Texture2D mask;
        Material material;
        Color32[] pixels;
        Bounds localBounds;
        Matrix4x4 projectorToWorld;
        Matrix4x4 worldToProjector;
        Vector2 surfaceWorldSize;
        int runtimeResolution;
        int initialDirty, remainingDirty;

        public string DisplayName => displayName;
        public bool RequiresOpenCover => requiresOpenCover;
        public float Cleanliness => initialDirty == 0 ? 1f : 1f - (float)remainingDirty / initialDirty;
        public bool IsClean => Cleanliness >= .9f;
        public Texture2D Mask => mask;
        public Texture2D ActivePattern { get; private set; }

        void Awake()
        {
            surfaceDirection = surfaceDirection.sqrMagnitude > .0001f ? surfaceDirection.normalized : Vector3.back;
            localBounds = GetProjectionBounds();
            BuildMask();
            BuildOverlayMaterial();
            BuildSurfaceOverlays();
            RefreshProjection();
        }

        Bounds GetProjectionBounds()
        {
            if (TryGetComponent(out BoxCollider box)) return new Bounds(box.center, box.size);
            if (TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) return filter.sharedMesh.bounds;
            return new Bounds(Vector3.zero, Vector3.one);
        }

        Bounds CenteredProjectionBounds => new(Vector3.zero, localBounds.size);

        Matrix4x4 GetProjectorLocalToWorld() => transform.localToWorldMatrix
            * Matrix4x4.TRS(localBounds.center, Quaternion.Euler(projectionRotation), Vector3.one);

        Vector3 GetProjectorNormalWorld()
        {
            Matrix4x4 projector = GetProjectorLocalToWorld();
            return projector.inverse.transpose.MultiplyVector(surfaceDirection).normalized;
        }

        void BuildMask()
        {
            runtimeResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(maskResolution), MinResolution, 512);
            pixels = new Color32[runtimeResolution * runtimeResolution];
            int seed = patternSeed != 0 ? patternSeed : System.Guid.NewGuid().GetHashCode();
            var random = new System.Random(seed);
            Texture2D selectedPattern = SelectPattern(random);
            ActivePattern = selectedPattern;
            Texture2D readablePattern = selectedPattern != null ? CreateReadablePattern(selectedPattern) : null;
            DirtPatternChannel resolvedChannel = ResolvePatternChannel(readablePattern);
            CreateProceduralBlobs(random, readablePattern == null, out Vector2[] blobCenters, out Vector2[] blobRadii);
            Vector2 halfSize = new(Mathf.Max(.001f, maskSize.x * .5f), Mathf.Max(.001f, maskSize.y * .5f));
            for (int y = 0; y < runtimeResolution; y++)
                for (int x = 0; x < runtimeResolution; x++)
                {
                    Vector2 uv = new((x + .5f) / runtimeResolution, (y + .5f) / runtimeResolution);
                    Vector2 normalized = new((uv.x - maskCenter.x) / halfSize.x, (uv.y - maskCenter.y) / halfSize.y);
                    int grain = PositiveModulo(x * 37 + y * 71 + x * y * 13 + seed, 29);
                    float grainSigned = grain / 28f * 2f - 1f;
                    float coverage = readablePattern != null
                        ? SamplePattern(readablePattern, resolvedChannel, normalized)
                        : SampleProceduralPattern(normalized, grainSigned, blobCenters, blobRadii);
                    float brightness = 1f + grainSigned * colorVariation;
                    Color sample = new(
                        Mathf.Clamp01(dirtColor.r * brightness),
                        Mathf.Clamp01(dirtColor.g * brightness),
                        Mathf.Clamp01(dirtColor.b * brightness),
                        dirtColor.a * coverage);
                    pixels[y * runtimeResolution + x] = sample;
                    if (sample.a > 1f / 255f) initialDirty++;
                }

            if (readablePattern != null) Destroy(readablePattern);

            remainingDirty = initialDirty;
            mask = new Texture2D(runtimeResolution, runtimeResolution, TextureFormat.RGBA32, false)
            {
                name = name + " Dirt Mask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            mask.SetPixels32(pixels);
            mask.Apply(false);
        }

        Texture2D SelectPattern(System.Random random)
        {
            if (patternMode == DirtPatternMode.Procedural || dirtPatterns == null || dirtPatterns.Length == 0) return null;
            var valid = new List<Texture2D>();
            foreach (Texture2D pattern in dirtPatterns)
                if (pattern != null) valid.Add(pattern);
            if (valid.Count == 0) return null;
            if (patternMode == DirtPatternMode.RandomTexture) return valid[random.Next(valid.Count)];
            return dirtPatterns[Mathf.Clamp(fixedPatternIndex, 0, dirtPatterns.Length - 1)]
                ?? valid[0];
        }

        Texture2D CreateReadablePattern(Texture2D source)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(runtimeResolution, runtimeResolution, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var readable = new Texture2D(runtimeResolution, runtimeResolution, TextureFormat.RGBA32, false, true)
            {
                name = source.name + " Runtime Copy",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            readable.ReadPixels(new Rect(0, 0, runtimeResolution, runtimeResolution), 0, 0, false);
            readable.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            return readable;
        }

        DirtPatternChannel ResolvePatternChannel(Texture2D pattern)
        {
            if (pattern == null || patternChannel != DirtPatternChannel.Auto) return patternChannel;
            Color32[] samples = pattern.GetPixels32();
            for (int i = 0; i < samples.Length; i += Mathf.Max(1, samples.Length / 256))
                if (samples[i].a < 250) return DirtPatternChannel.Alpha;
            return DirtPatternChannel.Luminance;
        }

        float SamplePattern(Texture2D pattern, DirtPatternChannel channel, Vector2 normalized)
        {
            Vector2 patternUV = normalized * .5f + Vector2.one * .5f;
            if (patternUV.x < 0f || patternUV.x > 1f || patternUV.y < 0f || patternUV.y > 1f) return 0f;
            Color sample = pattern.GetPixelBilinear(patternUV.x, patternUV.y);
            float coverage = channel == DirtPatternChannel.Luminance ? sample.grayscale : sample.a;
            return invertPattern ? 1f - coverage : coverage;
        }

        static int PositiveModulo(int value, int divisor) => (value % divisor + divisor) % divisor;

        static void CreateProceduralBlobs(System.Random random, bool create, out Vector2[] centers, out Vector2[] radii)
        {
            if (!create)
            {
                centers = null;
                radii = null;
                return;
            }

            int count = random.Next(4, 8);
            centers = new Vector2[count];
            radii = new Vector2[count];
            centers[0] = new Vector2(RandomRange(random, -.12f, .12f), RandomRange(random, -.12f, .12f));
            radii[0] = new Vector2(RandomRange(random, .62f, .92f), RandomRange(random, .55f, .88f));
            for (int i = 1; i < count; i++)
            {
                centers[i] = new Vector2(RandomRange(random, -.62f, .62f), RandomRange(random, -.62f, .62f));
                radii[i] = new Vector2(RandomRange(random, .24f, .58f), RandomRange(random, .2f, .52f));
            }
        }

        float SampleProceduralPattern(Vector2 point, float grainSigned, Vector2[] centers, Vector2[] radii)
        {
            float field = float.NegativeInfinity;
            for (int i = 0; i < centers.Length; i++)
            {
                Vector2 distance = point - centers[i];
                Vector2 normalized = new(distance.x / radii[i].x, distance.y / radii[i].y);
                field = Mathf.Max(field, 1f - normalized.sqrMagnitude);
            }
            return field + grainSigned * edgeNoise >= 0f ? 1f : 0f;
        }

        static float RandomRange(System.Random random, float minimum, float maximum) =>
            Mathf.Lerp(minimum, maximum, (float)random.NextDouble());

        void BuildOverlayMaterial()
        {
            if (overlayMaterial != null)
            {
                material = new Material(overlayMaterial);
            }
            else
            {
                Shader shader = Shader.Find("BORis/RepairDirt");
                if (shader != null) material = new Material(shader);
            }

            if (material == null)
            {
                Debug.LogError($"{name}: RepairDirt material is missing", this);
                enabled = false;
                return;
            }

            material.name = name + " Dirt Overlay";
            material.SetTexture(MaskId, mask);
            Bounds projectorBounds = CenteredProjectionBounds;
            material.SetVector(BoundsMinId, projectorBounds.min);
            material.SetVector(BoundsSizeId, projectorBounds.size);
            material.SetFloat(ProjectionId, (float)projection);
            material.SetFloat(NormalThresholdId, normalThreshold);
        }

        void BuildSurfaceOverlays()
        {
            if (material == null) return;
            if (surfaceRenderers == null || surfaceRenderers.Length == 0)
            {
                MeshRenderer ownRenderer = GetComponent<MeshRenderer>();
                surfaceRenderers = ownRenderer != null ? new[] { ownRenderer } : System.Array.Empty<MeshRenderer>();
            }

            foreach (MeshRenderer source in surfaceRenderers)
            {
                if (source == null || !source.TryGetComponent(out MeshFilter sourceFilter) || sourceFilter.sharedMesh == null) continue;

                var overlay = new GameObject(name + " Dirt Surface")
                {
                    layer = source.gameObject.layer,
                    hideFlags = HideFlags.DontSave
                };
                overlay.transform.SetParent(source.transform, false);
                overlay.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer overlayRenderer = overlay.AddComponent<MeshRenderer>();
                overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
                overlayRenderer.receiveShadows = false;
                overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
                overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                int materialCount = Mathf.Max(1, sourceFilter.sharedMesh.subMeshCount);
                var materials = new Material[materialCount];
                for (int i = 0; i < materialCount; i++) materials[i] = material;
                overlayRenderer.sharedMaterials = materials;
                overlayObjects.Add(overlay);
            }

            if (overlayObjects.Count == 0)
            {
                Debug.LogError($"{name}: DirtPatch has no valid surface renderers", this);
                enabled = false;
            }
        }

        void LateUpdate() => RefreshProjection();

        void RefreshProjection()
        {
            if (material == null) return;
            projectorToWorld = GetProjectorLocalToWorld();
            worldToProjector = projectorToWorld.inverse;
            material.SetMatrix(WorldToDirtId, worldToProjector);
            material.SetVector(SurfaceDirectionId, GetProjectorNormalWorld());

            Vector3 xAxis = projectorToWorld.MultiplyVector(Vector3.right * localBounds.size.x);
            Vector3 yAxis = projectorToWorld.MultiplyVector(Vector3.up * localBounds.size.y);
            Vector3 zAxis = projectorToWorld.MultiplyVector(Vector3.forward * localBounds.size.z);
            surfaceWorldSize = projection switch
            {
                DirtProjection.XZ => new Vector2(xAxis.magnitude, zAxis.magnitude),
                DirtProjection.ZY => new Vector2(zAxis.magnitude, yAxis.magnitude),
                _ => new Vector2(xAxis.magnitude, yAxis.magnitude)
            };
        }

        public bool AcceptsContact(Vector3 worldNormal) =>
            Vector3.Dot(GetProjectorNormalWorld(), worldNormal.normalized) >= normalThreshold;

        public int CleanStroke(Vector3 worldFrom, Vector3 worldTo, float radius)
        {
            if (!isActiveAndEnabled || !float.IsFinite(radius) || radius <= 0f) return 0;
            RefreshProjection();
            return CleanStrokeUV(WorldToMask(worldFrom), WorldToMask(worldTo), radius);
        }

        // Kept for save/progress helpers that intentionally clean a normalized mask in one stroke.
        public int CleanStroke(Vector2 from, Vector2 to, float radius)
        {
            RefreshProjection();
            return CleanStrokeUV(from, to, radius);
        }

        int CleanStrokeUV(Vector2 from, Vector2 to, float radius)
        {
            if (!isActiveAndEnabled || !ValidUV(from) || !ValidUV(to) || !float.IsFinite(radius) || radius <= 0f) return 0;
            if (surfaceWorldSize.x <= 0f || surfaceWorldSize.y <= 0f) return 0;

            Vector2 start = Vector2.Scale(from, surfaceWorldSize);
            Vector2 end = Vector2.Scale(to, surfaceWorldSize);
            Vector2 segment = end - start;
            if (segment.sqrMagnitude < .00000025f) return 0;
            int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(from.x, to.x) - radius / surfaceWorldSize.x) * runtimeResolution));
            int maxX = Mathf.Min(runtimeResolution - 1, Mathf.CeilToInt((Mathf.Max(from.x, to.x) + radius / surfaceWorldSize.x) * runtimeResolution));
            int minY = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(from.y, to.y) - radius / surfaceWorldSize.y) * runtimeResolution));
            int maxY = Mathf.Min(runtimeResolution - 1, Mathf.CeilToInt((Mathf.Max(from.y, to.y) + radius / surfaceWorldSize.y) * runtimeResolution));
            int cleaned = 0;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int index = y * runtimeResolution + x;
                    if (pixels[index].a == 0) continue;
                    Vector2 cell = Vector2.Scale(new Vector2((x + .5f) / runtimeResolution, (y + .5f) / runtimeResolution), surfaceWorldSize);
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

        Vector2 WorldToMask(Vector3 worldPoint)
        {
            Vector3 local = worldToProjector.MultiplyPoint3x4(worldPoint);
            Bounds projectorBounds = CenteredProjectionBounds;
            Vector3 size = projectorBounds.size;
            Vector3 offset = local - projectorBounds.min;
            return projection switch
            {
                DirtProjection.XZ => new Vector2(offset.x / size.x, offset.z / size.z),
                DirtProjection.ZY => new Vector2(offset.z / size.z, offset.y / size.y),
                _ => new Vector2(offset.x / size.x, offset.y / size.y)
            };
        }

        static bool ValidUV(Vector2 uv) => float.IsFinite(uv.x) && float.IsFinite(uv.y)
            && uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;

        void OnValidate()
        {
            maskCenter = new Vector2(Mathf.Clamp01(maskCenter.x), Mathf.Clamp01(maskCenter.y));
            maskSize = new Vector2(Mathf.Clamp(maskSize.x, .001f, 2f), Mathf.Clamp(maskSize.y, .001f, 2f));
            maskResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(maskResolution), MinResolution, 512);
            surfaceDirection = surfaceDirection.sqrMagnitude > .0001f ? surfaceDirection.normalized : Vector3.back;
        }

        void OnDrawGizmosSelected()
        {
            Bounds sourceBounds = GetProjectionBounds();
            Bounds bounds = new(Vector3.zero, sourceBounds.size);
            Vector3 center;
            Vector3 size;
            switch (projection)
            {
                case DirtProjection.XZ:
                    center = new Vector3(
                        bounds.min.x + bounds.size.x * maskCenter.x,
                        surfaceDirection.y >= 0f ? bounds.max.y : bounds.min.y,
                        bounds.min.z + bounds.size.z * maskCenter.y);
                    size = new Vector3(bounds.size.x * maskSize.x, Mathf.Max(bounds.size.y * .03f, .005f), bounds.size.z * maskSize.y);
                    break;
                case DirtProjection.ZY:
                    center = new Vector3(
                        surfaceDirection.x >= 0f ? bounds.max.x : bounds.min.x,
                        bounds.min.y + bounds.size.y * maskCenter.y,
                        bounds.min.z + bounds.size.z * maskCenter.x);
                    size = new Vector3(Mathf.Max(bounds.size.x * .03f, .005f), bounds.size.y * maskSize.y, bounds.size.z * maskSize.x);
                    break;
                default:
                    center = new Vector3(
                        bounds.min.x + bounds.size.x * maskCenter.x,
                        bounds.min.y + bounds.size.y * maskCenter.y,
                        surfaceDirection.z >= 0f ? bounds.max.z : bounds.min.z);
                    size = new Vector3(bounds.size.x * maskSize.x, bounds.size.y * maskSize.y, Mathf.Max(bounds.size.z * .03f, .005f));
                    break;
            }

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix
                * Matrix4x4.TRS(sourceBounds.center, Quaternion.Euler(projectionRotation), Vector3.one);
            Gizmos.color = new Color(1f, .75f, .1f, .9f);
            Gizmos.DrawWireCube(center, size);
            Gizmos.DrawRay(center, surfaceDirection * Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * .35f);
            Gizmos.matrix = previous;
        }

        void OnDestroy()
        {
            foreach (GameObject overlay in overlayObjects)
                if (overlay != null) Destroy(overlay);
            if (material != null) Destroy(material);
            if (mask != null) Destroy(mask);
        }
    }
}
