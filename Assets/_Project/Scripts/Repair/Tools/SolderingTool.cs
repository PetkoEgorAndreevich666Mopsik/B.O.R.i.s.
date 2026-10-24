using UnityEngine;

namespace BORis
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class SolderingTool : RepairTool
    {
        [SerializeField] WorkshopSession session;
        [SerializeField] AudioClip solderSound;
        AudioSource audioSource;
        Camera view;
        RobotRepair repair;
        float previousPosition;
        bool hasPrevious;
        int queryMask;

        public bool IsAvailable => isActiveAndEnabled && session != null && session.isActiveAndEnabled && session.CanOfferSolderingOrders;
        public SolderJoint Target { get; private set; }
        public override bool IsActive => Target != null;
        public bool HasContact { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.clip = solderSound;
            audioSource.volume = .22f;
        }

        public bool Begin(SolderJoint joint, Camera camera, Vector2 pointer)
        {
            End();
            if (!IsAvailable || joint == null || camera == null) return false;
            repair = joint.GetComponentInParent<RobotRepair>();
            if (repair == null || !repair.CanSolder(joint, session, out _)) return false;
            Target = joint;
            view = camera;
            var player = view.GetComponentInParent<PlayerController>();
            queryMask = player != null ? ~(1 << player.gameObject.layer) : ~0;
            SetColliders(false);
            Drag(pointer);
            return true;
        }

        public override void Drag(Vector2 pointer)
        {
            if (!IsAvailable || Target == null || repair == null || !repair.CanSolder(Target, session, out _)) { End(); return; }
            HasContact = Physics.Raycast(view.ScreenPointToRay(pointer), out RaycastHit hit, Reach,
                queryMask, QueryTriggerInteraction.Ignore) && hit.collider.GetComponent<SolderJoint>() == Target;
            if (!HasContact)
            {
                hasPrevious = false;
                audioSource.Stop();
                return;
            }
            transform.SetPositionAndRotation(hit.point + hit.normal * .002f,
                Quaternion.LookRotation(hit.normal, view.transform.up) * Quaternion.Euler(60f, 0f, -25f));
            float position = Mathf.Clamp01(Target.PositionOnSeam(hit.point));
            int added = hasPrevious ? repair.SolderStroke(Target, session, previousPosition, position) : 0;
            if (added > 0 && solderSound != null)
            {
                if (!audioSource.isPlaying) audioSource.Play();
            }
            else audioSource.Stop();
            previousPosition = position;
            hasPrevious = true;
        }

        public override void End()
        {
            Target = null;
            repair = null;
            hasPrevious = HasContact = false;
            if (audioSource != null) audioSource.Stop();
            RestorePose();
        }

        void OnDisable() => End();
    }
}
