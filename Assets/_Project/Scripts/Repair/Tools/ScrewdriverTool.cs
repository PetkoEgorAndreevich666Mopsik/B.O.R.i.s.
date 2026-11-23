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
        float autoDirection, initialTightness, spin;

        public ScrewFastener Target { get; private set; }
        public bool IsTurning => Target != null;
        public override bool IsActive => IsTurning;
        public float ActionProgress => Target == null ? 0f : autoDirection < 0f
            ? 1f - Target.Tightness / Mathf.Max(initialTightness, .001f)
            : (Target.Tightness - initialTightness) / Mathf.Max(1f - initialTightness, .001f);

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
            autoDirection = !screw.IsTight && screw.IsTightening ? 1f : -1f;
            initialTightness = screw.Tightness;
            spin = 0f;
            view = camera;
            SetColliders(false);
            previousOffset = pointer - (Vector2)view.WorldToScreenPoint(screw.transform.position);
            hasPrevious = previousOffset.sqrMagnitude >= deadZone * deadZone;
            FollowScrew();
            return true;
        }

        public override void Drag(Vector2 pointer)
        {
            if (Target == null || !Target.isActiveAndEnabled || Target.IsRemoved || repair == null) { End(); return; }
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
            if (Target != null)
                previousOffset = pointer - (Vector2)view.WorldToScreenPoint(Target.transform.position);
        }

        public void Scroll(float amount, Vector2 pointer)
        {
            if (Target == null || repair == null || !float.IsFinite(amount) || amount == 0f) return;
            // Scroll is an impulse, not a rate; use the wheel direction across mouse delta units.
            ApplyTurn(Mathf.Sign(amount) * 45f);
            if (Target == null) return;
            previousOffset = pointer - (Vector2)view.WorldToScreenPoint(Target.transform.position);
            hasPrevious = previousOffset.sqrMagnitude >= deadZone * deadZone;
        }

        public void Hold(float deltaTime)
        {
            if (Target == null || repair == null || !float.IsFinite(deltaTime) || deltaTime <= 0f) return;
            ScrewFastener screw = Target;
            ApplyTurn(autoDirection * 900f * Mathf.Min(deltaTime, .05f));
            if (Target != null && autoDirection > 0f && screw.IsTight) End();
        }

        void ApplyTurn(float degrees)
        {
            ScrewFastener target = Target;
            float applied = repair.Turn(target, degrees);
            spin += applied;
            if (Mathf.Abs(applied) > .001f)
            {
                bool reachedEnd = target.IsRemoved || target.IsTight;
                if (reachedEnd || !audioSource.loop) audioSource.Stop();
                audioSource.loop = !reachedEnd;
                audioSource.clip = reachedEnd ? stopSound : turnSound;
                audioSource.volume = reachedEnd ? .65f
                    : Mathf.Clamp(Mathf.Abs(applied) / Mathf.Max(Time.unscaledDeltaTime, .001f) / 360f, .12f, .45f);
                if (audioSource.clip != null && !audioSource.isPlaying) audioSource.Play();
            }
            else if (audioSource.loop) audioSource.Stop();
            if (target.IsRemoved) End();
            else FollowScrew();
        }

        void FollowScrew()
        {
            transform.SetPositionAndRotation(Target.transform.position,
                Target.transform.rotation * Quaternion.Euler(18f, 0f, -22f) * Quaternion.AngleAxis(spin, Vector3.up));
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
