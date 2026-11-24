using UnityEngine;
using UnityEngine.UI;

namespace BORis
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RepairHintView : MaskableGraphic
    {
        public Transform Target { get; private set; }
        public Color Tint { get; set; } = new(.4f, .96f, .82f);
        Camera view;
        RectTransform canvas;
        float padding = 16f, fallbackSize = .08f;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            canvasRenderer.SetAlpha(0f);
        }

        public void Show(Transform target, Camera camera, float border = 16f, float emptySize = .08f)
        {
            Target = target;
            view = camera;
            padding = border;
            fallbackSize = emptySize;
        }

        public void Hide()
        {
            Target = null;
            canvasRenderer.SetAlpha(0f);
        }

        void LateUpdate()
        {
            if (Target == null || !Target.gameObject.activeInHierarchy || view == null) { Hide(); return; }
            if (canvas == null)
            {
                var owner = GetComponentInParent<Canvas>();
                if (owner == null) return;
                canvas = owner.GetComponent<RectTransform>();
            }
            Bounds bounds = Target.TryGetComponent(out Collider collider) ? collider.bounds : new Bounds(Target.position, Vector3.one * fallbackSize);
            if (collider == null)
            {
                bool first = true;
                foreach (var renderer in Target.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || renderer is ParticleSystemRenderer) continue;
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            }
            Vector2 minimum = Vector2.positiveInfinity, maximum = Vector2.negativeInfinity;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 screen = view.WorldToScreenPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                if (screen.z <= 0f) { canvasRenderer.SetAlpha(0f); return; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 point);
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
            rectTransform.anchoredPosition = (minimum + maximum) * .5f;
            rectTransform.sizeDelta = Vector2.Max(maximum - minimum + Vector2.one * padding, Vector2.one * 38f);
            color = new Color(Tint.r, Tint.g, Tint.b, .65f + Mathf.Sin(Time.unscaledTime * 4f) * .2f);
            canvasRenderer.SetAlpha(1f);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            Color fill = color; fill.a *= .09f;
            Quad(mesh, rect, fill);
            const float length = 14f, thickness = 2f;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                float left = x < 0 ? rect.xMin : rect.xMax - length;
                float bottom = y < 0 ? rect.yMin : rect.yMax - length;
                Quad(mesh, new Rect(left, y < 0 ? rect.yMin : rect.yMax - thickness, length, thickness), color);
                Quad(mesh, new Rect(x < 0 ? rect.xMin : rect.xMax - thickness, bottom, thickness, length), color);
            }
        }

        static void Quad(VertexHelper mesh, Rect rect, Color tint)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(new Vector2(rect.xMin, rect.yMin), tint, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMin, rect.yMax), tint, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMax, rect.yMax), tint, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMax, rect.yMin), tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
