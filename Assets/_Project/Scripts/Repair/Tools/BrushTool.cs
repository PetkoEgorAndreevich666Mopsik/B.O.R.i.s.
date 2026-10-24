using UnityEngine;

namespace BORis
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class BrushTool : RepairTool
    {
        [SerializeField, Min(.001f)] float radius = .035f;
        [SerializeField] AudioClip scrubSound;
        AudioSource audioSource;
        Camera view;
        RobotRepair repair;
        Vector2 previousUV;
        bool hasPrevious;
        int queryMask;

        public DirtPatch Target { get; private set; }
        public override bool IsActive => Target != null;
        public bool HasContact { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.clip = scrubSound;
        }

        public bool Begin(DirtPatch patch, Camera camera, Vector2 pointer)
        {
            End();
            if (patch == null || camera == null) return false;
            repair = patch.GetComponentInParent<RobotRepair>();
            if (repair == null || !repair.CanClean(patch, out _)) return false;
            Target = patch;
            view = camera;
            var player = view.GetComponentInParent<PlayerController>();
            queryMask = player != null ? ~(1 << player.gameObject.layer) : ~0;
            SetColliders(false);
            Drag(pointer);
            return true;
        }

        public override void Drag(Vector2 pointer)
        {
            if (Target == null || repair == null || !repair.CanClean(Target, out _)) { End(); return; }
            HasContact = Physics.Raycast(view.ScreenPointToRay(pointer), out RaycastHit hit, Reach,
                queryMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponent<DirtPatch>() == Target;
            if (!HasContact)
            {
                hasPrevious = false;
                audioSource.Stop();
                return;
            }
            transform.SetPositionAndRotation(hit.point + hit.normal * .002f, Quaternion.LookRotation(hit.normal, view.transform.up));
            int cleaned = hasPrevious ? Target.CleanStroke(previousUV, hit.textureCoord, radius) : 0;
            if (cleaned > 0)
            {
                audioSource.volume = .28f;
                if (scrubSound != null && !audioSource.isPlaying) audioSource.Play();
            }
            else audioSource.Stop();
            previousUV = hit.textureCoord;
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
