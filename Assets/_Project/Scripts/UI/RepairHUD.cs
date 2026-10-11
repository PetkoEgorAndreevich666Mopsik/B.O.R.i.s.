using UnityEngine;
using UnityEngine.UI;

namespace BORis
{
    public sealed class RepairHUD : MonoBehaviour
    {
        [SerializeField] Text hint;

        public void Show(ScrewdriverTool tool, bool selected, ScrewFastener pointed)
        {
            if (hint == null) return;
            ScrewFastener screw = tool != null && tool.IsTurning ? tool.Target : pointed;
            string instruction = tool != null && tool.IsTurning
                ? "Веди мышь вокруг винта: против часовой — выкрутить, по часовой — затянуть\nЛКМ отпустить — сохранить положение"
                : selected ? "ОТВЁРТКА  |  ЛКМ на винте, затем круговое движение мышью\nОтведи курсор от центра винта и сделай круг  |  1 — убрать инструмент"
                : "2 — взять отвёртку или нажми на неё на столе";
            string state = screw == null ? "" : screw.Tightness <= 0f ? "\nВИНТ ОСВОБОЖДЁН"
                : screw.Tightness >= 1f ? "\nВИНТ ЗАТЯНУТ" : $"\nЗатяжка: {screw.Tightness:P0}";
            hint.text = instruction + state + "\nESC — вернуться";
        }
    }
}
