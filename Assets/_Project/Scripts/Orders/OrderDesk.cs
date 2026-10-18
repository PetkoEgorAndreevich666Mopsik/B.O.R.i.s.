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
        [SerializeField] UnityEngine.UI.Button deliverButton;
        [SerializeField] UnityEngine.UI.Button closeButton;
        InteractionController visitor;
        OrderDefinition displayedOrder;
        string displayedInstanceId;
        int displayedReward;
        bool showingReceipt;

        public bool IsOpen => visitor != null && panel != null && panel.activeSelf;

        void Awake()
        {
            if (panel != null) panel.SetActive(false);
            if (acceptButton != null) acceptButton.onClick.AddListener(Accept);
            if (deliverButton != null) deliverButton.onClick.AddListener(Deliver);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        public bool Open(InteractionController interaction)
        {
            if (!isActiveAndEnabled || interaction == null || session == null || panel == null) return false;
            visitor = interaction;
            showingReceipt = false;
            panel.SetActive(true);
            string message = "Осмотрите карточку и примите заказ";
            if (session.ActiveOrder != null)
                message = session.CanDeliverOrder(out string reason) ? "Робот готов. Выдайте заказ, чтобы получить оплату" : reason;
            Refresh(message);
            return true;
        }

        void Accept()
        {
            if (!IsOpen) return;
            if (showingReceipt)
            {
                showingReceipt = false;
                Refresh("Осмотрите следующий заказ. Он ещё не принят");
                return;
            }
            bool accepted = session.TryAcceptOrder(out string reason);
            Refresh(accepted ? "Заказ принят. Закройте карточку и подойдите к верстаку" : reason);
        }

        void Deliver()
        {
            if (!IsOpen || showingReceipt) return;
            if (!session.TryDeliverOrder(displayedInstanceId, out string reason)) { Refresh(reason); return; }
            showingReceipt = true;
            Refresh("Спасибо за ремонт! Можно посмотреть следующий заказ");
        }

        void Refresh(string message)
        {
            if (!showingReceipt)
            {
                displayedOrder = session.ActiveOrder != null ? session.ActiveOrder : session.OfferedOrder;
                displayedInstanceId = session.ActiveOrderId;
                displayedReward = session.ActiveOrder != null ? session.ActiveOrderReward : displayedOrder != null ? displayedOrder.Reward : 0;
            }
            OrderDefinition order = displayedOrder;
            if (card != null) card.text = order == null ? "Нет доступных заказов"
                : showingReceipt ? $"ЗАКАЗ {order.OrderId} ВЫДАН\n\nКлиент: {order.ClientName}\n\nПолучено: +{displayedReward} ед.\nБаланс мастерской: {session.Balance} ед.\n\nВыполнено заказов: {session.CompletedOrders}"
                : $"ЗАКАЗ {order.OrderId}\n\nКлиент: {order.ClientName}\n\n«{order.Symptom}»\n\n{order.Requirements}\n\nНаграда за заказ: {displayedReward} ед.";
            if (status != null) status.text = message + (session.HasSolderingStation ? ""
                : session.Balance >= WorkshopSession.SolderingStationPrice
                    ? "\nХватает на паяльную станцию. Оборудование — справа от верстака"
                    : $"\nПаяльная станция: осталось накопить {WorkshopSession.SolderingStationPrice - session.Balance} ед.");
            if (acceptButton != null)
            {
                acceptButton.gameObject.SetActive(showingReceipt || session.ActiveOrder == null);
                acceptButton.interactable = showingReceipt || session.ActiveOrder == null
                    && order != null && order.IsValid && (!order.RequiresSolderingStation || session.CanOfferSolderingOrders);
                var caption = acceptButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (caption != null) caption.text = showingReceipt ? "Следующий заказ" : "Принять заказ";
            }
            if (deliverButton != null) deliverButton.gameObject.SetActive(!showingReceipt && session.ActiveOrder != null);
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
            if (deliverButton != null) deliverButton.onClick.RemoveListener(Deliver);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        }
    }
}
