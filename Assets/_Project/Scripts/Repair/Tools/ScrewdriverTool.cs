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
        Collider[] colliders;
        Vector3 homePosition;
        Quaternion homeRotation;
        Camera view;
        RobotRepair repair;
        Vector2 previousOffset;
        bool hasPrevious, atStop;

        public ScrewFastener Target { get; private set; }
        public bool IsTurning => Target != null;
        public override bool IsActive => IsTurning;

        void Awake()
        {
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.clip = turnSound;
            colliders = GetComponentsInChildren<Collider>();
        }

        public bool Begin(ScrewFastener screw, Camera camera, Vector2 pointer)
        {
            End();
            if (screw == null || !screw.isActiveAndEnabled || camera == null) return false;
            repair = screw.GetComponentInParent<RobotRepair>();
            if (repair == null || !repair.CanTurn(screw, out _)) return false;
            Target = screw;
            view = camera;
            foreach (Collider collider in colliders) collider.enabled = false;
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
                audioSource.Stop();
                return;
            }
            float degrees = hasPrevious ? -Vector2.SignedAngle(previousOffset, offset) : 0f;
            previousOffset = offset;
            hasPrevious = true;
            // Ignore pointer warps; resetting the baseline lets the next real stroke continue.
            if (Mathf.Abs(degrees) > 90f) degrees = 0f;
            float applied = repair.Turn(Target, degrees);
            if (Mathf.Abs(applied) > .001f)
            {
                if (atStop) audioSource.Stop();
                atStop = false;
                audioSource.volume = Mathf.Clamp(Mathf.Abs(applied) / Mathf.Max(Time.unscaledDeltaTime, .001f) / 360f, .12f, .45f);
                if (turnSound != null && !audioSource.isPlaying) audioSource.Play();
            }
            else
            {
                if (!atStop) audioSource.Stop();
                if (Mathf.Abs(degrees) > .1f && !atStop)
                {
                    atStop = true;
                    if (stopSound != null) audioSource.PlayOneShot(stopSound, .5f);
                }
            }
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
            hasPrevious = atStop = false;
            if (audioSource != null) audioSource.Stop();
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            if (colliders != null)
                foreach (Collider collider in colliders) if (collider != null) collider.enabled = true;
        }

        void OnDisable() => End();
    }
}
