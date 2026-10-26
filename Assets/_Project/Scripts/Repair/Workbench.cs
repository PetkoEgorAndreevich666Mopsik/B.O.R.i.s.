using UnityEngine;

namespace BORis
{
    public sealed class Workbench : MonoBehaviour
    {
        [SerializeField] Transform cameraAnchor;
        [SerializeField] Collider testButton;
        [SerializeField] RobotRepair robotPrefab;
        [SerializeField] GameObject emptyIndicator;
        RobotRepair robot;
        Transform view;
        Vector3 previousPosition;
        Quaternion previousRotation;
        Transform robotParent;
        Vector3 robotPosition;
        Quaternion robotRotation;
        Vector3 robotScale;
        Camera inspectionCamera;
        Vector3 overviewPosition, inspectionPivot, framingCenter, cameraVelocity, sourcePosition;
        Transform inspectionRoot;
        RepairPart framingPart, liftedPart;
        Quaternion sourceRotation;
        Quaternion overviewRotation, inspectionRotation, objectRotation;
        float lift, liftHeight;
        float inspectionDistance, minimumDistance;
        float initialDistance, maximumDistance;
        int cameraMask;

        public bool IsFocused { get; private set; }
        public RepairPart FocusedPart => IsFocused ? liftedPart : null;
        public string InspectionName => framingPart != null && robot != null ? robot.PartLabel(framingPart) : "Робот";

        public bool IsOccupied => view != null;
        // Tool reach must follow the robot now that zoom moves it instead of the camera.
        public float CameraOffset => view != null ? Vector3.Distance(view.position, overviewPosition)
            + Mathf.Max(0f, inspectionDistance - initialDistance) : 0f;
        public Collider TestButton => testButton;
        public RobotRepair Robot => robot;
        public bool HasRobot => robot != null && robot.isActiveAndEnabled;
        public DriveTestStand DriveStand { get; private set; }

        void Awake()
        {
            DriveStand = GetComponentInChildren<DriveTestStand>();
            robot = GetComponentInChildren<RobotRepair>();
            robotParent = robot != null ? robot.transform.parent : transform;
            robotPosition = robot != null ? robot.transform.localPosition : Vector3.zero;
            robotRotation = robot != null ? robot.transform.localRotation : Quaternion.identity;
            robotScale = robot != null ? robot.transform.localScale : Vector3.one;
            if (emptyIndicator != null) emptyIndicator.SetActive(!HasRobot);
        }

        public bool ClearRobot()
        {
            if (IsOccupied) return false;
            if (DriveStand != null) DriveStand.Cancel();
            if (robot != null)
            {
                robot.gameObject.SetActive(false);
                Destroy(robot.gameObject);
                robot = null;
            }
            if (emptyIndicator != null) emptyIndicator.SetActive(true);
            return true;
        }

        public RobotRepair LoadRobot(RobotRepair prefab)
        {
            if (!isActiveAndEnabled || prefab == null || !prefab.gameObject.activeSelf || !ClearRobot()) return null;
            robotPrefab = prefab;
            return SpawnRobot();
        }

        RobotRepair SpawnRobot()
        {
            robot = Instantiate(robotPrefab, robotParent);
            robot.transform.SetLocalPositionAndRotation(robotPosition, robotRotation);
            robot.transform.localScale = robotScale;
            if (emptyIndicator != null) emptyIndicator.SetActive(false);
            return robot;
        }

        public RobotRepair ResetRobot()
        {
            if (robotPrefab == null || robot == null) return null;
            if (DriveStand != null) DriveStand.Cancel();
            ReturnToOverview(true);
            var oldRobot = robot;
            oldRobot.gameObject.SetActive(false);
            SpawnRobot();
            Destroy(oldRobot.gameObject);
            Focus(null);
            return robot;
        }

        public bool Enter(Camera camera)
        {
            if (!HasRobot || IsOccupied || cameraAnchor == null || camera == null) return false;
            view = camera.transform;
            previousPosition = view.localPosition;
            previousRotation = view.localRotation;
            var player = camera.GetComponentInParent<PlayerController>();
            cameraMask = player != null ? ~(1 << player.gameObject.layer) : Physics.DefaultRaycastLayers;
            // Keep the camera under the player so disabling the table never reparents an inactive hierarchy.
            inspectionCamera = camera;
            overviewPosition = cameraAnchor.position;
            overviewRotation = cameraAnchor.rotation;
            cameraVelocity = Vector3.zero;
            IsFocused = false;
            liftedPart = null;
            Focus(null);
            return true;
        }

