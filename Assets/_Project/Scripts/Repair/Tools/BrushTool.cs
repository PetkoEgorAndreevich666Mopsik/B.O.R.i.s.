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
        [SerializeField, Range(0f, 90f)] float handleSurfaceAngle = 75f;
        [SerializeField, Min(0f)] float brushSurfaceOffset = .024f;
        [SerializeField, Min(0f)] float rotationSpeed = 720f;
        [SerializeField, Min(0f)] float rotationMovementThreshold = .002f;
        [SerializeField, Range(0f, 90f)] float normalTransitionAngle = 25f;
        [SerializeField, Min(.001f)] float maxNormalTransitionDistance = .1f;
        [SerializeField, Range(90f, 180f)] float maxNormalTransitionAngle = 100f;
        AudioSource audioSource;
        ParticleSystem dustParticles;
        ParticleSystemRenderer dustRenderer;
        Camera view;
        RobotRepair repair;
        Vector2 previousUV;
        bool hasPrevious;
        Vector3 previousContactPoint;
        Vector3 brushUp;
        bool hasContactPose;
        Vector3 surfaceAnchorPoint;
        Vector3 surfaceAnchorNormal;
        bool hasSurfaceAnchor;
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
                hasPrevious = hasContactPose = false;
                audioSource.Stop();
                StopDust();
                return;
            }

            Vector3 hitNormal = hit.normal.normalized;
            float normalAngle = hasSurfaceAnchor
                ? Vector3.Angle(surfaceAnchorNormal, hitNormal)
                : 0f;
            bool changesSurface = hasSurfaceAnchor && normalAngle >= normalTransitionAngle;

            // Do not let a ray jump from the current face to a distant or opposite face.
            // The user must move through the edge, where both contact points are close.
            if (changesSurface && (normalAngle > maxNormalTransitionAngle
                || Vector3.Distance(surfaceAnchorPoint, hit.point) > maxNormalTransitionDistance))
            {
                Target = null;
                HasContact = false;
                hasPrevious = hasContactPose = false;
                audioSource.Stop();
                StopDust();
                return;
            }

            // A valid edge crossing starts a new stroke and a new tangent frame. This
            // prevents UV lines and the old movement direction from leaking across faces.
            if (changesSurface)
            {
                hasPrevious = hasContactPose = false;
                audioSource.Stop();
                StopDust();
            }

            transform.SetPositionAndRotation(
                hit.point + hitNormal * brushSurfaceOffset,
                SurfaceRotation(hit.point, hitNormal));
            int cleaned = Target != null && Target == previousTarget && hasPrevious
                ? Target.CleanStroke(previousUV, hit.textureCoord, radius) : 0;
            if (cleaned > 0)
            {
                audioSource.volume = .28f;
                if (scrubSound != null && !audioSource.isPlaying) audioSource.Play();
                UpdateDust(true, hit.point, hitNormal);
            }
            else
            {
                audioSource.Stop();
                UpdateDust(false, hit.point, hitNormal);
            }
            surfaceAnchorPoint = hit.point;
            surfaceAnchorNormal = hitNormal;
            hasSurfaceAnchor = true;
            previousUV = hit.textureCoord;
            hasPrevious = true;
        }

        public override void End()
        {
            Target = null;
            repair = null;
            hasPrevious = HasContact = hasContactPose = hasSurfaceAnchor = false;
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

        Quaternion SurfaceRotation(Vector3 point, Vector3 normal)
        {
            normal.Normalize();
            Vector3 cameraUp = Vector3.ProjectOnPlane(view.transform.up, normal);
            if (cameraUp.sqrMagnitude < .000001f)
                cameraUp = Vector3.ProjectOnPlane(view.transform.right, normal);
            cameraUp.Normalize();

            if (!hasContactPose)
            {
                brushUp = cameraUp;
                previousContactPoint = point;
                hasContactPose = true;
                return TiltFromSurface(Quaternion.LookRotation(normal, brushUp));
            }

            Vector3 currentUp = Vector3.ProjectOnPlane(brushUp, normal);
            if (currentUp.sqrMagnitude < .000001f) currentUp = cameraUp;
            else currentUp.Normalize();

            Vector3 movement = Vector3.ProjectOnPlane(point - previousContactPoint, normal);
            Vector3 targetUp = movement.sqrMagnitude >= rotationMovementThreshold * rotationMovementThreshold
                ? -movement.normalized
                : currentUp;

            float signedAngle = Vector3.SignedAngle(currentUp, targetUp, normal);
            float maxStep = rotationSpeed * Time.deltaTime;
            brushUp = Quaternion.AngleAxis(Mathf.Clamp(signedAngle, -maxStep, maxStep), normal) * currentUp;
            brushUp.Normalize();
            previousContactPoint = point;
            return TiltFromSurface(Quaternion.LookRotation(normal, brushUp));
        }

        Quaternion TiltFromSurface(Quaternion surfaceRotation) =>
            surfaceRotation * Quaternion.Euler(handleSurfaceAngle, 0f, 0f);

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
