using UnityEngine;

namespace BORis
{
    public sealed class ScrewFastener : MonoBehaviour
    {
        [SerializeField, Min(.1f)] float turns = 2f;
        [SerializeField, Min(0f)] float travel = .06f;
        [SerializeField, Range(0f, 1f)] float tightness = 1f;
        Vector3 seatedPosition;
        Quaternion seatedRotation;

        public float Tightness => tightness;

        void Awake()
        {
            seatedPosition = transform.localPosition;
            seatedRotation = transform.localRotation;
            UpdatePose();
        }

        internal float Turn(float clockwiseDegrees)
        {
            if (!float.IsFinite(clockwiseDegrees)) return 0f;
            float previous = tightness;
            tightness = Mathf.Clamp01(tightness + clockwiseDegrees / (turns * 360f));
            UpdatePose();
            return (tightness - previous) * turns * 360f;
        }

        void UpdatePose()
        {
            float loose = 1f - tightness;
            transform.localPosition = seatedPosition + seatedRotation * Vector3.up * (loose * travel);
            transform.localRotation = seatedRotation * Quaternion.AngleAxis(-loose * turns * 360f, Vector3.up);
        }
    }
}
