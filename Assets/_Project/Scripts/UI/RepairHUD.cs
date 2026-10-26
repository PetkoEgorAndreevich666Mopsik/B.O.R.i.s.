using UnityEngine;

namespace BORis
{
    public sealed class RepairHUD : MonoBehaviour
    {
        [SerializeField] UnityEngine.UI.Text hint;
        [SerializeField] UnityEngine.UI.Text assemblyState;
        [SerializeField] UnityEngine.UI.Text symptom;
        [SerializeField] UnityEngine.UI.Text controls;
        [SerializeField] GameObject questPanel;
        [SerializeField] ContextHintView context;
        [SerializeField] GameObject toolsPanel;
        [SerializeField] UnityEngine.UI.Image[] toolSlots;
        [SerializeField] UnityEngine.UI.Text cameraHints;
        [SerializeField] UnityEngine.UI.Button collapseButton;
        [SerializeField] UnityEngine.UI.Text collapseLabel;
        [SerializeField] GameObject helpPanel;
        [SerializeField] UnityEngine.UI.Button diagnosisButton;
        [SerializeField] GameObject diagnosisPanel;
        [SerializeField] UnityEngine.UI.Text diagnosisText;
        [SerializeField] UnityEngine.UI.Button diagnosisHelpButton;
        [SerializeField] UnityEngine.UI.Button closeDiagnosisButton;
        RectTransform questRect;
        CanvasGroup toolsGroup;
        Vector2 questSize, symptomSize, assemblySize, assemblyPosition;
        bool compactNow, showHelp;
        bool? collapsedOverride;
        string previousFeedback;
        Transform feedbackTarget;
        RobotRepair previousRobot;
        UnityEngine.UI.Text diagnosisHelpLabel;
        const string Accent = "#A6E3CD";
        int diagnosisHelpLevel;
        bool showDiagnosis;
        public bool IsHelpOpen => showHelp;

        void Awake()
        {
            if (collapseButton != null) collapseButton.onClick.AddListener(ToggleDetails);
            if (diagnosisButton != null) diagnosisButton.onClick.AddListener(ToggleDiagnosis);
            if (diagnosisHelpButton != null) diagnosisHelpButton.onClick.AddListener(RevealDiagnosisHelp);
            if (diagnosisHelpButton != null) diagnosisHelpLabel = diagnosisHelpButton.GetComponentInChildren<UnityEngine.UI.Text>();
            if (closeDiagnosisButton != null) closeDiagnosisButton.onClick.AddListener(CloseDiagnosisPanel);
            if (toolsPanel != null) toolsGroup = toolsPanel.GetComponent<CanvasGroup>();
            questRect = questPanel != null ? questPanel.GetComponent<RectTransform>() : null;
            if (questRect != null) questSize = questRect.sizeDelta;
            if (symptom != null) symptomSize = symptom.rectTransform.sizeDelta;
            if (assemblyState != null)
            {
                assemblySize = assemblyState.rectTransform.sizeDelta;
                assemblyPosition = assemblyState.rectTransform.anchoredPosition;
            }
        }

        void SetCompact(bool compact)
        {
            float width = Mathf.Min(questSize.x, 260f);
            if (questRect != null) questRect.sizeDelta = compact ? new Vector2(width, 96f) : questSize;
            if (symptom != null) symptom.rectTransform.sizeDelta = compact ? new Vector2(width - 48f, 40f) : symptomSize;
            if (assemblyState == null) return;
            assemblyState.rectTransform.sizeDelta = compact ? new Vector2(width - 32f, 28f) : assemblySize;
            assemblyState.rectTransform.anchoredPosition = compact ? new Vector2(assemblyPosition.x, -54f) : assemblyPosition;
        }

