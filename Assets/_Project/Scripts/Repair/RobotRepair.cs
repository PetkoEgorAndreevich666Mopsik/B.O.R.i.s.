using System.Collections;
using UnityEngine;

namespace BORis
{
    public enum RobotTestResult { NotTested, Faulty, Passed }

    public sealed class RobotRepair : MonoBehaviour
    {
        [SerializeField] RepairPart cover;
        [SerializeField] RepairPart[] drives = System.Array.Empty<RepairPart>();
        [SerializeField] RepairConnector connector;
        [SerializeField] Transform shoulder;
        [SerializeField] SolderJoint solderJoint;
        [SerializeField, Min(.1f)] float testDuration = 1.8f;
        Coroutine testRoutine;
        Quaternion restingShoulder;

        public bool IsTesting => testRoutine != null;
        public RobotTestResult TestResult { get; private set; }
        public bool IsFunctional => InstalledDrive != null && !InstalledDrive.Faulty
            && (solderJoint == null || solderJoint.IsRepaired);
        public SolderJoint SolderJoint => solderJoint;
        public bool JointFaultFound { get; private set; }
        public DirtPatch[] DirtPatches { get; private set; }
        public RepairPart InspectedDrive { get; private set; }
        public bool HasInspectedDrive => InspectedDrive != null;
        public bool DriveFaultFound { get; private set; }

        public string PartLabel(RepairPart part) => part != null
            ? part.Kind == RepairPartKind.Drive && part.Faulty && !DriveFaultFound ? "Привод руки" : part.DisplayName
            : "";

        public bool InspectDrive(RepairPart part)
        {
            if (!Owns(part) || IsTesting || cover == null || cover.IsInstalled
                || part.Kind != RepairPartKind.Drive || !part.IsInstalled) return false;
            InspectedDrive = part;
            DriveFaultFound |= part.Faulty;
            return true;
        }

        public bool InspectJoint(SolderJoint joint)
        {
            if (joint == null || joint != solderJoint || !joint.isActiveAndEnabled || !isActiveAndEnabled
                || IsTesting || cover == null || cover.IsInstalled) return false;
            JointFaultFound |= !joint.IsRepaired;
            return true;
        }

        void Awake()
        {
            if (shoulder != null) restingShoulder = shoulder.localRotation;
            DirtPatches = GetComponentsInChildren<DirtPatch>(true);
        }

        public bool CheckCleaning(out string reason)
        {
            reason = "";
            if (DirtPatches.Length == 0) reason = "Не найдены поверхности для проверки очистки";
            foreach (DirtPatch patch in DirtPatches)
                if (patch == null || !patch.isActiveAndEnabled || !patch.IsClean)
                {
                    reason = patch != null ? $"Завершите очистку: {patch.DisplayName}" : "Поверхность для очистки недоступна";
                    break;
                }
            return reason.Length == 0;
        }

        public RepairPart Cover => cover;
        public RepairConnector Connector => connector;
        public RepairPart InstalledDrive
        {
            get
            {
                foreach (RepairPart drive in drives)
                    if (drive != null && drive.IsInstalled) return drive;
                return null;
            }
        }

        public bool CanTurn(ScrewFastener screw, out string reason)
        {
            reason = "";
            if (screw == null || !screw.isActiveAndEnabled || !isActiveAndEnabled || screw.GetComponentInParent<RobotRepair>() != this)
                reason = "Винт недоступен";
            else if (IsTesting) reason = "Дождитесь окончания теста руки";
            else
            {
                RepairPart part = screw.GetComponentInParent<RepairPart>();
                if (part != null && !part.IsInstalled) reason = "Сначала установите деталь в робота";
                else if (part != null && part.Kind == RepairPartKind.Drive && cover != null && cover.IsInstalled)
                    reason = "Сначала снимите крышку";
            }
            return reason.Length == 0;
        }

        public float Turn(ScrewFastener screw, float clockwiseDegrees)
        {
            if (!CanTurn(screw, out _)) return 0f;
            float applied = screw.Turn(clockwiseDegrees);
            if (Mathf.Abs(applied) > .0001f)
            {
                InvalidateTest();
                if (cover != null && screw.GetComponentInParent<RepairPart>() == cover) cover.RefreshRelease();
            }
            return applied;
        }

