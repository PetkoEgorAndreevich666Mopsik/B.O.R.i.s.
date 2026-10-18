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
        RectTransform questRect;
        CanvasGroup toolsGroup;
        Vector2 questSize, symptomSize, assemblySize, assemblyPosition;
        bool compactNow, showHelp, driverLearned, brushLearned;
        bool? collapsedOverride;
        string previousFeedback;
        Transform feedbackTarget;
        RobotRepair previousRobot;
        bool faultWasFound;
        float findingUntil;
        const string Accent = "#A6E3CD";

        void Awake()
        {
            if (collapseButton != null) collapseButton.onClick.AddListener(ToggleDetails);
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
            if (questRect != null) questRect.sizeDelta = compact ? new Vector2(questSize.x, 196f) : questSize;
            if (symptom != null) symptom.rectTransform.sizeDelta = compact ? new Vector2(symptomSize.x, 58f) : symptomSize;
            if (assemblyState == null) return;
            assemblyState.rectTransform.sizeDelta = compact ? new Vector2(assemblySize.x, 108f) : assemblySize;
            assemblyState.rectTransform.anchoredPosition = compact ? new Vector2(assemblyPosition.x, -76f) : assemblyPosition;
        }

        public void Show(InteractionController interaction)
        {
            if (questPanel != null) questPanel.SetActive(true);
            if (controls != null) controls.gameObject.SetActive(true);
            bool compact = collapsedOverride ?? interaction.IsInspecting;
            compactNow = compact;
            SetCompact(compact);
            if (toolsPanel != null) toolsPanel.SetActive(true);
            if (UnityEngine.InputSystem.Keyboard.current?.f1Key.wasPressedThisFrame == true) showHelp = !showHelp;
            if (helpPanel != null) helpPanel.SetActive(showHelp);
            if (collapseLabel != null) collapseLabel.text = compact ? "+" : "−";
            RobotRepair robot = interaction.Robot;
            if (previousRobot != robot)
            {
                previousRobot = robot;
                faultWasFound = robot != null && (robot.DriveFaultFound || robot.JointFaultFound);
                findingUntil = 0f;
            }
            else if (robot != null && (robot.DriveFaultFound || robot.JointFaultFound) && !faultWasFound)
            {
                faultWasFound = true;
                findingUntil = Time.unscaledTime + 1.4f;
            }
            OrderDefinition order = interaction.ActiveOrder;
            bool cleaning = order != null && order.RequiresCleaning;
            bool clean = !cleaning || robot != null && robot.CheckCleaning(out _);
            bool ready = robot != null && robot.TestResult == RobotTestResult.Passed && clean;
            if (toolsGroup != null) toolsGroup.alpha = robot != null && robot.IsTesting ? .4f : 1f;

            if (symptom != null)
            {
                string heading = order != null ? $"ЗАКАЗ {order.OrderId} · {order.ClientName}" : "РЕМОНТ РОБОТА";
                symptom.text = interaction.IsInspecting
                    ? $"<size=13>ОСМОТР</size>\n<b>{interaction.InspectionName}</b>"
                    : compact ? $"<size=13>{heading}</size>\n<b>{(cleaning ? "Обслуживание" : "Ремонт руки")}</b>"
                    : $"<size=13>{heading}</size>\n«{(order != null ? order.Symptom : "Осмотрите и проверьте робота")}»";
            }

            if (assemblyState != null)
            {
                string goal = ready ? "Вернитесь к стойке и выдайте робота" : NextAction(robot, cleaning);
                string finding = FindingText(robot, cleaning);
                string progress = "";
                if (cleaning && robot != null)
                {
                    int completed = 0;
                    foreach (DirtPatch patch in robot.DirtPatches) if (patch != null && patch.IsClean) completed++;
                    progress = $"\n<color={Accent}>Очистка ≥90%: {completed} / {robot.DirtPatches.Length} участков</color>";
                    if (!compact)
                        foreach (DirtPatch patch in robot.DirtPatches)
                            if (patch != null) progress += $"\n{patch.DisplayName}: {(patch.IsClean ? "готово" : patch.Cleanliness.ToString("P0"))}";
                }
                if (robot != null && robot.JointFaultFound && robot.SolderJoint != null)
                    progress += $"\n<color={Accent}>Контакт: {(robot.SolderJoint.IsRepaired ? "восстановлен" : robot.SolderJoint.Progress.ToString("P0"))}</color>";
                assemblyState.text = compact
                    ? (robot != null && (robot.DriveFaultFound || robot.JointFaultFound) ? FindingText(robot, cleaning) + "\n" : "")
                        + $"<b>СЕЙЧАС</b>\n{goal}{progress}"
                    : $"{finding}{progress}\n\n<b>{(ready ? "ГОТОВ К ВЫДАЧЕ" : "СЕЙЧАС")}</b>\n{goal}";
                assemblyState.color = ready ? new Color(.65f, .89f, .8f) : new Color(.89f, .93f, .94f);
            }

            if (hint != null)
            {
                string contextText = ContextHint(interaction);
                if (interaction.Screwdriver != null && interaction.Screwdriver.IsTurning) driverLearned = true;
                if (interaction.Brush != null && interaction.Brush.IsActive) brushLearned = true;
                Transform target = ContextTarget(interaction);
                if (previousFeedback != interaction.Feedback)
                {
                    previousFeedback = interaction.Feedback;
                    feedbackTarget = target;
                }
                bool feedback = interaction.Feedback.Length > 0 && feedbackTarget == target;
                if (feedback) contextText = $"<color=#FFD599>{interaction.Feedback}</color>";
                else if (Time.unscaledTime < findingUntil)
                    contextText = FindingText(robot, cleaning);
                bool working = robot != null && robot.IsTesting || interaction.HeldPart != null
                    || interaction.Screwdriver != null && interaction.Screwdriver.IsTurning
                    || interaction.Brush != null && interaction.Brush.IsActive
                    || interaction.Soldering != null && interaction.Soldering.IsActive;
                if (context != null)
                {
                    if (interaction.IsViewMoving || showHelp) context.Hide();
                    else context.Present(contextText, interaction.View, working ? null : target,
                        interaction.SolderingSelected && interaction.Soldering.IsActive ? interaction.Soldering.Target.Progress
                            : interaction.ObservationProgress > 0f ? interaction.ObservationProgress : -1f,
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
                            if (button != null) button.interactable = (robot == null || !robot.IsTesting) && (i != 3 || interaction.SolderingAvailable);
                            toolSlots[i].gameObject.SetActive(i != 3 || interaction.SolderingAvailable);
                            toolSlots[i].color = i == selected ? new Color(.24f, .49f, .44f, .96f)
                                : new Color(.055f, .105f, .13f, .94f);
                        }
            }
            if (cameraHints != null)
            {
                cameraHints.gameObject.SetActive(true);
                cameraHints.text = interaction.IsInspecting ? "F  Общий вид · ПКМ  Ракурс\nКолесо  Масштаб · F1  Справка" : "F  Осмотр · F1  Справка";
            }
        }

        void ToggleDetails() => collapsedOverride = !compactNow;

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
            if (robot != null && robot.JointFaultFound && robot.SolderJoint != null)
                return robot.SolderJoint.IsRepaired ? $"<color={Accent}><b>КОНТАКТ ВОССТАНОВЛЕН</b></color>"
                    : $"<color={Accent}><b>НАЙДЕНО</b> · разрыв дорожки контакта</color>";
            if (robot != null && robot.DriveFaultFound)
                return $"<color={Accent}><b>НАЙДЕНО</b> · повреждён привод руки</color>";
            if (cleaning) return "Обслуживание · замена привода не требуется";
            if (robot != null && robot.HasInspectedDrive && robot.InspectedDrive == robot.InstalledDrive)
                return "Осмотрено · видимых повреждений привода нет";
            return "Причина пока неизвестна.";
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

        string ContextHint(InteractionController interaction)
        {
            RobotRepair robot = interaction.Robot;
            if (robot != null && robot.IsTesting) return "<b>ПРОВЕРКА РУКИ</b> · дождитесь результата";
            if (interaction.HeldPart != null)
                return $"<b>{robot.PartLabel(interaction.HeldPart)}</b>\nОтпустите ЛКМ — " + (interaction.DropAnchor == null
                    ? "вернуть назад" : "установить");
            var driver = interaction.Screwdriver;
            var brush = interaction.Brush;
            var soldering = interaction.Soldering;
            SolderJoint joint = soldering != null && soldering.IsActive ? soldering.Target : interaction.PointedJoint;
            if (joint != null)
            {
                if (interaction.ObservationProgress > 0f)
                    return "<b>ОСМОТР КОНТАКТА</b>\nЗадержите указатель на дорожке";
                string state = joint.IsRepaired ? "восстановлен" : joint.Progress.ToString("P0");
                string action = joint.IsRepaired ? "Подключите разъём, соберите и проверьте робота"
                    : !interaction.SolderingSelected ? "4 — паяльник · ЛКМ + движение по дорожке"
                    : !robot.CanSolder(joint, interaction.Session, out string reason) ? reason
                    : soldering.IsActive && !soldering.HasContact ? "Нет контакта · вернитесь жалом на дорожку"
                    : "ЛКМ + движение по дорожке · пройденный участок станет серебристым";
                return $"<b>КОНТАКТ</b> · {state}\n{action}";
            }
            ScrewFastener screw = driver != null && driver.IsTurning ? driver.Target : interaction.PointedScrew;
            DirtPatch patch = brush != null && brush.IsActive ? brush.Target : interaction.PointedDirt;
            if (screw != null)
            {
                var owner = screw.GetComponentInParent<RepairPart>();
                string name = owner != null && owner.Kind == RepairPartKind.Cover ? "ВИНТ КРЫШКИ" : "ВИНТ ПРИВОДА";
                string state = screw.IsLoose ? "освобождён" : screw.IsTight ? "затянут" : "в резьбе";
                string action = interaction.ScrewdriverSelected
                    ? driver != null && driver.IsTurning ? "↓ Выкрутить · ↑ Затянуть"
                    : driverLearned && !showHelp ? "ЛКМ — удерживать и вращать" : "ЛКМ + круговой жест или колесо"
                    : "2 — отвёртка · ЛКМ — удерживать";
                return $"<b>{name}</b> · {state}\n{action}";
            }
            if (patch != null && (interaction.BrushSelected || interaction.PointedPart == null))
                return $"<b>{patch.DisplayName}</b> · {(patch.IsClean ? "чисто" : patch.Cleanliness.ToString("P0"))}\n" +
                    (interaction.BrushSelected ? brush != null && brush.IsActive ? "Ведите щёткой по загрязнению"
                    : brushLearned && !showHelp ? "ЛКМ — удерживать и чистить" : "ЛКМ + движение — очистить"
                    : "3 — щётка: провести по грязи");
            if (interaction.PointedTestButton)
            {
                string reason = "Примите заказ";
                bool allowed = robot != null && robot.CanRunTest(out reason);
                return "<b>ТЕСТ РУКИ</b>\n" + (allowed ? "ЛКМ — проверить работу робота" : reason);
            }
            if (interaction.PointedTool != null)
                return interaction.PointedTool is ScrewdriverTool ? "<b>ОТВЁРТКА</b>\nЛКМ или 2 — взять инструмент"
                    : interaction.PointedTool is SolderingTool ? "<b>ПАЯЛЬНИК</b>\nЛКМ или 4 — взять инструмент"
                    : "<b>ЩЁТКА</b>\nЛКМ или 3 — взять инструмент";
            if (interaction.PointedConnector != null)
                return "<b>РАЗЪЁМ</b> · " + (interaction.PointedConnector.IsConnected ? "подключён" : "отключён") + "\n" +
                    (!interaction.BrushSelected && !interaction.ScrewdriverSelected && !interaction.SolderingSelected ? "ЛКМ — подключить / отключить" : "1 — рука: работать с разъёмом");
            RepairPart part = interaction.PointedPart;
            if (part != null)
            {
                if (interaction.ObservationProgress > 0f)
                    return $"<b>ОСМОТР ПРИВОДА</b> · {interaction.ObservationProgress:P0}\nЗадержите указатель на детали";
                if (interaction.BrushSelected || interaction.ScrewdriverSelected || interaction.SolderingSelected)
                    return $"<b>{robot.PartLabel(part)}</b>\n1 — рука · "
                        + (interaction.IsInspecting ? "F — общий вид" : "F — приблизить");
                if (robot != null && !robot.CanMove(part, out string reason))
                    return $"<b>{robot.PartLabel(part)}</b>\n{reason}";
                return $"<b>{robot.PartLabel(part)}</b>\nУдерживайте ЛКМ — перенести деталь";
            }
            return interaction.PointedObject != null
                && interaction.PointedObject.GetComponentInParent<RobotRepair>() == robot ? "F — осмотреть робота" : "";
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
        }
    }
}
