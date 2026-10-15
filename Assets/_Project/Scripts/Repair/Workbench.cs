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
            float eyeHeight = view.position.y;
            view.SetParent(cameraAnchor, false);
            view.localPosition = Vector3.zero;
            view.position = new Vector3(view.position.x, eyeHeight, view.position.z);
            view.localRotation = Quaternion.identity;
            return true;
        }

        public void Exit()
        {
            if (robot != null) robot.CancelTest();
            if (view == null) return;
            view.SetParent(previousParent, false);
            view.localPosition = previousPosition;
            view.localRotation = previousRotation;
            view = null;
        }

        void OnDisable() => Exit();
    }
}