        public void Show(InteractionController interaction)
        {
            RobotRepair robot = interaction.Robot;
            if (previousRobot != robot)
            {
                previousRobot = robot;
                diagnosisHelpLevel = 0;
                collapsedOverride = null;
                showHelp = showDiagnosis = false;
            }
            if (questPanel != null) questPanel.SetActive(true);
            if (controls != null) controls.gameObject.SetActive(false);
            bool compact = collapsedOverride ?? true;
            compactNow = compact;
            SetCompact(compact);
            if (toolsPanel != null) toolsPanel.SetActive(true);
            if (UnityEngine.InputSystem.Keyboard.current?.f1Key.wasPressedThisFrame == true)
            { showHelp = !showHelp; showDiagnosis = false; }
            if (UnityEngine.InputSystem.Keyboard.current?.hKey.wasPressedThisFrame == true) ToggleDiagnosis();
            if (helpPanel != null) helpPanel.SetActive(showHelp);
            if (diagnosisButton != null) diagnosisButton.gameObject.SetActive(!compact || showHelp);
            if (collapseLabel != null) collapseLabel.text = compact ? "+" : "−";
            OrderDefinition order = interaction.ActiveOrder;
            bool independent = order != null && order.IndependentDiagnosis;
            if (diagnosisPanel != null) diagnosisPanel.SetActive(showDiagnosis);
            if (diagnosisText != null && showDiagnosis)
                diagnosisText.text = "<b>УСТРОЙСТВО РУКИ</b>\nПлата → контакт питания → мотор / редуктор → рука\n"
                    + "Исправный привод вращается ровно. Исправная рука выполняет полный жест. Стенд проверяет только снятый привод.\n\n"
                    + "<b>НАБЛЮДЕНИЯ</b>\n" + (robot != null ? robot.Findings : "Нет робота")
                    + "\n\n<b>ПОМОЩЬ</b>\n" + (robot != null ? robot.DiagnosisHelp(diagnosisHelpLevel) : "");
            if (diagnosisHelpButton != null)
            {
                diagnosisHelpButton.interactable = diagnosisHelpLevel < 3;
                if (diagnosisHelpLabel != null) diagnosisHelpLabel.text = diagnosisHelpLevel == 0 ? "Куда смотреть" : diagnosisHelpLevel == 1 ? "Что проверить"
                    : diagnosisHelpLevel == 2 ? "Показать решение" : "Решение открыто";
            }
            bool cleaning = order != null && order.RequiresCleaning;
            bool clean = !cleaning || robot != null && robot.CheckCleaning(out _);
            bool ready = robot != null && robot.TestResult == RobotTestResult.Passed && clean;
            if (toolsGroup != null) toolsGroup.alpha = robot != null && (robot.IsTesting || robot.IsOnDriveStand) ? .4f : 1f;

            if (symptom != null)
            {
                string heading = order != null ? $"{(independent ? "ЗАКАЗ" : "ЗАКАЗ " + order.OrderId)} · {order.ClientName}" : "РЕМОНТ РОБОТА";
                symptom.text = compact ? $"<size=13>{heading}</size>\n<b>{(cleaning ? "Обслуживание" : "Ремонт руки")}</b>"
                    : $"<size=13>{heading}</size>\n«{(order != null ? order.Symptom : "Осмотрите и проверьте робота")}»";
            }

            if (assemblyState != null)
            {
                string goal;
                if (ready) goal = "Отнесите робота в зону выдачи у стойки";
                else if (!independent) goal = NextAction(robot, cleaning);
                else if (robot != null && robot.IsTesting) goal = "Проверяем движение руки…";
                else if (robot != null && robot.IsOnDriveStand) goal = robot.ActiveDriveStand.Status;
                else goal = "Найдите причину отказа и проверьте результат ремонта.\nH — факты и помощь по запросу";
                string finding = FindingText(robot, cleaning);
                string progress = "";
                int completed = 0;
                if (cleaning && robot != null)
                {
                    foreach (DirtPatch patch in robot.DirtPatches) if (patch != null && patch.IsClean) completed++;
                    progress = $"\n<color={Accent}>Очистка ≥90%: {completed} / {robot.DirtPatches.Length} участков</color>";
                    if (!compact)
                        foreach (DirtPatch patch in robot.DirtPatches)
                            if (patch != null) progress += $"\n{patch.DisplayName}: {(patch.IsClean ? "готово" : patch.Cleanliness.ToString("P0"))}";
                }
                if (robot != null && robot.JointFaultFound && robot.SolderJoint != null)
                    progress += $"\n<color={Accent}>Контакт: {(robot.SolderJoint.IsRepaired ? "восстановлен" : robot.SolderJoint.Progress.ToString("P0"))}</color>";
                assemblyState.text = compact
                    ? ready ? "Готов к выдаче" : robot != null && robot.IsTesting ? "Проверка руки…"
                        : robot != null && robot.IsOnDriveStand ? "Привод на стенде"
                        : cleaning && robot != null ? $"Очистка: {completed} / {robot.DirtPatches.Length}" : "В работе"
                    : $"{finding}{progress}\n\n<b>{(ready ? "ГОТОВ К ВЫДАЧЕ" : "СЕЙЧАС")}</b>\n{goal}";
                assemblyState.color = ready ? new Color(.65f, .89f, .8f) : new Color(.89f, .93f, .94f);
            }

            if (hint != null)
            {
                string contextText = WorkingStatus(interaction);
                Transform target = ContextTarget(interaction);
                if (previousFeedback != interaction.Feedback)
                {
                    previousFeedback = interaction.Feedback;
                    feedbackTarget = target;
                }
                bool feedback = interaction.Feedback.Length > 0 && feedbackTarget == target;
                bool working = robot != null && robot.IsTesting || interaction.HeldPart != null
                    || interaction.Screwdriver != null && interaction.Screwdriver.IsTurning
                    || interaction.Brush != null && interaction.Brush.IsActive
                    || interaction.Soldering != null && interaction.Soldering.IsActive;
                if (feedback && !working) contextText = $"<color=#FFD599>{interaction.Feedback}</color>";
                if (context != null)
                {
                    if (interaction.IsViewMoving || showHelp || showDiagnosis) context.Hide();
                    else context.Present(contextText, interaction.View, null,
                        interaction.SolderingSelected && interaction.Soldering.IsActive ? interaction.Soldering.Target.Progress
                            : interaction.BrushSelected && interaction.Brush.IsActive && interaction.Brush.Target != null
                                ? interaction.Brush.Target.Cleanliness : -1f,
                        working || feedback);
                }
                else hint.text = contextText;
            }
            if (controls != null)
            {
                string hand = ToolLabel("1  Рука", !interaction.BrushSelected && !interaction.ScrewdriverSelected && !interaction.SolderingSelected);
                string driver = ToolLabel("2  Отвёртка", interaction.ScrewdriverSelected);
                string brush = ToolLabel("3  Щётка", interaction.BrushSelected);
                int selected = interaction.ScrewdriverSelected ? 1 : interaction.BrushSelected ? 2 : interaction.SolderingSelected ? 3 : 0;
                controls.text = selected == 0 ? hand : selected == 1 ? driver : selected == 2 ? brush : ToolLabel("4  Паяльник", true);
                if (toolSlots != null)
                    for (int i = 0; i < toolSlots.Length; i++)
                        if (toolSlots[i] != null)
                        {
                            var button = toolSlots[i].GetComponent<UnityEngine.UI.Button>();
                            if (button != null) button.interactable = (robot == null || !robot.IsTesting && !robot.IsOnDriveStand) && (i != 3 || interaction.SolderingAvailable);
                            toolSlots[i].gameObject.SetActive(i != 3 || interaction.SolderingAvailable);
                            toolSlots[i].color = i == selected ? new Color(.24f, .49f, .44f, .96f)
                                : new Color(.055f, .105f, .13f, .94f);
                        }
            }
            if (cameraHints != null)
            {
                cameraHints.gameObject.SetActive(true);
                cameraHints.text = "F1 — помощь";
            }
        }

