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
        Vector3 overviewPosition, inspectionPivot;
        Quaternion overviewRotation;
        float yaw, pitch, inspectionDistance, minimumDistance;
        int cameraMask;

        public bool IsFocused { get; private set; }
        public RepairPart FocusedPart { get; private set; }
        public string InspectionName => FocusedPart != null && robot != null ? robot.PartLabel(FocusedPart) : "Робот";

        public bool IsOccupied => view != null;
        public Collider TestButton => testButton;
        public RobotRepair Robot => robot;
        public bool HasRobot => robot != null && robot.isActiveAndEnabled;

        void Awake()
        {
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
            if (!IsOccupied || !HasRobot) return;
            FocusedPart = part != null && !part.IsInstalled ? part : null;
            Bounds bounds;
            if (FocusedPart != null)
            {
                bounds = new Bounds(part.transform.position, Vector3.zero);
                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                // The robot owns its trays too; frame the repair socket instead of their combined bounds.
                Collider socket = robot.Cover.InstallationAnchor.GetComponent<Collider>();
                bounds = socket.bounds;
            }
            inspectionPivot = bounds.center;
            minimumDistance = Mathf.Max(.35f, bounds.extents.magnitude + .12f);
            float halfFov = Mathf.Atan(Mathf.Tan(inspectionCamera.fieldOfView * Mathf.Deg2Rad * .5f)
                * Mathf.Min(1f, inspectionCamera.aspect));
            inspectionDistance = Mathf.Clamp(bounds.extents.magnitude / Mathf.Sin(halfFov) * 1.15f,
                minimumDistance, 2.5f);
            yaw = cameraAnchor.eulerAngles.y;
            pitch = FocusedPart != null && Mathf.Abs(Vector3.Dot(part.transform.forward, Vector3.up)) > .6f ? 60f : 20f;
            IsFocused = true;
        }

        public void ReturnToOverview()
        {
            IsFocused = false;
            FocusedPart = null;
        }

        public bool UpdateView(Vector2 orbitDelta, float zoom)
        {
            if (view == null) return false;
            if (IsFocused)
            {
                yaw = Mathf.Clamp(yaw + orbitDelta.x * .2f, cameraAnchor.eulerAngles.y - 85f, cameraAnchor.eulerAngles.y + 85f);
                pitch = Mathf.Clamp(pitch - orbitDelta.y * .2f, 10f, 80f);
                if (zoom != 0f) inspectionDistance = Mathf.Clamp(inspectionDistance * Mathf.Exp(-Mathf.Sign(zoom) * .15f), minimumDistance, 2.5f);
            }
            Quaternion rotation = IsFocused ? Quaternion.Euler(pitch, yaw, 0f) : overviewRotation;
            Vector3 position = IsFocused ? inspectionPivot - rotation * Vector3.forward * inspectionDistance : overviewPosition;
            bool moving = Vector3.Distance(view.position, position) > .001f || Quaternion.Angle(view.rotation, rotation) > .1f;
            if (!moving) return false;
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
            return changed;
        }

        public void Exit()
        {
            if (robot != null) robot.CancelTest();
            if (view == null) return;
            view.SetParent(previousParent, false);
            view.localPosition = previousPosition;
            view.localRotation = previousRotation;
            view = null;
            inspectionCamera = null;
            ReturnToOverview();
        }

        void OnDisable() => Exit();
    }
}
