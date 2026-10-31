using UnityEngine;

namespace BORis
{
    public enum RepairPartKind { Cover, Drive }
    public enum DriveTestResult { NotTested, Smooth, Interrupted }

    public sealed class RepairPart : MonoBehaviour
    {
        [SerializeField] RepairPartKind kind;
        [SerializeField] string displayName;
        [SerializeField] bool faulty;
        [SerializeField] bool installed = true;
        [SerializeField] Transform installationAnchor;
        [SerializeField] Transform trayAnchor;
        [SerializeField] AudioClip releaseSound;
        ScrewFastener[] screws;
        Rigidbody body;
        Rigidbody[] queryBodies;
        Vector3[] queryLocalPositions;
        Quaternion[] queryLocalRotations;
        Collider tableSurface;
        Vector3 holdPosition;
        Quaternion holdRotation;
        AudioSource audioSource;
        float nextImpact;

        public RepairPartKind Kind => kind;
        public string DisplayName => displayName;
        public bool Faulty => faulty;
        public bool IsInstalled => installed;
        public Transform InstallationAnchor => installationAnchor;
        public Transform TrayAnchor => trayAnchor;
        public ScrewFastener[] Screws => screws;
        public bool IsReleased { get; private set; }
        public bool IsDragging { get; private set; }
        public bool IsSupported { get; private set; }
        public Rigidbody Body => body;
        internal float ExtractionDistance => kind == RepairPartKind.Cover ? .34f : .28f;
        internal Pose PickupPose { get; private set; }
        public DriveTestResult StandResult { get; internal set; }
        public bool WasInitiallyInstalled { get; private set; }
        public string DriveIdentity => WasInitiallyInstalled ? "Исходный привод" : "Запасной привод";
        public string StandObservation => StandResult == DriveTestResult.Interrupted ? "вращение прерывается, вал заедает"
            : StandResult == DriveTestResult.Smooth ? "вал вращается равномерно" : "на стенде не проверен";

        void Awake()
        {
            WasInitiallyInstalled = installed;
            screws = GetComponentsInChildren<ScrewFastener>();
            // Keep non-convex cleaning meshes as UV ray targets, outside the dynamic compound.
            var surfaces = GetComponentsInChildren<MeshCollider>();
            queryBodies = new Rigidbody[surfaces.Length];
            queryLocalPositions = new Vector3[surfaces.Length];
            queryLocalRotations = new Quaternion[surfaces.Length];
            for (int i = 0; i < surfaces.Length; i++)
            {
                queryLocalPositions[i] = transform.InverseTransformPoint(surfaces[i].transform.position);
                queryLocalRotations[i] = Quaternion.Inverse(transform.rotation) * surfaces[i].transform.rotation;
                var query = surfaces[i].GetComponent<Rigidbody>();
                if (query == null) query = surfaces[i].gameObject.AddComponent<Rigidbody>();
                query.isKinematic = true;
                query.useGravity = false;
                query.interpolation = RigidbodyInterpolation.None;
                query.excludeLayers = ~0;
                queryBodies[i] = query;
            }
            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.mass = kind == RepairPartKind.Cover ? .9f : 2.5f;
            body.linearDamping = 2f;
            body.angularDamping = 4f;
            body.maxAngularVelocity = 6f;
            body.maxDepenetrationVelocity = 1.5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = 10;
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            var bench = GetComponentInParent<Workbench>();
            if (bench != null)
            {
                Transform surface = bench.transform.Find("TableTop");
                if (surface != null) tableSurface = surface.GetComponent<Collider>();
            }
            UpdatePhysics();
        }

        public void SetDragging(bool dragging)
        {
            IsDragging = dragging;
            if (dragging)
            {
                PickupPose = new Pose(transform.position, transform.rotation);
                IsSupported = false;
                holdPosition = transform.position;
                holdRotation = transform.rotation;
            }
            UpdatePhysics();
        }

        public void MoveHeld(Vector3 position, Quaternion rotation)
        {
            if (!IsDragging || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z)) return;
            holdPosition = position;
            holdRotation = rotation;
        }

        public void SetSupported(bool supported)
        {
            IsSupported = supported;
            IsDragging = false;
            UpdatePhysics();
        }

        void UpdatePhysics()
        {
            if (body == null) return;
            bool fixedPose = installed || IsSupported;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = fixedPose;
            body.interpolation = fixedPose ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            body.useGravity = !fixedPose && !IsDragging;
            if (!fixedPose) body.WakeUp();
        }