        void ToggleDetails() => collapsedOverride = !compactNow;
        void ToggleDiagnosis() { showDiagnosis = !showDiagnosis; showHelp = false; }
        void RevealDiagnosisHelp() => diagnosisHelpLevel = Mathf.Min(3, diagnosisHelpLevel + 1);
        void CloseDiagnosisPanel() => showDiagnosis = false;
        public bool CloseOverlay()
        {
            if (!showDiagnosis && !showHelp) return false;
            showDiagnosis = false;
            showHelp = false;
            return true;
        }

        static Transform ContextTarget(InteractionController interaction)
        {
            if (interaction.PointedScrew != null) return interaction.PointedScrew.transform;
            if (interaction.PointedJoint != null) return interaction.PointedJoint.transform;
            if (interaction.PointedDirt != null) return interaction.PointedDirt.transform;
            if (interaction.PointedConnector != null) return interaction.PointedConnector.transform;
            if (interaction.PointedPart != null) return interaction.PointedPart.transform;
            return interaction.PointedObject;
        }

        static string ToolLabel(string label, bool selected) => selected
            ? $"<color={Accent}><b>[{label}]</b></color>" : label;

        static string FindingText(RobotRepair robot, bool cleaning)
        {
            if (cleaning) return "Обслуживание · замена привода не требуется";
            if (robot == null) return "Причина пока неизвестна.";
            if (robot.JointFaultFound && robot.SolderJoint != null)
                return robot.SolderJoint.IsRepaired ? "Контакт восстановлен" : "На дорожке контакта виден разрыв";
            RepairPart drive = robot.InstalledDrive;
            if (drive != null && drive.StandResult != DriveTestResult.NotTested)
                return $"{drive.DriveIdentity}: {drive.StandObservation}";
            if (drive != null && robot.InspectedDrive == drive) return "Осмотрено · видимых повреждений привода нет";
            return robot.LastArmObservation != ArmObservation.NotTested ? robot.ArmObservationText : "Причина пока неизвестна.";
        }

