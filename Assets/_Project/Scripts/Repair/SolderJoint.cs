using UnityEngine;

namespace BORis
{
    public sealed class SolderJoint : MonoBehaviour
    {
        [SerializeField, Min(.01f)] float length = .3f;
        [SerializeField] Renderer[] segments = System.Array.Empty<Renderer>();
        [SerializeField] Material repairedMaterial;
        [SerializeField] bool initiallyRepaired;
        bool[] repaired;
        int repairedCount;
        MaterialPropertyBlock seamTint;
        RobotRepair robot;

        public float Progress => repaired != null && repaired.Length > 0 ? (float)repairedCount / repaired.Length : 0f;
        public bool IsRepaired => repaired != null && repaired.Length > 0 && repairedCount == repaired.Length;
        public float PositionOnSeam(Vector3 point) => transform.InverseTransformPoint(point).x / length + .5f;
        public Vector3 PointOnSeam(float position) => transform.TransformPoint(new Vector3((position - .5f) * length, 0f, -.006f));

        void Awake()
        {
            robot = GetComponentInParent<RobotRepair>();
            seamTint = new MaterialPropertyBlock();
            repaired = new bool[segments.Length];
            if (!initiallyRepaired) return;
            for (int i = 0; i < segments.Length; i++)
            {
                repaired[i] = true;
                if (segments[i] != null) segments[i].sharedMaterial = repairedMaterial;
            }
            repairedCount = segments.Length;
        }

        void LateUpdate()
        {
            bool visible = robot != null && robot.Cover != null && !robot.Cover.IsInstalled && !IsRepaired;
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == null) continue;
                if (!visible || repaired[i]) { segments[i].SetPropertyBlock(null); continue; }
                var material = segments[i].sharedMaterial;
                if (material == null || !material.HasProperty("_BaseColor")) continue;
                seamTint.SetColor("_BaseColor", Color.Lerp(material.GetColor("_BaseColor"), new Color(1f, .65f, .22f),
                    .35f + Mathf.Sin(Time.unscaledTime * 3f) * .15f));
                segments[i].SetPropertyBlock(seamTint);
            }
        }

        void OnDisable()
        {
            foreach (var segment in segments) if (segment != null) segment.SetPropertyBlock(null);
        }

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