        public void Focus(RepairPart part)
        {
            if (!IsOccupied || !HasRobot || robot.IsTesting || robot.IsOnDriveStand || IsFocused) return;
            if (part != null && (!part.isActiveAndEnabled || part.GetComponentInParent<RobotRepair>() != robot)) return;
            foreach (RepairTool tool in GetComponentsInChildren<RepairTool>())
                if (tool.IsActive) return;
            ReturnToOverview(true);
            liftedPart = part != null && !part.IsInstalled ? part : null;
            inspectionRoot = liftedPart != null ? liftedPart.transform : robot.InspectionPivot;
            if (inspectionRoot == null) return;
            framingPart = part;
            Transform framingTarget = part != null ? part.transform : inspectionRoot;
            Bounds bounds = new(framingTarget.position, Vector3.zero);
            bool hasBounds = false;
            foreach (Renderer renderer in framingTarget.GetComponentsInChildren<Renderer>())
                if (renderer.enabled)
                {
                    if (hasBounds) bounds.Encapsulate(renderer.bounds);
                    else { bounds = renderer.bounds; hasBounds = true; }
                }
            if (part == null)
                foreach (RepairPart installed in robot.GetComponentsInChildren<RepairPart>())
                    if (installed.IsInstalled)
                        foreach (Renderer renderer in installed.GetComponentsInChildren<Renderer>())
                            if (renderer.enabled)
                            {
                                if (hasBounds) bounds.Encapsulate(renderer.bounds);
                                else { bounds = renderer.bounds; hasBounds = true; }
                            }
            sourcePosition = inspectionRoot.position;
            sourceRotation = inspectionRoot.rotation;
            inspectionPivot = bounds.center;
            liftHeight = liftedPart != null ? Mathf.Max(.06f, bounds.extents.magnitude - bounds.extents.y + .03f) : 0f;
            lift = 0f;
            framingCenter = bounds.center + Vector3.up * liftHeight;
            inspectionRotation = part == null ? cameraAnchor.rotation
                : Quaternion.Euler(liftedPart != null ? 55f : 25f, overviewRotation.eulerAngles.y, 0f);
            float halfFov = Mathf.Atan(Mathf.Tan(inspectionCamera.fieldOfView * Mathf.Deg2Rad * .5f)
                * Mathf.Min(1f, inspectionCamera.aspect));
            initialDistance = part == null ? Mathf.Max(.1f, Vector3.Distance(cameraAnchor.position, inspectionPivot))
                : Mathf.Max(.35f, bounds.extents.magnitude / Mathf.Sin(halfFov) * 1.1f);
            minimumDistance = Mathf.Min(initialDistance,
                Mathf.Max(bounds.extents.magnitude + inspectionCamera.nearClipPlane + .05f, initialDistance * .25f));
            maximumDistance = initialDistance * 2.5f;
            if (part == null) framingCenter = cameraAnchor.position + cameraAnchor.forward * initialDistance;
            inspectionDistance = initialDistance;
            objectRotation = Quaternion.identity;
            IsFocused = true;
        }

        public void ReturnToOverview(bool immediate = false)
        {
            IsFocused = false;
            if (!immediate) return;
            if (inspectionRoot != null)
            {
                if (liftedPart != null) liftedPart.AlignWithAnchor();
                else
                {
                    inspectionRoot.SetPositionAndRotation(sourcePosition, sourceRotation);
                    AlignInstalledParts();
                }
            }
            inspectionRoot = null;
            framingPart = null;
            liftedPart = null;
            lift = 0f;
            Physics.SyncTransforms();
        }

        internal void ReleaseInspectionPart(RepairPart part)
        {
            if (part != liftedPart) return;
            // A dragged part keeps its visible pose; the hand or stand now owns its motion.
            inspectionRoot = null;
            liftedPart = null;
        }

        public void AlignInspection()
        {
            if (!IsFocused || inspectionRoot == null) return;
            inspectionDistance = initialDistance;
            objectRotation = Quaternion.identity;
            ApplyInspectionPose();
        }