        static string NextAction(RobotRepair robot, bool cleaning)
        {
            if (robot == null) return "Примите заказ на стойке";
            if (robot.IsTesting) return "Проверяем движение руки…";
            if (cleaning && !robot.CheckCleaning(out _))
            {
                foreach (DirtPatch patch in robot.DirtPatches)
                {
                    if (patch == null || patch.IsClean) continue;
                    if (!robot.Cover.IsInstalled && !patch.RequiresOpenCover
                        && patch.GetComponentInParent<RepairPart>() == robot.Cover) continue;
                    if (patch.RequiresOpenCover && robot.Cover.IsInstalled)
                        return robot.Cover.IsReleased ? "1 — рука: перенесите крышку в левый лоток"
                            : "2 — отвёртка: освободите винты крышки для очистки внутри";
                    return $"3 — щётка: очистите «{patch.DisplayName}» до 90%";
                }
            }
            return robot.AssemblyHint;
        }

        static string WorkingStatus(InteractionController interaction)
        {
            if (interaction.HeldPart != null) return interaction.Robot.PartLabel(interaction.HeldPart);
            var driver = interaction.Screwdriver;
            if (driver != null && driver.IsTurning)
                return $"Затяжка: {driver.Target.Tightness:P0}";
            var brush = interaction.Brush;
            if (brush != null && brush.IsActive && brush.Target != null)
                return $"{brush.Target.DisplayName}: {brush.Target.Cleanliness:P0}";
            var soldering = interaction.Soldering;
            if (soldering != null && soldering.IsActive)
                return soldering.HasContact ? $"Контакт: {soldering.Target.Progress:P0}" : "Нет контакта";
            return "";
        }
        public void ShowShop(string message)
        {
            Hide();
            if (context != null) context.Present(message);
            else if (hint != null) hint.text = message;
        }

        public void Hide()
        {
            SetCompact(false);
            if (questPanel != null) questPanel.SetActive(false);
            if (controls != null) controls.gameObject.SetActive(false);
            if (toolsPanel != null) toolsPanel.SetActive(false);
            if (cameraHints != null) cameraHints.gameObject.SetActive(false);
            if (helpPanel != null) helpPanel.SetActive(false);
            if (diagnosisPanel != null) diagnosisPanel.SetActive(false);
            if (diagnosisButton != null) diagnosisButton.gameObject.SetActive(false);
            showDiagnosis = false;
            showHelp = false;
            if (assemblyState != null) assemblyState.text = "";
            if (symptom != null) symptom.text = "";
        }

        void OnDisable()
        {
            Hide();
            if (context != null) context.Hide();
        }

        void OnDestroy()
        {
            if (collapseButton != null) collapseButton.onClick.RemoveListener(ToggleDetails);
            if (diagnosisButton != null) diagnosisButton.onClick.RemoveListener(ToggleDiagnosis);
            if (diagnosisHelpButton != null) diagnosisHelpButton.onClick.RemoveListener(RevealDiagnosisHelp);
            if (closeDiagnosisButton != null) closeDiagnosisButton.onClick.RemoveListener(CloseDiagnosisPanel);
        }
    }
}
