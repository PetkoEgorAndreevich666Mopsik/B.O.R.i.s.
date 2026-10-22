using UnityEngine;

namespace BORis
{
    public sealed class EquipmentShop : MonoBehaviour
    {
        [SerializeField] WorkshopSession session;
        [SerializeField] GameObject panel;
        [SerializeField] UnityEngine.UI.Text description;
        [SerializeField] UnityEngine.UI.Text status;
        [SerializeField] UnityEngine.UI.Button buyButton;
        [SerializeField] UnityEngine.UI.Button closeButton;
        [SerializeField] GameObject installedStation;
        [SerializeField] GameObject stationPreview;
        InteractionController visitor;
        string feedback = "";

        public bool IsOpen => visitor != null && panel != null && panel.activeSelf;

        void Awake()
        {
            if (panel != null) panel.SetActive(false);
            if (buyButton != null) buyButton.onClick.AddListener(Buy);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        void Start() => Refresh();

        void Update()
        {
            if (IsOpen || session != null && installedStation != null && installedStation.activeSelf != session.HasSolderingStation)
                Refresh();
        }

        public bool Open(InteractionController interaction)
        {
            if (!isActiveAndEnabled || interaction == null || session == null || !session.isActiveAndEnabled || panel == null) return false;
            visitor = interaction;
            feedback = "";
            panel.SetActive(true);
            Refresh();
            return true;
        }

        void Buy()
        {
            if (!IsOpen) return;
            bool bought = session.TryBuySolderingStation(out string reason);
            feedback = bought ? "Станция установлена на верстаке справа. Базовые заказы остаются доступны" : reason;
            Refresh();
        }

        void Refresh()
        {
            if (session == null) return;
            bool owned = session.HasSolderingStation;
            if (installedStation != null) installedStation.SetActive(owned);
            if (stationPreview != null) stationPreview.SetActive(!owned);
            int missing = Mathf.Max(0, WorkshopSession.SolderingStationPrice - session.Balance);
            if (description != null)
                description.text = "ПАЯЛЬНАЯ СТАНЦИЯ\n\nРемонт повреждённых контактов без замены узла.\n\n"
                    + (owned ? "ОБОРУДОВАНИЕ УСТАНОВЛЕНО\n" : $"Стоимость: {WorkshopSession.SolderingStationPrice} ед.\n")
                    + $"Баланс мастерской: {session.Balance} ед.\n"
                    + (owned ? "" : missing > 0 ? $"До покупки осталось: {missing} ед.\n" : $"После покупки останется: {session.Balance - WorkshopSession.SolderingStationPrice} ед.\n")
                    + (session.CanOfferSolderingOrders ? "\nДоступны заказы на восстановление контактов."
                        : session.SolderingRepairsReady ? "\nПокупка откроет паяльник и заказ C:\nвосстановление контакта · награда 180 ед."
                        : "\nПайка и новые заказы пока недоступны.\nВ этой версии покупка устанавливает станцию.")
                    + "\n\nДеньги и покупка сохраняются автоматически.\nНезавершённый ремонт после запуска начинается заново.";
            if (status != null) status.text = feedback.Length > 0 ? feedback : owned ? "Станция ваша. Можно вернуться к заказам"
                : missing > 0 ? "Выполняйте доступные заказы на стойке приёма и копите на оборудование"
                : "Средств достаточно. Одно нажатие установит станцию и спишет указанную сумму";
            if (buyButton != null)
            {
                buyButton.interactable = session.isActiveAndEnabled && session.IsProgressReady && !owned && missing == 0;
                var caption = buyButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (caption != null) caption.text = owned ? "Установлена" : missing > 0 ? $"Нужно ещё {missing} ед." : $"Установить · {WorkshopSession.SolderingStationPrice} ед.";
            }
        }

        void Close()
        {
            if (visitor != null) visitor.CloseEquipmentShop();
        }

        public void Hide()
        {
            visitor = null;
            if (panel != null) panel.SetActive(false);
        }

        void OnDisable() => Close();

        void OnDestroy()
        {
            if (buyButton != null) buyButton.onClick.RemoveListener(Buy);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        }
    }
}
