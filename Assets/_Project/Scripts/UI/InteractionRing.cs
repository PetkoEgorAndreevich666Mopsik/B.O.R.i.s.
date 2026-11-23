using UnityEngine;
using UnityEngine.UI;

namespace BORis
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InteractionRing : MaskableGraphic
    {
        float progress;
        public float Progress
        {
            get => progress;
            set { if (Mathf.Approximately(progress, value)) return; progress = Mathf.Clamp01(value); SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .5f;
            for (int i = 0; i < 64; i++)
            {
                float a = (90f - i * 360f / 64f) * Mathf.Deg2Rad;
                float b = (90f - (i + 1) * 360f / 64f) * Mathf.Deg2Rad;
                Color tint = color; if ((i + .5f) / 64f > progress) tint.a *= .16f;
                int start = mesh.currentVertCount;
                var va = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var vb = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                mesh.AddVert(va * radius, tint, Vector2.zero);
                mesh.AddVert(va * (radius - 3f), tint, Vector2.zero);
                mesh.AddVert(vb * (radius - 3f), tint, Vector2.zero);
                mesh.AddVert(vb * radius, tint, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
