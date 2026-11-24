using System.Collections.Generic;
using UnityEngine;

namespace BORis
{
    public sealed class Workbench : MonoBehaviour
    {
        [SerializeField] Transform cameraAnchor;
        [SerializeField, Range(30f, 80f)] float inspectionFieldOfView = 45f;
        [SerializeField] float repairTilt = 60f;
        [SerializeField] Renderer[] inspectionOccluders = System.Array.Empty<Renderer>();
        bool[] occluderStates;
        readonly Dictionary<Collider, bool> occluderColliders = new();
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
        float previousFieldOfView;
        Vector3 partVelocity;
        Vector3 overviewPosition, inspectionPivot, cameraVelocity;
        Transform inspectionRoot;
        RepairPart framingPart, liftedPart;
        Quaternion overviewRotation;
        Vector3 inspectionStartPosition, inspectionCenterLocal;
        Quaternion inspectionStartRotation, inspectionDisplayRotation;
        Pose robotRestPose;
        Vector3 robotDisplayPosition;
        Quaternion robotDisplayRotation;
        bool hasRobotDisplayPosition;
        float inspectionDistance, minimumDistance;
        float initialDistance, maximumDistance;
        readonly List<GameObject> transportHidden = new();

        public bool IsFocused { get; private set; }
        public RepairPart FocusedPart => IsFocused ? liftedPart : null;
        public string InspectionName => framingPart != null && robot != null ? robot.PartLabel(framingPart) : "Робот";

        public bool IsOccupied => view != null;
        public float CameraOffset => view != null ? Mathf.Max(Vector3.Distance(view.position, overviewPosition),
            IsFocused ? Mathf.Max(0f, inspectionDistance - initialDistance) : 0f)
            + Mathf.Max(0f, initialDistance - 2.5f) : 0f;
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
            hasRobotDisplayPosition = false;
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
            robotRestPose = new Pose(robot.InspectionPivot.position, robot.InspectionPivot.rotation);
            hasRobotDisplayPosition = false;
            previousPosition = view.localPosition;
            previousRotation = view.localRotation;
            // Keep the camera under the player so disabling the table never reparents an inactive hierarchy.
            inspectionCamera = camera;
            previousFieldOfView = camera.fieldOfView;
            overviewPosition = cameraAnchor.position;
            overviewRotation = cameraAnchor.rotation;
            cameraVelocity = Vector3.zero;
            occluderStates = new bool[inspectionOccluders.Length];
            for (int i = 0; i < inspectionOccluders.Length; i++)
                if (inspectionOccluders[i] != null)
                {
                    occluderStates[i] = inspectionOccluders[i].forceRenderingOff;
                    inspectionOccluders[i].forceRenderingOff = true;
                    foreach (Collider collider in inspectionOccluders[i].GetComponents<Collider>())
                    {
                        occluderColliders.TryAdd(collider, collider.enabled);
                        collider.enabled = false;
                    }
                }
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
            Pose robotPose = new(robot.InspectionPivot.position, robot.InspectionPivot.rotation);
            ReturnToOverview(true);
            liftedPart = part != null && !part.IsInstalled ? part : null;
            if (liftedPart != null)
                robot.InspectionPivot.SetPositionAndRotation(robotPose.position, robotPose.rotation);
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
            inspectionPivot = liftedPart != null ? bounds.center : inspectionRoot.position;

            float halfFov = Mathf.Atan(Mathf.Tan(inspectionFieldOfView * Mathf.Deg2Rad * .5f)
                * Mathf.Min(1f, inspectionCamera.aspect));
            initialDistance = part == null ? Mathf.Max(.1f, Vector3.Distance(cameraAnchor.position, inspectionPivot))
                : Mathf.Max(.35f, bounds.extents.magnitude / Mathf.Sin(halfFov) * 1.1f);
            minimumDistance = Mathf.Min(initialDistance,
                Mathf.Max(bounds.extents.magnitude + inspectionCamera.nearClipPlane + .05f, initialDistance * .25f));
            maximumDistance = initialDistance + .55f;
            inspectionStartPosition = inspectionRoot.position;
            inspectionStartRotation = inspectionRoot.rotation;
            inspectionDisplayRotation = inspectionStartRotation;
            inspectionCenterLocal = inspectionRoot.InverseTransformPoint(inspectionPivot);
            inspectionDistance = initialDistance;
            partVelocity = Vector3.zero;
            if (liftedPart != null)
            {
                inspectionPivot = overviewPosition + overviewRotation * Vector3.forward * initialDistance;
                minimumDistance = initialDistance * .65f;
                if (liftedPart.Kind == RepairPartKind.Cover)
                {
                    bool innerSide = Vector3.Dot(inspectionRoot.forward, overviewPosition - bounds.center) > 0f;
                    inspectionDisplayRotation = overviewRotation * Quaternion.Euler(0f, innerSide ? 180f : 0f, 0f);
                    inspectionRoot.rotation = inspectionDisplayRotation;
                }
            }
            else
            {
                inspectionDisplayRotation = Quaternion.AngleAxis(repairTilt, transform.right) * inspectionStartRotation;
                Vector3 frameCenter = inspectionRoot.InverseTransformPoint(bounds.center);
                inspectionRoot.rotation = inspectionDisplayRotation;
                if (!hasRobotDisplayPosition)
                {
                    inspectionRoot.position += bounds.center - transform.up * .25f - inspectionRoot.TransformPoint(frameCenter);
                    robotDisplayPosition = inspectionRoot.position;
                    robotDisplayRotation = inspectionDisplayRotation;
                    hasRobotDisplayPosition = true;
                }
                inspectionRoot.rotation = robotDisplayRotation;
                inspectionPivot = robotDisplayPosition;
                inspectionRoot.position = inspectionPivot;
                initialDistance = Mathf.Max(.1f, Vector3.Distance(cameraAnchor.position, inspectionPivot));
                inspectionDistance = initialDistance;
                maximumDistance = initialDistance + .55f;
                minimumDistance = Mathf.Min(initialDistance,
                    Mathf.Max(bounds.extents.magnitude + inspectionCamera.nearClipPlane + .05f, initialDistance * .25f));
                SynchronizeRobotPhysics();
            }
            IsFocused = true;
        }

