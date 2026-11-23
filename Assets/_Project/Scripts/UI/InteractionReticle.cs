using UnityEngine;
using UnityEngine.UI;

namespace BORis
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InteractionReticle : MaskableGraphic
    {
        InteractionController interaction;
        float spread = 6f, velocity;
        public float Spread => spread;
        public bool IsVisible => interaction != null && interaction.ShowReticle;

        protected override void Awake()
        {
            base.Awake();
            interaction = GetComponentInParent<InteractionController>();
            raycastTarget = false;
        }

        void LateUpdate()
        {
            canvasRenderer.SetAlpha(IsVisible ? 1f : 0f);
            bool active = IsVisible && interaction.CanInteract;
            float next = Mathf.SmoothDamp(spread, active ? 10f : 6f, ref velocity, .09f, Mathf.Infinity, Time.unscaledDeltaTime);
            if (!Mathf.Approximately(next, spread)) { spread = next; SetVerticesDirty(); }
            color = Color.Lerp(color, active ? new Color(1f, .85f, .58f) : new Color(.87f, .92f, .9f, .8f),
                1f - Mathf.Exp(-Time.unscaledDeltaTime * 16f));
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            for (int pass = 0; pass < 2; pass++)
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                float left = x > 0 ? spread - 4.5f : -spread;
                float bottom = y > 0 ? spread - 4.5f : -spread;
                var horizontal = new Rect(left, y > 0 ? spread - 1.5f : -spread, 4.5f, 1.5f);
                var vertical = new Rect(x > 0 ? spread - 1.5f : -spread, bottom, 1.5f, 4.5f);
                Stroke(mesh, horizontal, pass == 0);
                Stroke(mesh, vertical, pass == 0);
            }
        }

        void Stroke(VertexHelper mesh, Rect rect, bool outline)
        {
            if (outline) rect = Rect.MinMaxRect(rect.xMin - .65f, rect.yMin - .65f, rect.xMax + .65f, rect.yMax + .65f);
            Quad(mesh, rect, outline ? new Color(0f, 0f, 0f, .6f) : color);
        }

        static void Quad(VertexHelper mesh, Rect rect, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector2(rect.xMin, rect.yMin), tint, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMin, rect.yMax), tint, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMax, rect.yMax), tint, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMax, rect.yMin), tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
