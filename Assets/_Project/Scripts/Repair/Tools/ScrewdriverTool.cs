using UnityEngine;

namespace BORis
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class ScrewdriverTool : RepairTool
    {
        [SerializeField, Min(1f)] float deadZone = 12f;
        [SerializeField] AudioClip turnSound;
        [SerializeField] AudioClip stopSound;
        AudioSource audioSource;
        Camera view;
        RobotRepair repair;
        Vector2 previousOffset;
        bool hasPrevious;

        public ScrewFastener Target { get; private set; }
        public bool IsTurning => Target != null;
        public override bool IsActive => IsTurning;

        protected override void Awake()
        {
            base.Awake();
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.clip = turnSound;
        }

        public bool Begin(ScrewFastener screw, Camera camera, Vector2 pointer)
        {
            End();
            if (screw == null || !screw.isActiveAndEnabled || camera == null) return false;
            repair = screw.GetComponentInParent<RobotRepair>();
            if (repair == null || !repair.CanTurn(screw, out _)) return false;
            Target = screw;
            view = camera;
            SetColliders(false);
            previousOffset = pointer - (Vector2)view.WorldToScreenPoint(screw.transform.position);
            hasPrevious = previousOffset.sqrMagnitude >= deadZone * deadZone;
            FollowScrew();
            return true;
        }

        public override void Drag(Vector2 pointer)
        {
            if (Target == null || !Target.isActiveAndEnabled || repair == null) { End(); return; }
            Vector2 offset = pointer - (Vector2)view.WorldToScreenPoint(Target.transform.position);
            if (offset.sqrMagnitude < deadZone * deadZone)
            {
                hasPrevious = false;
                if (audioSource.loop) audioSource.Stop();
                return;
            }
            float degrees = hasPrevious ? -Vector2.SignedAngle(previousOffset, offset) : 0f;
            previousOffset = offset;
            hasPrevious = true;
            // Ignore pointer warps; resetting the baseline lets the next real stroke continue.
            if (Mathf.Abs(degrees) > 90f) degrees = 0f;
            ApplyTurn(degrees);
            // The screw moves toward the camera; don't count that movement as a reverse gesture.
            previousOffset = pointer - (Vector2)view.WorldToScreenPoint(Target.transform.position);
        }

        public void Scroll(float amount, Vector2 pointer)
        {
            if (Target == null || repair == null || !float.IsFinite(amount) || amount == 0f) return;
            // Scroll is an impulse, not a rate; use the wheel direction across mouse delta units.
            ApplyTurn(Mathf.Sign(amount) * 45f);
            previousOffset = pointer - (Vector2)view.WorldToScreenPoint(Target.transform.position);
            hasPrevious = previousOffset.sqrMagnitude >= deadZone * deadZone;
        }

        void ApplyTurn(float degrees)
        {
            float applied = repair.Turn(Target, degrees);
            if (Mathf.Abs(applied) > .001f)
            {
                bool reachedEnd = Target.IsLoose || Target.IsTight;
                if (reachedEnd || !audioSource.loop) audioSource.Stop();
                audioSource.loop = !reachedEnd;
                audioSource.clip = reachedEnd ? stopSound : turnSound;
                audioSource.volume = reachedEnd ? .65f
                    : Mathf.Clamp(Mathf.Abs(applied) / Mathf.Max(Time.unscaledDeltaTime, .001f) / 360f, .12f, .45f);
                if (audioSource.clip != null && !audioSource.isPlaying) audioSource.Play();
            }
            else if (audioSource.loop) audioSource.Stop();
            FollowScrew();
        }

        void FollowScrew()
        {
            transform.SetPositionAndRotation(Target.transform.position, Target.transform.rotation);
        }

        public override void End()
        {
            Target = null;
            repair = null;
            hasPrevious = false;
            if (audioSource != null && audioSource.loop) audioSource.Stop();
            RestorePose();
        }

        void OnDisable() => End();
    }
}
