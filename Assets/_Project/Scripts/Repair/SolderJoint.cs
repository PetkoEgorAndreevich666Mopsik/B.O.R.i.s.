using UnityEngine;

namespace BORis
{
    public sealed class SolderJoint : MonoBehaviour
    {
        [SerializeField, Min(.01f)] float length = .3f;
        [SerializeField] Renderer[] segments = System.Array.Empty<Renderer>();
        [SerializeField] Material repairedMaterial;
        bool[] repaired;
        int repairedCount;

        public float Progress => repaired != null && repaired.Length > 0 ? (float)repairedCount / repaired.Length : 0f;
        public bool IsRepaired => repaired != null && repaired.Length > 0 && repairedCount == repaired.Length;
        public float PositionOnSeam(Vector3 point) => transform.InverseTransformPoint(point).x / length + .5f;
        public Vector3 PointOnSeam(float position) => transform.TransformPoint(new Vector3((position - .5f) * length, 0f, -.006f));

        void Awake() => repaired = new bool[segments.Length];

        internal int ApplyStroke(float from, float to)
        {
            if (!isActiveAndEnabled || repaired == null || repairedMaterial == null
                || !float.IsFinite(from) || !float.IsFinite(to) || from < 0f || from > 1f || to < 0f || to > 1f
                || Mathf.Abs(to - from) < .0001f) return 0;
            int added = 0;
            // shortcut: a straight seam with one cell per visible segment; use a path only when repairs need bends.
            float radius = .008f / length;
            for (int i = 0; i < repaired.Length; i++)
            {
                float position = (i + .5f) / repaired.Length;
                if (repaired[i] || segments[i] == null || position < Mathf.Min(from, to) - radius
                    || position > Mathf.Max(from, to) + radius) continue;
                repaired[i] = true;
                segments[i].sharedMaterial = repairedMaterial;
                repairedCount++;
                added++;
            }
            return added;
        }
    }
}
