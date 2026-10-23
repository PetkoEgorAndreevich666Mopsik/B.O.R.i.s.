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
        Transform previousParent;
        Vector3 previousPosition;
        Quaternion previousRotation;
        Transform robotParent;
        Vector3 robotPosition;
        Quaternion robotRotation;
        Vector3 robotScale;
        Camera inspectionCamera;
        Vector3 overviewPosition, inspectionPivot, framingCenter;
        Quaternion overviewRotation, objectRotation, initialObjectRotation;
        float yaw, pitch, inspectionDistance, minimumDistance;
        float initialDistance, maximumDistance;
        Transform[] inspectionRoots;
        Vector3[] sourcePositions;
        Quaternion[] sourceRotations;
        Collider[] inspectionColliders;
        bool[] colliderStates;
        int cameraMask;

        public bool IsFocused { get; private set; }
        public RepairPart FocusedPart { get; private set; }
        public string InspectionName => FocusedPart != null && robot != null ? robot.PartLabel(FocusedPart) : "Робот";

        public bool IsOccupied => view != null;
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
            ReturnToOverview();
            var oldRobot = robot;
            oldRobot.gameObject.SetActive(false);
            SpawnRobot();
            Destroy(oldRobot.gameObject);
            return robot;
        }

        public bool Enter(Camera camera)
        {
            if (!HasRobot || IsOccupied || cameraAnchor == null || camera == null) return false;
            view = camera.transform;
            previousParent = view.parent;
            previousPosition = view.localPosition;
            previousRotation = view.localRotation;
            var player = camera.GetComponentInParent<PlayerController>();
            cameraMask = player != null ? ~(1 << player.gameObject.layer) : Physics.DefaultRaycastLayers;
            float eyeHeight = view.position.y;
            view.SetParent(cameraAnchor, false);
            view.localPosition = Vector3.zero;
            view.position = new Vector3(view.position.x, eyeHeight, view.position.z);
            view.localRotation = Quaternion.identity;
            inspectionCamera = camera;
            overviewPosition = view.position;
            overviewRotation = view.rotation;
            IsFocused = false;
            FocusedPart = null;
            return true;
        }

        public void Focus(RepairPart part)
        {
            if (!IsOccupied || !HasRobot || robot.IsTesting || robot.IsOnDriveStand || IsFocused) return;
            if (part != null && (!part.isActiveAndEnabled || part.GetComponentInParent<RobotRepair>() != robot)) return;
            foreach (RepairTool tool in GetComponentsInChildren<RepairTool>())
                if (tool.IsActive) return;
            FocusedPart = part != null && !part.IsInstalled ? part : null;
            var roots = new List<Transform>();
            if (FocusedPart != null) roots.Add(FocusedPart.transform);
            else
            {
                if (robot.InspectionPivot == null) return;
                roots.Add(robot.InspectionPivot);
                foreach (RepairPart installed in robot.GetComponentsInChildren<RepairPart>())
                    if (installed.IsInstalled) roots.Add(installed.transform);
            }

            inspectionRoots = roots.ToArray();
            sourcePositions = new Vector3[roots.Count];
            sourceRotations = new Quaternion[roots.Count];
            var colliders = new List<Collider>();
            Bounds bounds = new(roots[0].position, Vector3.zero);
            for (int i = 0; i < roots.Count; i++)
            {
                sourcePositions[i] = roots[i].position;
                sourceRotations[i] = roots[i].rotation;
                foreach (Renderer renderer in roots[i].GetComponentsInChildren<Renderer>())
                    if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
                colliders.AddRange(roots[i].GetComponentsInChildren<Collider>(true));
            }
            inspectionColliders = colliders.ToArray();
            colliderStates = new bool[inspectionColliders.Length];
            for (int i = 0; i < inspectionColliders.Length; i++)
            {
                colliderStates[i] = inspectionColliders[i].enabled;
                inspectionColliders[i].enabled = false;
            }

            inspectionPivot = FocusedPart != null ? bounds.center : robot.InspectionPivot.position;
            framingCenter = bounds.center;
            float halfFov = Mathf.Atan(Mathf.Tan(inspectionCamera.fieldOfView * Mathf.Deg2Rad * .5f)
                * Mathf.Min(1f, inspectionCamera.aspect));
            initialDistance = Mathf.Max(.35f, bounds.extents.magnitude / Mathf.Sin(halfFov) * 1.1f);
            minimumDistance = Mathf.Max(bounds.extents.magnitude + inspectionCamera.nearClipPlane + .05f, initialDistance * .75f);
            maximumDistance = initialDistance * 1.8f;
            initialDistance = Mathf.Max(initialDistance, minimumDistance);
            inspectionDistance = initialDistance;
            yaw = pitch = 0f;
            initialObjectRotation = FocusedPart != null
                ? Quaternion.FromToRotation(part.transform.forward, -(overviewRotation * Vector3.forward))
                : Quaternion.identity;
            objectRotation = initialObjectRotation;
            IsFocused = true;
            ApplyInspectionPose();
        }

        public void ReturnToOverview()
        {
            if (inspectionRoots != null)
                for (int i = 0; i < inspectionRoots.Length; i++)
                    if (inspectionRoots[i] != null)
                        inspectionRoots[i].SetPositionAndRotation(sourcePositions[i], sourceRotations[i]);
            if (inspectionColliders != null)
                for (int i = 0; i < inspectionColliders.Length; i++)
                    if (inspectionColliders[i] != null) inspectionColliders[i].enabled = colliderStates[i];
            inspectionRoots = null;
            sourcePositions = null;
            sourceRotations = null;
            inspectionColliders = null;
            colliderStates = null;
            IsFocused = false;
            FocusedPart = null;
        }

        public void AlignInspection()
        {
            if (!IsFocused) return;
            yaw = pitch = 0f;
            inspectionDistance = initialDistance;
            objectRotation = initialObjectRotation;
            ApplyInspectionPose();
        }

        void ApplyInspectionPose()
        {
            Vector3 translation = FocusedPart != null
                ? overviewPosition + overviewRotation * new Vector3(.12f, .04f, inspectionDistance) - inspectionPivot
                : Vector3.zero;
            // Keep every part under RobotRepair: ownership and repair anchors depend on that hierarchy.
            for (int i = 0; i < inspectionRoots.Length; i++)
                if (inspectionRoots[i] != null)
                    inspectionRoots[i].SetPositionAndRotation(inspectionPivot + translation
                        + objectRotation * (sourcePositions[i] - inspectionPivot), objectRotation * sourceRotations[i]);
        }

        public bool UpdateView(Vector2 orbitDelta, float zoom)
        {
            if (view == null) return false;
            if (IsFocused && (!HasRobot || inspectionRoots[0] == null || !inspectionRoots[0].gameObject.activeInHierarchy))
                ReturnToOverview();
            bool objectMoved = false;
            if (IsFocused)
            {
                if (FocusedPart != null)
                    objectRotation = Quaternion.AngleAxis(-orbitDelta.x * .25f, overviewRotation * Vector3.up)
                        * Quaternion.AngleAxis(orbitDelta.y * .25f, overviewRotation * Vector3.right) * objectRotation;
                else
                {
                    yaw = Mathf.Repeat(yaw - orbitDelta.x * .25f, 360f);
                    pitch = Mathf.Clamp(pitch + orbitDelta.y * .25f, -12f, 12f);
                    objectRotation = Quaternion.AngleAxis(pitch, overviewRotation * Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);
                }
                if (zoom != 0f) inspectionDistance = Mathf.Clamp(inspectionDistance * Mathf.Exp(-Mathf.Sign(zoom) * .15f), minimumDistance, maximumDistance);
                ApplyInspectionPose();
                objectMoved = orbitDelta.sqrMagnitude > 0f || zoom != 0f;
            }
            Quaternion rotation = overviewRotation;
            Vector3 position = IsFocused && FocusedPart == null
                ? framingCenter - overviewRotation * Vector3.forward * inspectionDistance : overviewPosition;
            bool moving = Vector3.Distance(view.position, position) > .001f || Quaternion.Angle(view.rotation, rotation) > .1f;
            if (!moving)
            {
                view.SetPositionAndRotation(position, rotation);
                return objectMoved;
            }
            float blend = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
            position = Vector3.Lerp(view.position, position, blend);
            Vector3 travel = position - view.position;
            // Check the swept camera volume, including the path between the overview and a tray.
            if (Physics.SphereCast(view.position, .08f, travel.normalized, out RaycastHit hit, travel.magnitude,
                cameraMask, QueryTriggerInteraction.Ignore))
                position = view.position + travel.normalized * Mathf.Max(0f, hit.distance - .01f);
            rotation = Quaternion.Slerp(view.rotation, rotation, blend);
            bool changed = Vector3.Distance(view.position, position) > .00001f || Quaternion.Angle(view.rotation, rotation) > .01f;
            view.SetPositionAndRotation(position, rotation);
            return changed || objectMoved;
        }

        public void Exit()
        {
            if (DriveStand != null) DriveStand.Cancel();
            if (robot != null) robot.CancelTest();
            ReturnToOverview();
            if (view == null) return;
            view.SetParent(previousParent, false);
            view.localPosition = previousPosition;
            view.localRotation = previousRotation;
            view = null;
            inspectionCamera = null;
        }

        void OnDisable() => Exit();
    }
}
