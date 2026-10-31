using System.Collections.Generic;
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
        Vector3 overviewPosition, inspectionPivot, cameraVelocity;
        Transform inspectionRoot;
        RepairPart framingPart, liftedPart;
        Quaternion overviewRotation, inspectionRotation, initialInspectionRotation;
        float orbitPitch, orbitYaw;
        float inspectionDistance, minimumDistance;
        float initialDistance, maximumDistance;
        int cameraMask;
        readonly List<GameObject> transportHidden = new();

        public bool IsFocused { get; private set; }
        public RepairPart FocusedPart => IsFocused ? liftedPart : null;
        public string InspectionName => framingPart != null && robot != null ? robot.PartLabel(framingPart) : "Робот";

        public bool IsOccupied => view != null;
        public float CameraOffset => view != null ? Vector3.Distance(view.position, overviewPosition) : 0f;
        public Collider TestButton => testButton;
        public RobotRepair Robot => robot;
        public bool RobotOnTable { get; private set; } = true;
        public bool HasRobot => RobotOnTable && robot != null && robot.isActiveAndEnabled;
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
            transportHidden.Clear();
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
            RobotOnTable = true;
            robot = Instantiate(robotPrefab, robotParent);
            robot.transform.SetLocalPositionAndRotation(robotPosition, robotRotation);
            robot.transform.localScale = robotScale;
            SynchronizeRobotPhysics();
            if (emptyIndicator != null) emptyIndicator.SetActive(false);
            return robot;
        }

        public void PositionRobot(Transform destination)
        {
            if (robot == null || IsOccupied) return;
            RobotOnTable = destination == null;
            foreach (GameObject hidden in transportHidden) if (hidden != null) hidden.SetActive(true);
            transportHidden.Clear();
            robot.transform.SetLocalPositionAndRotation(robotPosition, robotRotation);
            robot.transform.localScale = robotScale;
            foreach (Transform child in robot.transform)
            {
                RepairPart part = child.GetComponent<RepairPart>();
                if (!RobotOnTable && child.gameObject.activeSelf && child != robot.InspectionPivot && (part == null || !part.IsInstalled))
                {
                    transportHidden.Add(child.gameObject);
                    child.gameObject.SetActive(false);
                }
            }
            if (destination != null)
            {
                robot.transform.rotation = destination.rotation;
                SynchronizeRobotPhysics();
                Bounds bounds = robot.InspectionPivot.GetComponentInChildren<Renderer>().bounds;
                foreach (Renderer renderer in robot.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                robot.transform.position += destination.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            }
            SynchronizeRobotPhysics();
            if (emptyIndicator != null) emptyIndicator.SetActive(!RobotOnTable);
            Physics.SyncTransforms();
        }

        internal void SynchronizeRobotPhysics()
        {
            if (robot == null) return;
            foreach (RepairPart part in robot.GetComponentsInChildren<RepairPart>())
                part.SynchronizePhysicsPose();
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
            if (!IsOccupied || !HasRobot || robot.IsTesting || robot.IsOnDriveStand) return;
            if (part != null && (!part.isActiveAndEnabled || part.GetComponentInParent<RobotRepair>() != robot)) return;
            foreach (RepairTool tool in GetComponentsInChildren<RepairTool>())
                if (tool.IsActive) return;
            ReturnToOverview(true);
            liftedPart = part != null && !part.IsInstalled ? part : null;
            inspectionRoot = liftedPart != null ? liftedPart.transform : robot.InspectionPivot;
            if (inspectionRoot == null) return;
            if (liftedPart != null) liftedPart.SetSupported(true);
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
            inspectionPivot = bounds.center;
            inspectionRotation = part == null ? cameraAnchor.rotation
                : Quaternion.Euler(liftedPart != null ? 55f : 25f, overviewRotation.eulerAngles.y, 0f);
            initialInspectionRotation = inspectionRotation;
            orbitPitch = inspectionRotation.eulerAngles.x;
            orbitYaw = inspectionRotation.eulerAngles.y;
            float halfFov = Mathf.Atan(Mathf.Tan(inspectionCamera.fieldOfView * Mathf.Deg2Rad * .5f)
                * Mathf.Min(1f, inspectionCamera.aspect));
            initialDistance = part == null ? Mathf.Max(.1f, Vector3.Distance(cameraAnchor.position, inspectionPivot))
                : Mathf.Max(.35f, bounds.extents.magnitude / Mathf.Sin(halfFov) * 1.1f);
            minimumDistance = Mathf.Min(initialDistance,
                Mathf.Max(bounds.extents.magnitude + inspectionCamera.nearClipPlane + .05f, initialDistance * .25f));
            maximumDistance = initialDistance * 2.5f;
            if (part == null) inspectionPivot = cameraAnchor.position + cameraAnchor.forward * initialDistance;
            inspectionDistance = initialDistance;
            IsFocused = true;
        }

        public void ReturnToOverview(bool immediate = false)
        {
            IsFocused = false;
            if (liftedPart != null) liftedPart.SetSupported(false);
            inspectionRoot = null;
            framingPart = null;
            liftedPart = null;
        }

        internal void ReleaseInspectionPart(RepairPart part)
        {
            if (part != liftedPart) return;
            liftedPart.SetSupported(false);
            inspectionRoot = null;
            liftedPart = null;
        }

        public void AlignInspection()
        {
            if (!IsFocused || inspectionRoot == null) return;
            inspectionDistance = initialDistance;
            inspectionRotation = initialInspectionRotation;
            orbitPitch = inspectionRotation.eulerAngles.x;
            orbitYaw = inspectionRotation.eulerAngles.y;
        }

        public bool UpdateView(Vector2 orbitDelta, float zoom)
        {
            if (view == null) return false;
            if (!HasRobot || inspectionRoot != null && !inspectionRoot.gameObject.activeInHierarchy)
                ReturnToOverview(true);
            if (liftedPart != null && (liftedPart.IsInstalled || liftedPart.IsDragging))
                ReleaseInspectionPart(liftedPart);
            bool orbitChanged = false;
            if (IsFocused && !robot.IsTesting && !robot.IsOnDriveStand)
            {
                if (orbitDelta.sqrMagnitude > 0f)
                {
                    orbitYaw += orbitDelta.x * .25f;
                    orbitPitch = Mathf.Clamp(orbitPitch - orbitDelta.y * .25f, 15f, 80f);
                    inspectionRotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
                }
                if (zoom != 0f) inspectionDistance = Mathf.Clamp(inspectionDistance * Mathf.Exp(-Mathf.Sign(zoom) * .15f), minimumDistance, maximumDistance);
                orbitChanged = orbitDelta.sqrMagnitude > 0f || zoom != 0f;
            }
            Quaternion rotation = IsFocused ? inspectionRotation : overviewRotation;
            Vector3 position = IsFocused
                ? inspectionPivot - inspectionRotation * Vector3.forward * inspectionDistance : overviewPosition;
            bool moving = Vector3.Distance(view.position, position) > .001f || Quaternion.Angle(view.rotation, rotation) > .1f;
            if (!moving)
            {
                view.SetPositionAndRotation(position, rotation);
                cameraVelocity = Vector3.zero;
                return orbitChanged;
            }
            if (IsFocused)
            {
                Vector3 offset = view.position - inspectionPivot;
                Quaternion from = offset.sqrMagnitude > .0001f ? Quaternion.LookRotation(-offset, Vector3.up) : rotation;
                float step = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
                position = inspectionPivot - Quaternion.Slerp(from, rotation, step) * Vector3.forward
                    * Mathf.Lerp(offset.magnitude, inspectionDistance, step);
            }
            else position = Vector3.SmoothDamp(view.position, position, ref cameraVelocity,
                .4f, Mathf.Infinity, Time.unscaledDeltaTime);
            Vector3 travel = position - view.position;
            // Sweep the camera so orbiting cannot cross the bench or room walls.
            if (Physics.SphereCast(view.position, .08f, travel.normalized, out RaycastHit hit, travel.magnitude,
                cameraMask, QueryTriggerInteraction.Ignore))
                position = view.position + travel.normalized * Mathf.Max(0f, hit.distance - .01f);
            rotation = Quaternion.Slerp(view.rotation, rotation, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            bool changed = Vector3.Distance(view.position, position) > .00001f || Quaternion.Angle(view.rotation, rotation) > .01f;
            view.SetPositionAndRotation(position, rotation);
            return changed || orbitChanged;
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
