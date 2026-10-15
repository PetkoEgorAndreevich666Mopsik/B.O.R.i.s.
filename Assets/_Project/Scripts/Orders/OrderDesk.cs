using UnityEngine;

namespace BORis
{
    public sealed class OrderDesk : MonoBehaviour
    {
        [SerializeField] WorkshopSession session;
        [SerializeField] GameObject panel;
        [SerializeField] UnityEngine.UI.Text card;
        [SerializeField] UnityEngine.UI.Text status;
        [SerializeField] UnityEngine.UI.Button acceptButton;
        [SerializeField] UnityEngine.UI.Button closeButton;
        InteractionController visitor;

        public bool IsOpen => visitor != null && panel != null && panel.activeSelf;

        void Awake()
        {
            if (panel != null) panel.SetActive(false);
            if (acceptButton != null) acceptButton.onClick.AddListener(Accept);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        public bool Open(InteractionController interaction)
        {
            if (!isActiveAndEnabled || interaction == null || session == null || panel == null) return false;
            visitor = interaction;
            panel.SetActive(true);
            Refresh(session.ActiveOrder != null ? "Заказ принят. Робот ждёт на верстаке" : "Осмотрите карточку и примите заказ");
            return true;
        }

        void Accept()
        {
            if (!IsOpen) return;
            bool accepted = session.TryAcceptOrder(out string reason);
            Refresh(accepted ? "Заказ принят. Закройте карточку и подойдите к верстаку" : reason);
        }

        void Refresh(string message)
        {
            OrderDefinition order = session.ActiveOrder != null ? session.ActiveOrder : session.OfferedOrder;
            if (card != null) card.text = order == null ? "Нет доступных заказов"
                : $"ЗАКАЗ {order.OrderId}\n\nКлиент: {order.ClientName}\n\n«{order.Symptom}»\n\nНаграда за заказ: {order.Reward} ед.";
            if (status != null) status.text = message;
            if (acceptButton != null) acceptButton.interactable = session.ActiveOrder == null
                && order != null && order.IsValid && !order.RequiresSolderingStation;
        }

        void Close()
        {
            if (visitor != null) visitor.CloseOrderDesk();
        }

        public void Hide()
        {
            visitor = null;
            if (panel != null) panel.SetActive(false);
        }

        void OnDisable() => Close();

        void OnDestroy()
        {
            if (acceptButton != null) acceptButton.onClick.RemoveListener(Accept);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        }
    }
}