        public void ReturnToOverview(bool immediate = false)
        {
            if (inspectionRoot != null)
            {
                inspectionRoot.SetPositionAndRotation(inspectionStartPosition, inspectionStartRotation);
                SynchronizeRobotPhysics();
            }
            if (view != null && robot != null)
            {
                robot.InspectionPivot.SetPositionAndRotation(robotRestPose.position, robotRestPose.rotation);
                SynchronizeRobotPhysics();
            }
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
            IsFocused = false;
            Focus(null);
        }

        internal Pose BeginPlacement(RepairPart part)
        {
            Pose rest = new(part.transform.position, part.transform.rotation);
            if (part != liftedPart) return rest;
            rest = new Pose(inspectionStartPosition, inspectionStartRotation);
            inspectionRoot = null;
            liftedPart = null;
            framingPart = null;
            IsFocused = false;
            part.SetSupported(true);
            return rest;
        }

        public void AlignInspection()
        {
            if (!IsFocused || inspectionRoot == null) return;
            inspectionDistance = initialDistance;
            inspectionRoot.SetPositionAndRotation(inspectionStartPosition, inspectionDisplayRotation);
            if (liftedPart == null) robotDisplayRotation = inspectionDisplayRotation;
            SynchronizeRobotPhysics();
        }

        public bool UpdateView(Vector2 orbitDelta, float zoom)
        {
            if (view == null) return false;
            if (!HasRobot || inspectionRoot != null && !inspectionRoot.gameObject.activeInHierarchy)
                ReturnToOverview(true);
            if (liftedPart != null && (liftedPart.IsInstalled || liftedPart.IsDragging))
                ReleaseInspectionPart(liftedPart);
            bool changed = false;
            if (IsFocused && inspectionRoot != null && !robot.IsTesting && !robot.IsOnDriveStand)
            {
                if (orbitDelta.sqrMagnitude > 0f)
                {
                    Quaternion turn = Quaternion.AngleAxis(-orbitDelta.x * .35f, view.up)
                        * Quaternion.AngleAxis(orbitDelta.y * .35f, view.right);
                    inspectionRoot.rotation = turn * inspectionRoot.rotation;
                    if (liftedPart == null) robotDisplayRotation = inspectionRoot.rotation;
                    changed = true;
                }
                if (zoom != 0f)
                {
                    inspectionDistance = Mathf.Clamp(inspectionDistance * Mathf.Exp(-Mathf.Sign(zoom) * .15f), minimumDistance, maximumDistance);
                    changed = true;
                }
                Vector3 center = inspectionPivot + overviewRotation * Vector3.forward * (inspectionDistance - initialDistance);
                Vector3 target = inspectionRoot.position + center - inspectionRoot.TransformPoint(inspectionCenterLocal);
                inspectionRoot.position = liftedPart != null
                    ? Vector3.SmoothDamp(inspectionRoot.position, target, ref partVelocity, .14f,
                        Mathf.Infinity, Time.unscaledDeltaTime) : target;
                SynchronizeRobotPhysics();
            }
            if (changed) Physics.SyncTransforms();
            bool moving = Vector3.Distance(view.position, overviewPosition) > .001f
                || Quaternion.Angle(view.rotation, overviewRotation) > .1f;
            if (moving)
            {
                view.position = Vector3.SmoothDamp(view.position, overviewPosition, ref cameraVelocity, .2f,
                    Mathf.Infinity, Time.unscaledDeltaTime);
                view.rotation = Quaternion.Slerp(view.rotation, overviewRotation, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            }
            else view.SetPositionAndRotation(overviewPosition, overviewRotation);
            inspectionCamera.fieldOfView = Mathf.Lerp(inspectionCamera.fieldOfView, inspectionFieldOfView,
                1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            return moving || changed;
        }

        public void Exit(bool immediate = true)
        {
            if (DriveStand != null) DriveStand.Cancel();
            if (robot != null) robot.CancelTest();
            ReturnToOverview(true);
            if (view == null) return;
            for (int i = 0; i < inspectionOccluders.Length; i++)
                if (inspectionOccluders[i] != null) inspectionOccluders[i].forceRenderingOff = occluderStates[i];
            foreach (var collider in occluderColliders)
                if (collider.Key != null) collider.Key.enabled = collider.Value;
            occluderColliders.Clear();
            var player = view.GetComponentInParent<PlayerController>();
            if (player != null) player.ReturnView(previousPosition, previousRotation, !immediate, previousFieldOfView);
            else
            {
                view.SetLocalPositionAndRotation(previousPosition, previousRotation);
                inspectionCamera.fieldOfView = previousFieldOfView;
            }
            view = null;
            inspectionCamera = null;
        }

        void OnDisable() => Exit();
    }
}
