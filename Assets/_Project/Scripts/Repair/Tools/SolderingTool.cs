using UnityEngine;

namespace BORis
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class SolderingTool : RepairTool
    {
        [SerializeField] WorkshopSession session;
        [SerializeField] AudioClip solderSound;
        [SerializeField] ParticleSystem smoke;
        AudioSource audioSource;
        float workedUntil;
        Camera view;
        RobotRepair repair;
        float previousPosition;
        bool hasPrevious;
        int queryMask;

        internal void Bind(WorkshopSession owner) => session = owner;

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
            if (solderSound == null) solderSound = Resources.Load<AudioClip>("WorkshopSounds/SolderLoop");
            audioSource.clip = solderSound;
            if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
                if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            transform.SetPositionAndRotation(hit.point + hit.normal * .002f,
                Quaternion.LookRotation(hit.normal, view.transform.up) * Quaternion.Euler(60f, 0f, -25f));
            float position = Mathf.Clamp01(Target.PositionOnSeam(hit.point));
            int added = hasPrevious ? repair.SolderStroke(Target, session, previousPosition, position) : 0;
            if (added > 0) workedUntil = Time.unscaledTime + .12f;
            bool heating = Time.unscaledTime < workedUntil;
            audioSource.volume = Mathf.MoveTowards(audioSource.volume, heating ? .18f : 0f, Time.unscaledDeltaTime * 2f);
            if (heating && solderSound != null && !audioSource.isPlaying) audioSource.Play();
            if (!heating && audioSource.volume <= 0f) audioSource.Stop();
            if (smoke != null)
            {
                smoke.transform.position = hit.point + hit.normal * .004f;
                smoke.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                if (heating && !smoke.isPlaying) smoke.Play();
                else if (!heating) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            previousPosition = position;
            hasPrevious = true;
        }

        public override void End()
        {
            Target = null;
            repair = null;
            hasPrevious = HasContact = false;
            workedUntil = 0f;
            if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (audioSource != null) audioSource.Stop();
            RestorePose();
        }

        void OnDisable() => End();
    }
}