        public bool CanClean(DirtPatch patch, out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled || patch == null || !patch.isActiveAndEnabled || patch.GetComponentInParent<RobotRepair>() != this)
                reason = "Поверхность недоступна";
            else if (IsTesting) reason = "Дождитесь окончания теста руки";
            else if (patch.RequiresOpenCover && cover != null && cover.IsInstalled)
                reason = "Сначала снимите крышку";
            return reason.Length == 0;
        }

        public bool CanSolder(SolderJoint joint, WorkshopSession session, out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled || joint == null || joint != solderJoint || !joint.isActiveAndEnabled
                || joint.GetComponentInParent<RobotRepair>() != this) reason = "Контакт недоступен";
            else if (session == null || !session.isActiveAndEnabled || !session.CanOfferSolderingOrders)
                reason = "Сначала приобретите паяльную станцию";
            else if (session.ActiveOrder == null || session.ActiveRobot != this) reason = "Этот робот не относится к текущему заказу";
            else if (IsTesting) reason = "Дождитесь окончания теста руки";
            else if (cover == null || cover.IsInstalled) reason = "Сначала снимите крышку";
            else if (connector == null || connector.IsConnected) reason = "Перед пайкой отсоедините разъём";
            else if (joint.IsRepaired) reason = "Контакт уже восстановлен";
            return reason.Length == 0;
        }

        public int SolderStroke(SolderJoint joint, WorkshopSession session, float from, float to)
        {
            if (!CanSolder(joint, session, out _)) return 0;
            int added = joint.ApplyStroke(from, to);
            if (added > 0) { JointFaultFound = true; InvalidateTest(); }
            return added;
        }

        bool Owns(RepairPart part) => part != null && part.isActiveAndEnabled && isActiveAndEnabled
            && part.GetComponentInParent<RobotRepair>() == this
            && (part == cover || System.Array.IndexOf(drives, part) >= 0)
            && part.InstallationAnchor != null && part.TrayAnchor != null;

        public bool CanMove(RepairPart part, out string reason)
        {
            reason = "";
            if (!Owns(part)) reason = "Деталь недоступна";
            else if (IsTesting) reason = "Дождитесь окончания теста руки";
            else if (part.Kind == RepairPartKind.Drive && cover.IsInstalled) reason = "Сначала снимите крышку";
            else if (part.Kind == RepairPartKind.Drive && connector.IsConnected) reason = "Сначала отсоедините разъём";
            else if (part.IsInstalled)
            {
                int remaining = 0;
                foreach (ScrewFastener screw in part.Screws) if (!screw.IsLoose) remaining++;
                if (remaining > 0) reason = $"Освободите винты: осталось {remaining}";
            }
            return reason.Length == 0;
        }

        public bool CanPlace(RepairPart part, bool inRobot, out string reason)
        {
            if (!CanMove(part, out reason)) return false;
            if (inRobot)
            {
                if (part.Kind == RepairPartKind.Drive && InstalledDrive != null && InstalledDrive != part)
                    reason = "Место занято: сначала уберите установленный привод в лоток";
                else if (part.Kind == RepairPartKind.Cover && InstalledDrive == null)
                    reason = "Сначала установите привод";
                else if (part.Kind == RepairPartKind.Cover && !FullyTightened(InstalledDrive))
                    reason = "Сначала затяните оба винта привода";
                else if (part.Kind == RepairPartKind.Cover && !connector.IsConnected)
                    reason = "Сначала подключите разъём";
            }
            return reason.Length == 0;
        }

        public bool TryPlace(RepairPart part, bool inRobot, out string reason)
        {
            if (!CanPlace(part, inRobot, out reason)) return false;
            if (part.IsInstalled != inRobot) InvalidateTest();
            part.Place(inRobot);
            return true;
        }

        public bool ToggleConnection(RepairConnector target, out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled || target == null || target != connector || !target.isActiveAndEnabled)
                reason = "Разъём недоступен";
            else if (IsTesting) reason = "Дождитесь окончания теста руки";
            else if (cover.IsInstalled) reason = "Сначала снимите крышку";
            else if (InstalledDrive == null) reason = "Сначала установите привод";
            else if (!connector.IsConnected && !FullyTightened(InstalledDrive)) reason = "Сначала затяните оба винта привода";
            if (reason.Length > 0) return false;
            InvalidateTest();
            connector.SetConnected(!connector.IsConnected);
            return true;
        }

        public bool CanRunTest(out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled || shoulder == null || cover == null || connector == null) reason = "Тест недоступен";
            else if (IsTesting) reason = "Тест руки уже выполняется";
            else if (InstalledDrive == null) reason = "Сначала установите привод";
            else if (!FullyTightened(InstalledDrive)) reason = "Сначала затяните оба винта привода";
            else if (!connector.IsConnected) reason = "Сначала подключите разъём";
            else if (!cover.IsInstalled) reason = "Сначала установите крышку";
            else if (!FullyTightened(cover)) reason = "Сначала затяните четыре винта крышки";
            return reason.Length == 0;
        }

        public bool RunTest(out string reason)
        {
            if (!CanRunTest(out reason)) return false;
            InvalidateTest();
            testRoutine = StartCoroutine(TestArm());
            return true;
        }

        IEnumerator TestArm()
        {
            bool functional = IsFunctional;
            float elapsed = 0f;
            while (elapsed < testDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / testDuration);
                float angle = functional ? 65f * Mathf.SmoothStep(0f, 1f, progress)
                    : 7f * Mathf.Sin(elapsed * 32f) * Mathf.Sin(progress * Mathf.PI);
                shoulder.localRotation = restingShoulder * Quaternion.AngleAxis(angle, Vector3.forward);
                yield return null;
            }
            shoulder.localRotation = restingShoulder * Quaternion.AngleAxis(functional ? 65f : 0f, Vector3.forward);
            TestResult = functional ? RobotTestResult.Passed : RobotTestResult.Faulty;
            testRoutine = null;
        }

        void InvalidateTest()
        {
            TestResult = RobotTestResult.NotTested;
            if (shoulder != null) shoulder.localRotation = restingShoulder;
        }

        public void CancelTest()
        {
            if (!IsTesting) return;
            StopCoroutine(testRoutine);
            testRoutine = null;
            InvalidateTest();
        }

        void OnDisable() => CancelTest();

        public static bool FullyTightened(RepairPart part)
        {
            if (part == null) return false;
            foreach (ScrewFastener screw in part.Screws) if (!screw.IsTight) return false;
            return true;
        }

        public string AssemblyHint
        {
            get
            {
                if (IsTesting) return "Проверка руки… дождитесь результата";
                if (TestResult == RobotTestResult.Passed) return "РЕМОНТ ЗАВЕРШЁН: рука исправна. Можно начать новый ремонт";
                if (TestResult == RobotTestResult.Faulty && !DriveFaultFound && !JointFaultFound)
                    return "Тест не пройден. Снимите крышку и осмотрите узел";
                RepairPart drive = InstalledDrive;
                if (cover.IsInstalled)
                    return cover.IsReleased ? "Возьмите свободную крышку и перенесите её в левый лоток"
                        : drive != null && !drive.Faulty && (solderJoint == null || solderJoint.IsRepaired)
                        ? FullyTightened(cover) ? "Сборка завершена: нажмите кнопку «ТЕСТ РУКИ»" : "Затяните четыре винта крышки"
                        : "Освободите винты сервисной крышки, чтобы осмотреть узел";
                if (drive == null) return "Перенесите исправный привод из зелёного лотка в робота";
                if (drive.Faulty && !DriveFaultFound)
                    return "Задержите указатель на открытом приводе для осмотра";
                if (drive.Faulty) return connector.IsConnected ? "Рукой отсоедините разъём справа от привода"
                    : "Освободите оба винта привода и перенесите его в красный лоток";
                if (solderJoint != null && !solderJoint.IsRepaired)
                    return !JointFaultFound ? "Осмотрите открытый узел: привод и дорожку контакта под ним"
                        : connector.IsConnected ? "1 — рука: отсоедините разъём перед пайкой"
                        : "4 — паяльник: удерживайте ЛКМ и проведите по всей дорожке";
                if (!FullyTightened(drive)) return "Затяните оба винта исправного привода";
                return connector.IsConnected ? "Верните крышку из левого лотка на робота" : "Рукой подключите разъём";
            }
        }
    }
}
