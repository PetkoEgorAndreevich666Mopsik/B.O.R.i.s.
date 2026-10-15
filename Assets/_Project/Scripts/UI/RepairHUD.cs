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

        public void Show(InteractionController interaction)
        {
            if (hint == null) return;
            if (questPanel != null) questPanel.SetActive(true);
            if (symptom != null) symptom.text = interaction.ActiveOrder != null
                ? interaction.ActiveOrder.Symptom : "Правая рука дёргается\nи не поднимается";
            ScrewdriverTool tool = interaction.Screwdriver;
            BrushTool brush = interaction.Brush;
            DirtPatch dirt = brush != null && brush.IsActive ? brush.Target : interaction.PointedDirt;
            ScrewFastener screw = tool != null && tool.IsTurning ? tool.Target : interaction.PointedScrew;
            if (assemblyState != null)
            {
                assemblyState.text = interaction.Robot != null ? interaction.Robot.AssemblyHint : "";
                if (interaction.ActiveOrder != null && interaction.Robot != null && interaction.Robot.TestResult == RobotTestResult.Passed)
                    assemblyState.text = "Проверка пройдена. Вернитесь к стойке и выдайте робота";
                assemblyState.color = interaction.Robot != null && interaction.Robot.TestResult == RobotTestResult.Passed
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
            hint.text = instruction + state + "\nESC — вернуться";
        }

        public void Hide()
        {
            if (questPanel != null) questPanel.SetActive(false);
            if (assemblyState != null) assemblyState.text = "";
            if (symptom != null) symptom.text = "";
        }

        void OnDisable() => Hide();
    }
}
