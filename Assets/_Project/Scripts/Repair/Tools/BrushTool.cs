using UnityEngine;

namespace BORis
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class BrushTool : RepairTool
    {
        [SerializeField, Min(.001f)] float radius = .035f;
        [SerializeField] AudioClip scrubSound;
        [SerializeField] Transform dustCleaning;
        [SerializeField, Min(0f)] float dustStopDelay = .08f;
        [SerializeField, Min(0f)] float dustSurfacePadding = .003f;
        AudioSource audioSource;
        ParticleSystem dustParticles;
        ParticleSystemRenderer dustRenderer;
        Camera view;
        RobotRepair repair;
        Vector2 previousUV;
        bool hasPrevious;
        float lastDustEmissionTime = float.NegativeInfinity;
        int queryMask;

        public DirtPatch Target { get; private set; }
        public override bool IsActive => repair != null;
        public bool HasContact { get; private set; }
        public bool IsDustEmitting => dustParticles != null && dustParticles.isEmitting;
        public float DustStopDelay => dustStopDelay;

        protected override void Awake()
        {
            base.Awake();
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.clip = scrubSound;
            if (dustCleaning == null) dustCleaning = transform.Find("DustCleaning");
            if (dustCleaning != null)
            {
                dustParticles = dustCleaning.GetComponentInChildren<ParticleSystem>(true);
                if (dustParticles != null)
                {
                    var main = dustParticles.main;
                    main.playOnAwake = false;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    dustRenderer = dustParticles.GetComponent<ParticleSystemRenderer>();

                    // DustCleaning's forward axis follows the surface normal.
                    // Keep the emitter on that same axis so a cone cannot fire into the object.
                    dustParticles.transform.localRotation = Quaternion.identity;
                    var shape = dustParticles.shape;
                    shape.rotation = Vector3.zero;
                    dustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        public bool Begin(RobotRepair robot, Camera camera, Vector2 pointer)
        {
            End();
            if (robot == null || camera == null || !robot.isActiveAndEnabled || robot.IsTesting || robot.IsOnDriveStand) return false;
            repair = robot;
            view = camera;
            var player = view.GetComponentInParent<PlayerController>();
            queryMask = player != null ? ~(1 << player.gameObject.layer) : ~0;
            SetColliders(false);
            Drag(pointer);
            if (!HasContact) { End(); return false; }
            return true;
        }

        public override void Drag(Vector2 pointer)
        {
            if (repair == null || !repair.isActiveAndEnabled || view == null || repair.IsTesting || repair.IsOnDriveStand) { End(); return; }
            DirtPatch previousTarget = Target;
            Target = null;
            HasContact = Physics.Raycast(view.ScreenPointToRay(pointer), out RaycastHit hit, Reach,
                queryMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<RobotRepair>() == repair;
            if (HasContact)
            {
                Target = hit.collider.GetComponent<DirtPatch>();
                if (Target != null && !repair.CanClean(Target, out _)) { Target = null; HasContact = false; }
            }
            if (!HasContact)
            {
                hasPrevious = false;
                audioSource.Stop();
                StopDust();
                return;
            }
            transform.SetPositionAndRotation(hit.point + hit.normal * .002f, Quaternion.LookRotation(hit.normal, view.transform.up));
            int cleaned = Target != null && Target == previousTarget && hasPrevious
                ? Target.CleanStroke(previousUV, hit.textureCoord, radius) : 0;
            if (cleaned > 0)
            {
                audioSource.volume = .28f;
                if (scrubSound != null && !audioSource.isPlaying) audioSource.Play();
                UpdateDust(true, hit.point, hit.normal);
            }
            else
            {
                audioSource.Stop();
                UpdateDust(false, hit.point, hit.normal);
            }
            previousUV = hit.textureCoord;
            hasPrevious = Target != null;
        }

        public override void End()
        {
            Target = null;
            repair = null;
            hasPrevious = HasContact = false;
            if (audioSource != null) audioSource.Stop();
            StopDust();
            RestorePose();
        }

        void UpdateDust(bool removedDirt, Vector3 point, Vector3 normal)
        {
            if (dustParticles == null) return;
            normal.Normalize();
            dustCleaning.SetPositionAndRotation(
                point + normal * GetDustSurfaceOffset(normal),
                Quaternion.LookRotation(normal, view.transform.up));

            if (removedDirt)
            {
                lastDustEmissionTime = Time.time;
                if (!dustParticles.isEmitting)
                {
                    dustParticles.Play();
                    dustParticles.Emit(1);
                }
                return;
            }

            if (dustParticles.isEmitting && Time.time - lastDustEmissionTime >= dustStopDelay)
                StopDust();
        }

        float GetDustSurfaceOffset(Vector3 surfaceNormal)
        {
            var main = dustParticles.main;
            float maxSize = main.startSize3D
                ? Mathf.Max(main.startSizeX.constantMax, main.startSizeY.constantMax, main.startSizeZ.constantMax)
                : main.startSize.constantMax;
            Vector3 scale = dustParticles.transform.lossyScale;
            maxSize *= Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            float particleRadius = maxSize * .5f;
            if (dustRenderer == null || dustRenderer.renderMode != ParticleSystemRenderMode.Billboard)
                return dustSurfacePadding + particleRadius;

            // A view-facing particle only intersects the surface when it is seen at an angle.
            // Move its centre just far enough along the normal to keep its visible disk outside.
            float facing = Mathf.Clamp01(Mathf.Abs(Vector3.Dot(surfaceNormal, view.transform.forward)));
            float grazing = Mathf.Sqrt(1f - facing * facing);
            return dustSurfacePadding + particleRadius * grazing;
        }

        void StopDust()
        {
            if (dustParticles == null) return;
            dustParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            lastDustEmissionTime = float.NegativeInfinity;
        }

        void OnDisable() => End();
    }
}
