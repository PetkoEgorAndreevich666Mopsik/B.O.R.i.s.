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
        RectTransform questRect;
        Vector2 questSize, symptomSize, assemblySize, assemblyPosition;
        const string Accent = "#A6E3CD";

        void Awake()
        {
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
            SetCompact(interaction.IsInspecting);
            RobotRepair robot = interaction.Robot;
            OrderDefinition order = interaction.ActiveOrder;
            bool cleaning = order != null && order.RequiresCleaning;
            bool clean = !cleaning || robot != null && robot.CheckCleaning(out _);
            bool ready = robot != null && robot.TestResult == RobotTestResult.Passed && clean;

            if (symptom != null)
            {
                string heading = order != null ? $"ЗАКАЗ {order.OrderId} · {order.ClientName}" : "РЕМОНТ РОБОТА";
                symptom.text = interaction.IsInspecting
                    ? $"<size=13>ОСМОТР</size>\n<b>{interaction.InspectionName}</b>"
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
                    if (!interaction.IsInspecting)
                        foreach (DirtPatch patch in robot.DirtPatches)
                            if (patch != null) progress += $"\n{patch.DisplayName}: {(patch.IsClean ? "готово" : patch.Cleanliness.ToString("P0"))}";
                }
                assemblyState.text = interaction.IsInspecting
                    ? (robot != null && robot.DriveFaultFound ? $"<color={Accent}><b>НАЙДЕНО</b> · привод повреждён</color>\n" : "")
                        + $"<b>СЕЙЧАС</b>\n{goal}{progress}"
                    : $"{finding}{progress}\n\n<b>{(ready ? "ГОТОВ К ВЫДАЧЕ" : "СЕЙЧАС")}</b>\n{goal}";
                assemblyState.color = ready ? new Color(.65f, .89f, .8f) : new Color(.89f, .93f, .94f);
            }

            if (hint != null)
            {
                string context = ContextHint(interaction);
                hint.text = interaction.Feedback.Length > 0
                    ? $"<color=#FFD599>{interaction.Feedback}</color>\n{context}" : context;
            }
            if (controls != null)
            {
                string hand = ToolLabel("1  Рука", !interaction.BrushSelected && !interaction.ScrewdriverSelected);
                string driver = ToolLabel("2  Отвёртка", interaction.ScrewdriverSelected);
                string brush = ToolLabel("3  Щётка", interaction.BrushSelected);
                controls.text = $"{hand}     {driver}     {brush}     ·     " + (interaction.IsInspecting
                    ? "F  Общий вид   ·   ПКМ  Обзор   ·   Колесо  Масштаб" : "F  Осмотр") + "     ·     ESC  Вернуться";
            }
        }

        static string ToolLabel(string label, bool selected) => selected
            ? $"<color={Accent}><b>[{label}]</b></color>" : label;

        static string FindingText(RobotRepair robot, bool cleaning)
        {
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

        static string ContextHint(InteractionController interaction)
        {
            RobotRepair robot = interaction.Robot;
            if (robot != null && robot.IsTesting) return "<b>ПРОВЕРКА РУКИ</b> · дождитесь результата";
            if (interaction.HeldPart != null)
                return $"<b>{robot.PartLabel(interaction.HeldPart)}</b>\nОтпустите ЛКМ — " + (interaction.DropAnchor == null
                    ? "вернуть на исходное место" : "установить в выделенное место");
            var driver = interaction.Screwdriver;
            var brush = interaction.Brush;
            ScrewFastener screw = driver != null && driver.IsTurning ? driver.Target : interaction.PointedScrew;
            DirtPatch patch = brush != null && brush.IsActive ? brush.Target : interaction.PointedDirt;
            if (screw != null)
            {
                var owner = screw.GetComponentInParent<RepairPart>();
                string name = owner != null && owner.Kind == RepairPartKind.Cover ? "ВИНТ КРЫШКИ" : "ВИНТ ПРИВОДА";
                string state = screw.IsLoose ? "освобождён" : screw.IsTight ? "затянут" : "в резьбе";
                string action = interaction.ScrewdriverSelected
                    ? "ЛКМ + круговой жест или колесо: ↓ выкрутить, ↑ затянуть"
                    : "2 — отвёртка · затем удерживайте ЛКМ на винте";
                return $"<b>{name}</b> · {state}\n{action}";
            }
            if (patch != null && (interaction.BrushSelected || interaction.PointedPart == null))
                return $"<b>{patch.DisplayName}</b> · {(patch.IsClean ? "чисто" : patch.Cleanliness.ToString("P0"))}\n" +
                    (interaction.BrushSelected ? "Удерживайте ЛКМ и ведите щёткой по поверхности" : "3 — щётка: провести по грязи");
            if (interaction.PointedTestButton)
            {
                string reason = "Примите заказ";
                bool allowed = robot != null && robot.CanRunTest(out reason);
                return "<b>ТЕСТ РУКИ</b>\n" + (allowed ? "ЛКМ — проверить работу робота" : reason);
            }
            if (interaction.PointedTool != null)
                return interaction.PointedTool is ScrewdriverTool ? "<b>ОТВЁРТКА</b>\nЛКМ или 2 — взять инструмент"
                    : "<b>ЩЁТКА</b>\nЛКМ или 3 — взять инструмент";
            if (interaction.PointedConnector != null)
                return "<b>РАЗЪЁМ</b> · " + (interaction.PointedConnector.IsConnected ? "подключён" : "отключён") + "\n" +
                    (!interaction.BrushSelected && !interaction.ScrewdriverSelected ? "ЛКМ — подключить / отключить" : "1 — рука: работать с разъёмом");
            RepairPart part = interaction.PointedPart;
            if (part != null)
            {
                if (interaction.ObservationProgress > 0f)
                    return $"<b>ОСМОТР ПРИВОДА</b> · {interaction.ObservationProgress:P0}\nЗадержите указатель на детали";
                if (interaction.BrushSelected || interaction.ScrewdriverSelected)
                    return $"<b>{robot.PartLabel(part)}</b>\n1 — рука для переноса · F — приблизить";
                if (robot != null && !robot.CanMove(part, out string reason))
                    return $"<b>{robot.PartLabel(part)}</b>\n{reason}";
                return $"<b>{robot.PartLabel(part)}</b>\nУдерживайте ЛКМ — перенести деталь";
            }
            return interaction.IsInspecting ? "Наведите указатель на деталь · ПКМ — повернуть ракурс"
                : "Наведите указатель на предмет · F — приблизить робота или снятую деталь";
        }

        public void Hide()
        {
            SetCompact(false);
            if (questPanel != null) questPanel.SetActive(false);
            if (controls != null) controls.gameObject.SetActive(false);
            if (assemblyState != null) assemblyState.text = "";
            if (symptom != null) symptom.text = "";
        }

        void OnDisable() => Hide();
    }
}
