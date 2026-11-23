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
            string message = "";
            if (session.ActiveOrder != null)
                message = session.CanDeliverOrder(out string reason) ? "Готов к выдаче" : reason;
            Refresh(message);
            return true;
        }

        void Accept()
        {
            if (!IsOpen) return;
            if (!session.IsProgressReady)
            {
                bool restored = session.TryReloadProgress(out string error);
                Refresh(restored ? "Загружено" : error);
                return;
            }
            if (showingReceipt)
            {
                showingReceipt = false;
                Refresh("");
                return;
            }
            bool accepted = session.TryAcceptOrder(out string reason);
            Refresh(accepted ? "Заказ принят" : reason);
        }

        void Deliver()
        {
            if (!IsOpen || showingReceipt) return;
            if (!session.TryDeliverOrder(displayedInstanceId, out string reason)) { Refresh(reason); return; }
            showingReceipt = true;
            Refresh("Оплачено");
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
            if (card != null) card.text = order == null ? "Нет заказов"
                : showingReceipt ? $"{order.ClientName}\n\n+{displayedReward} ед.\nБаланс: {session.Balance} ед."
                : $"{order.ClientName} · {displayedReward} ед.\n\n«{order.Symptom}»";
            if (status != null) status.text = session.IsProgressReady ? message : session.SaveStatus;
            if (acceptButton != null)
            {
                acceptButton.gameObject.SetActive(!session.IsProgressReady || showingReceipt || session.ActiveOrder == null);
                acceptButton.interactable = !session.IsProgressReady || showingReceipt || session.ActiveOrder == null
                    && order != null && order.IsValid && (!order.RequiresSolderingStation || session.CanOfferSolderingOrders);
                var caption = acceptButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (caption != null) caption.text = !session.IsProgressReady ? "Повторить" : showingReceipt ? "Далее" : "Принять";
            }
            if (deliverButton != null) deliverButton.gameObject.SetActive(session.IsProgressReady && !showingReceipt && session.ActiveOrder != null);
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
