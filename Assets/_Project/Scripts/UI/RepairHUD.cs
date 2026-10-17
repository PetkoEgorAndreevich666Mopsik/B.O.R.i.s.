using UnityEngine;
using UnityEngine.UI;

namespace BORis
{
    public sealed class RepairHUD : MonoBehaviour
    {
        [SerializeField] Text hint;
        [SerializeField] Text assemblyState;
        [SerializeField] Text symptom;
        [SerializeField] GameObject questPanel;
        RectTransform questRect;
        Vector2 questSize, symptomSize;

        void Awake()
        {
            questRect = questPanel != null ? questPanel.GetComponent<RectTransform>() : null;
            if (questRect != null) questSize = questRect.sizeDelta;
            if (symptom != null) symptomSize = symptom.rectTransform.sizeDelta;
        }

        void SetCompact(bool compact)
        {
            if (questRect != null) questRect.sizeDelta = compact ? new Vector2(questSize.x, 100f) : questSize;
            if (symptom != null) symptom.rectTransform.sizeDelta = compact ? new Vector2(symptomSize.x, 76f) : symptomSize;
            if (assemblyState != null) assemblyState.gameObject.SetActive(!compact);
        }

        public void Show(InteractionController interaction)
        {
            if (hint == null) return;
            if (questPanel != null) questPanel.SetActive(true);
            SetCompact(interaction.IsInspecting);
            if (symptom != null) symptom.text = interaction.ActiveOrder != null
                ? interaction.ActiveOrder.Symptom : "Правая рука дёргается\nи не поднимается";
            ScrewdriverTool tool = interaction.Screwdriver;
            BrushTool brush = interaction.Brush;
            DirtPatch dirt = brush != null && brush.IsActive ? brush.Target : interaction.PointedDirt;
            ScrewFastener screw = tool != null && tool.IsTurning ? tool.Target : interaction.PointedScrew;
            if (interaction.IsInspecting && symptom != null)
                symptom.text = $"ОСМОТР: {interaction.InspectionName}\n" + (dirt != null
                    ? $"{dirt.DisplayName}: {dirt.Cleanliness:P0}" : "F — общий вид верстака");
            if (assemblyState != null)
            {
                RobotRepair robot = interaction.Robot;
                bool cleaningRequired = interaction.ActiveOrder != null && interaction.ActiveOrder.RequiresCleaning;
                bool cleaningDone = !cleaningRequired || robot != null && robot.CheckCleaning(out _);
                bool passed = robot != null && robot.TestResult == RobotTestResult.Passed;
                assemblyState.text = robot != null ? robot.AssemblyHint : "";
                if (robot != null && cleaningRequired)
                {
                    if (!cleaningDone && !robot.IsTesting && robot.IsFunctional)
                        assemblyState.text = robot.Cover.IsInstalled
                            ? "Очистите корпус снаружи; освободите винты и снимите крышку для очистки внутри"
                            : "Щёткой очистите указанные участки. Исправный привод менять не нужно";
                    string progress = "ОБЯЗАТЕЛЬНАЯ ОЧИСТКА (90% каждого участка)";
                    foreach (DirtPatch patch in robot.DirtPatches)
                        if (patch != null) progress += $"\n{patch.DisplayName}: {patch.Cleanliness:P0}" + (patch.IsClean ? " — ЧИСТО" : "");
                    assemblyState.text = progress + "\n\n" + assemblyState.text;
                }
                if (interaction.ActiveOrder != null && passed && (!cleaningRequired || cleaningDone))
                    assemblyState.text = "Проверка пройдена. Вернитесь к стойке и выдайте робота";
                assemblyState.color = passed && (!cleaningRequired || cleaningDone)
                    ? new Color(.55f, 1f, .65f) : Color.white;
            }
            if (interaction.Robot != null && interaction.Robot.IsTesting)
            {
                hint.text = "ТЕСТ РУКИ  |  Ремонт временно заблокирован\nESC — прервать и вернуться  |  Новый ремонт — начать заново";
                return;
            }
            string instruction = interaction.HeldPart != null
                ? $"РУКА: {interaction.HeldPart.DisplayName}\nЛКМ отпустить — " + (interaction.DropAnchor == null ? "вернуть на исходное место" : "поставить в выделенное место")
                : tool != null && tool.IsTurning
                ? "ЛКМ удерживать + колесо: вверх — затянуть, вниз — выкрутить\nИли веди мышь вокруг винта  |  ЛКМ отпустить — сохранить положение"
                : interaction.BrushSelected ? "ЩЁТКА  |  Удерживай ЛКМ и води по грязи; неподвижная щётка не чистит\n1 — рука  |  2 — отвёртка"
                : interaction.ScrewdriverSelected ? "ОТВЁРТКА  |  Удерживай ЛКМ на винте: колесо или круговое движение\n1 — рука  |  3 — щётка"
                : "РУКА  |  ЛКМ удерживать — перенос детали  |  ЛКМ на разъёме — подключить / отключить\n2 — отвёртка  |  3 — щётка или нажми на инструмент на столе";
            string state = interaction.Feedback.Length > 0 ? "\n" + interaction.Feedback
                : interaction.PointedTestButton ? "\nЛКМ — проверить движение руки"
                : interaction.BrushSelected && dirt != null ? $"\n{dirt.DisplayName}: очищено {dirt.Cleanliness:P0}" + (dirt.IsClean ? " — ЧИСТО" : "")
                : interaction.PointedConnector != null ? interaction.PointedConnector.IsConnected ? "\nРАЗЪЁМ ПОДКЛЮЧЁН" : "\nРАЗЪЁМ ОТКЛЮЧЁН"
                : screw == null ? interaction.PointedPart == null ? "" : "\n" + interaction.PointedPart.DisplayName
                : screw.IsLoose ? "\nВИНТ ОСВОБОЖДЁН"
                : screw.IsTight ? "\nВИНТ ЗАТЯНУТ" : "\nВращайте до щелчка";
            string cameraHint = interaction.IsInspecting
                ? "F — общий вид  |  ПКМ + мышь — обзор  |  Колесо — масштаб вне работы инструментом"
                : "F на роботе или снятой детали — осмотр";
            hint.text = instruction + state + "\n" + cameraHint + "\nESC — вернуться";
        }

        public void Hide()
        {
            SetCompact(false);
            if (questPanel != null) questPanel.SetActive(false);
            if (assemblyState != null) assemblyState.text = "";
            if (symptom != null) symptom.text = "";
        }

        void OnDisable() => Hide();
    }
}