        void ApplyInspectionPose()
        {
            inspectionRoot.SetPositionAndRotation(inspectionPivot + Vector3.up * lift + inspectionRotation * Vector3.forward * (inspectionDistance - initialDistance)
                + objectRotation * (sourcePosition - inspectionPivot),
                objectRotation * sourceRotation);
            if (liftedPart == null) AlignInstalledParts();
            Physics.SyncTransforms();
        }

        void AlignInstalledParts()
        {
            if (robot == null) return;
            foreach (RepairPart part in robot.GetComponentsInChildren<RepairPart>())
                if (part.IsInstalled) part.AlignWithAnchor();
        }

        public bool UpdateView(Vector2 orbitDelta, float zoom)
        {
            if (view == null) return false;
            if (!HasRobot || inspectionRoot != null && !inspectionRoot.gameObject.activeInHierarchy)
                ReturnToOverview(true);
            if (liftedPart != null && (liftedPart.IsInstalled || liftedPart.IsDragging))
                ReleaseInspectionPart(liftedPart);
            bool objectMoved = false;
            if (IsFocused && !robot.IsTesting && !robot.IsOnDriveStand)
            {
                objectRotation = Quaternion.AngleAxis(-orbitDelta.x * .25f, inspectionRotation * Vector3.up)
                    * Quaternion.AngleAxis(orbitDelta.y * .25f, inspectionRotation * Vector3.right) * objectRotation;
                if (zoom != 0f) inspectionDistance = Mathf.Clamp(inspectionDistance * Mathf.Exp(-Mathf.Sign(zoom) * .15f), minimumDistance, maximumDistance);
                objectMoved = orbitDelta.sqrMagnitude > 0f || zoom != 0f;
            }
            if (inspectionRoot != null)
            {
                float nextLift = Mathf.MoveTowards(lift, IsFocused ? liftHeight : 0f,
                    liftHeight * Time.unscaledDeltaTime / (IsFocused ? .35f : .45f));
                objectMoved |= nextLift != lift;
                lift = nextLift;
                if (!IsFocused)
                {
                    objectRotation = Quaternion.RotateTowards(objectRotation, Quaternion.identity, 540f * Time.unscaledDeltaTime);
                    objectMoved |= Quaternion.Angle(objectRotation, Quaternion.identity) > .01f;
                }
                if (objectMoved || !IsFocused) ApplyInspectionPose();
                if (!IsFocused && lift == 0f && Quaternion.Angle(objectRotation, Quaternion.identity) < .01f)
                    ReturnToOverview(true);
            }
            Quaternion rotation = IsFocused ? inspectionRotation : overviewRotation;
            Vector3 position = IsFocused
                ? framingCenter - inspectionRotation * Vector3.forward * initialDistance : overviewPosition;
            bool moving = Vector3.Distance(view.position, position) > .001f || Quaternion.Angle(view.rotation, rotation) > .1f;
            if (!moving)
            {
                view.SetPositionAndRotation(position, rotation);
                cameraVelocity = Vector3.zero;
                return objectMoved;
            }
            position = Vector3.SmoothDamp(view.position, position, ref cameraVelocity,
                IsFocused ? .18f : .4f, Mathf.Infinity, Time.unscaledDeltaTime);
            Vector3 travel = position - view.position;
            // Check the swept camera volume, including the path between the overview and a tray.
            if (Physics.SphereCast(view.position, .08f, travel.normalized, out RaycastHit hit, travel.magnitude,
                cameraMask, QueryTriggerInteraction.Ignore))
                position = view.position + travel.normalized * Mathf.Max(0f, hit.distance - .01f);
            rotation = Quaternion.Slerp(view.rotation, rotation, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            bool changed = Vector3.Distance(view.position, position) > .00001f || Quaternion.Angle(view.rotation, rotation) > .01f;
            view.SetPositionAndRotation(position, rotation);
            return changed || objectMoved;
        }

        public void Exit(bool immediate = true)
        {
            if (DriveStand != null) DriveStand.Cancel();
            if (robot != null) robot.CancelTest();
            ReturnToOverview(true);
            if (view == null) return;
            var player = view.GetComponentInParent<PlayerController>();
            if (player != null) player.ReturnView(previousPosition, previousRotation, !immediate);
            else view.SetLocalPositionAndRotation(previousPosition, previousRotation);
            view = null;
            inspectionCamera = null;
        }

        void OnDisable() => Exit();
    }
}