        void FixedUpdate()
        {
            if (installed || IsSupported) return;
            if (tableSurface != null && !IsDragging)
            {
                Bounds table = tableSurface.bounds;
                Vector3 p = body.position;
                if (p.y < table.min.y - .6f || p.x < table.min.x - .7f || p.x > table.max.x + .7f
                    || p.z < table.min.z - .7f || p.z > table.max.z + .7f)
                {
                    RecoverToTable();
                    return;
                }
            }
            if (!IsDragging) return;
            Vector3 acceleration = (holdPosition - body.position) * 180f - body.linearVelocity * 26f;
            body.AddForce(Vector3.ClampMagnitude(acceleration, 70f), ForceMode.Acceleration);
            Quaternion delta = holdRotation * Quaternion.Inverse(body.rotation);
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > .01f && float.IsFinite(axis.x))
                body.AddTorque(Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad * 100f)
                    - body.angularVelocity * 20f, 60f), ForceMode.Acceleration);
        }

        void LateUpdate() => SynchronizePhysicsPose();

        internal void SynchronizePhysicsPose()
        {
            if (body != null && (installed || IsSupported))
            {
                if (installed) AlignWithAnchor();
                body.position = transform.position;
                body.rotation = transform.rotation;
            }
            SynchronizeQueryBodies();
        }

        void SynchronizeQueryBodies()
        {
            if (queryBodies == null) return;
            for (int i = 0; i < queryBodies.Length; i++)
            {
                Rigidbody query = queryBodies[i];
                if (query == null) continue;
                // Child actors retain world poses in PhysX; rebuild the authored surface pose from its part.
                Vector3 position = transform.TransformPoint(queryLocalPositions[i]);
                Quaternion rotation = transform.rotation * queryLocalRotations[i];
                query.transform.SetPositionAndRotation(position, rotation);
                query.position = position;
                query.rotation = rotation;
            }
        }

        public void RecoverToTable()
        {
            if (installed || IsSupported || trayAnchor == null) return;
            IsDragging = false;
            SetSupported(true);
            transform.SetPositionAndRotation(trayAnchor.position + Vector3.up * .06f, trayAnchor.rotation);
            body.position = transform.position;
            body.rotation = transform.rotation;
            SetSupported(false);
            SynchronizeQueryBodies();
            Physics.SyncTransforms();
        }

        void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (installed || IsSupported || speed < .3f || Time.unscaledTime < nextImpact) return;
            AudioClip sound = releaseSound;
            var owner = GetComponentInParent<RobotRepair>();
            if (sound == null && owner != null && owner.Cover != null) sound = owner.Cover.releaseSound;
            if (sound == null) return;
            audioSource.pitch = kind == RepairPartKind.Cover ? 1.3f : .72f;
            audioSource.PlayOneShot(sound, Mathf.Clamp(speed * .12f, .06f, .45f));
            nextImpact = Time.unscaledTime + .15f;
        }

        internal void Place(bool inRobot)
        {
            bool extracted = installed && !inRobot;
            installed = inRobot;
            IsReleased = false;
            if (extracted && installationAnchor != null)
            {
                // Clear the service platform so the released panel cannot remain standing in front of the opening.
                transform.position -= installationAnchor.forward * ExtractionDistance;
                if (kind == RepairPartKind.Cover)
                    transform.rotation = Quaternion.AngleAxis(-12f, installationAnchor.right) * transform.rotation;
            }
            RestorePose();
        }

        internal void RefreshRelease()
        {
            if (kind != RepairPartKind.Cover || !installed) return;
            bool released = screws.Length > 0;
            foreach (ScrewFastener screw in screws) released &= screw.IsLoose;
            if (released == IsReleased) return;
            IsReleased = released;
            RestorePose();
            if (released && releaseSound != null)
            {
                var audio = GetComponent<AudioSource>();
                if (audio != null) audio.PlayOneShot(releaseSound, .7f);
            }
        }

        public void RestorePose()
        {
            IsDragging = false;
            IsSupported = false;
            if (installed) AlignWithAnchor();
            body.position = transform.position;
            body.rotation = transform.rotation;
            UpdatePhysics();
            SynchronizeQueryBodies();
        }

        internal void AlignWithAnchor()
        {
            Transform anchor = installed ? installationAnchor : null;
            if (anchor != null) transform.SetPositionAndRotation(
                anchor.position + (installed && IsReleased ? anchor.rotation * Vector3.back * .035f : Vector3.zero), anchor.rotation);
        }
    }
}
