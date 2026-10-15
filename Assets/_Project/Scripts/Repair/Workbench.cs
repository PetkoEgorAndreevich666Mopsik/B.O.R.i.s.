using UnityEngine;

namespace BORis
{
    public sealed class Workbench : MonoBehaviour
    {
        [SerializeField] Transform cameraAnchor;
        [SerializeField] Collider testButton;
        [SerializeField] RobotRepair robotPrefab;
        RobotRepair robot;
        Transform view;
        Transform previousParent;
        Vector3 previousPosition;
        Quaternion previousRotation;

        public bool IsOccupied => view != null;
        public Collider TestButton => testButton;

        void Awake() => robot = GetComponentInChildren<RobotRepair>();

        public RobotRepair ResetRobot()
        {
            if (robotPrefab == null || robot == null) return null;
            var oldRobot = robot;
            oldRobot.gameObject.SetActive(false);
            robot = Instantiate(robotPrefab, oldRobot.transform.parent);
            robot.transform.SetLocalPositionAndRotation(oldRobot.transform.localPosition, oldRobot.transform.localRotation);
            robot.transform.localScale = oldRobot.transform.localScale;
            Destroy(oldRobot.gameObject);
            return robot;
        }

        public bool Enter(Camera camera)
        {
            if (IsOccupied || cameraAnchor == null || camera == null) return false;
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
