using System.Collections;
using UnityEngine;

namespace BORis
{
    public sealed class DriveTestStand : MonoBehaviour
    {
        [SerializeField] Transform socket;
        [SerializeField] Transform spindle;
        [SerializeField] TextMesh display;
        [SerializeField, Min(.1f)] float duration = 2.4f;
        Workbench workbench;
        RobotRepair robot;
        Coroutine routine;
        Quaternion spindleRest;

        public Transform Socket => socket;
        public RepairPart Part { get; private set; }
        public RepairPart LastTestedPart { get; private set; }
        public bool IsTesting => routine != null;
        public string Status => IsTesting ? "Стенд: проверка вращения… · ESC — отменить"
            : Part != null ? "Привод на стенде · ЛКМ по стенду — тест · ESC — вернуть"
            : LastTestedPart != null ? $"{LastTestedPart.DriveIdentity}: {LastTestedPart.StandObservation}"
            : "Стенд питает привод отдельно от робота. Перенесите сюда снятый привод";

        void Awake()
        {
            workbench = GetComponentInParent<Workbench>();
            if (spindle != null) spindleRest = spindle.localRotation;
            if (spindle != null) spindle.gameObject.SetActive(false);
        }

        void Update()
        {
            if (robot != null && (Part == null || !Part.isActiveAndEnabled || !robot.isActiveAndEnabled
                || workbench == null || workbench.Robot != robot || !workbench.IsOccupied)) Cancel();
            if (display != null) display.text = IsTesting ? "ВРАЩЕНИЕ…"
                : Part != null ? "ЛКМ — ТЕСТ"
                : LastTestedPart != null ? LastTestedPart.StandResult == DriveTestResult.Smooth ? "РОВНОЕ ВРАЩЕНИЕ" : "ВРАЩЕНИЕ ПРЕРЫВАЕТСЯ"
                : "СТЕНД ПРИВОДА";
        }

        public bool CanLoad(RobotRepair owner, RepairPart part, out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled || socket == null || workbench == null || !workbench.isActiveAndEnabled
                || !workbench.IsOccupied || workbench.IsFocused || owner == null || workbench.Robot != owner
                || !owner.Owns(part)) reason = "Стенд принимает только деталь текущего робота";
            else if (part.Kind != RepairPartKind.Drive) reason = "Стенд предназначен для привода";
            else if (part.IsInstalled) reason = "Сначала снимите привод";
            else if (Part != null || owner.IsOnDriveStand || owner.IsTesting) reason = "Дождитесь завершения текущей проверки";
            else
                foreach (RepairTool tool in workbench.GetComponentsInChildren<RepairTool>())
                    if (tool.IsActive) { reason = "Сначала закончите работу инструментом"; break; }
            return reason.Length == 0;
        }

        public bool TryLoad(RobotRepair owner, RepairPart part, out string reason)
        {
            if (!CanLoad(owner, part, out reason)) return false;
            robot = owner;
            Part = part;
            owner.ActiveDriveStand = this;
            if (spindle != null) spindle.gameObject.SetActive(true);
            part.SetDragging(true);
            part.transform.SetPositionAndRotation(socket.position, socket.rotation);
            return true;
        }

        public bool RunTest(out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled || Part == null || robot == null || !Part.isActiveAndEnabled
                || !robot.isActiveAndEnabled || robot.ActiveDriveStand != this) reason = "Перенесите снятый привод в гнездо стенда";
            else if (IsTesting) reason = "Проверка уже выполняется";
            if (reason.Length > 0) return false;
            routine = StartCoroutine(TestDrive());
            return true;
        }

        IEnumerator TestDrive()
        {
            float elapsed = 0f;
            float angle = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                bool turning = !Part.Faulty || Mathf.Repeat(elapsed, .8f) < .22f;
                if (turning) angle += Time.unscaledDeltaTime * 240f;
                if (spindle != null) spindle.localRotation = spindleRest * Quaternion.AngleAxis(angle, Vector3.forward);
                Part.transform.position = socket.position + (Part.Faulty ? socket.right * Mathf.Sin(elapsed * 45f) * .004f : Vector3.zero);
                yield return null;
            }
            Part.StandResult = Part.Faulty ? DriveTestResult.Interrupted : DriveTestResult.Smooth;
            LastTestedPart = Part;
            routine = null;
            ReleasePart();
        }

        void ReleasePart()
        {
            if (Part != null) Part.RestorePose();
            if (robot != null && robot.ActiveDriveStand == this) robot.ActiveDriveStand = null;
            Part = null;
            robot = null;
            if (spindle != null)
            {
                spindle.localRotation = spindleRest;
                spindle.gameObject.SetActive(false);
            }
        }

        public void Cancel()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            ReleasePart();
        }

        void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
        void OnDisable() => Cancel();
    }
}
