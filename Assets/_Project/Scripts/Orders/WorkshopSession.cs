using UnityEngine;

namespace BORis
{
    public sealed class WorkshopSession : MonoBehaviour
    {
        [SerializeField] Workbench workbench;
        [SerializeField] OrderDefinition[] orders = System.Array.Empty<OrderDefinition>();
        // shortcut: scene-authored readiness; enable only after the soldering tool and its orders are integrated.
        [SerializeField] bool solderingRepairsReady;
        int nextOrder;

        public const int SolderingStationPrice = 180;
        public bool HasSolderingStation { get; private set; }
        public bool CanOfferSolderingOrders => HasSolderingStation && solderingRepairsReady;
        public OrderDefinition OfferedOrder
        {
            get
            {
                if (orders == null) return null;
                for (int i = 0; i < orders.Length; i++)
                {
                    OrderDefinition order = orders[(nextOrder + i) % orders.Length];
                    if (order != null && (!order.RequiresSolderingStation || CanOfferSolderingOrders)) return order;
                }
                return null;
            }
        }
        public OrderDefinition ActiveOrder { get; private set; }
        public string ActiveOrderId { get; private set; }
        public int ActiveOrderReward { get; private set; }
        public int Balance { get; private set; }
        public int CompletedOrders { get; private set; }
        public RobotRepair ActiveRobot => workbench != null ? workbench.Robot : null;

        void Start()
        {
            if (ActiveOrder == null && workbench != null) workbench.ClearRobot();
        }

        public bool TryAcceptOrder(out string reason)
        {
            OrderDefinition offeredOrder = OfferedOrder;
            reason = "";
            if (ActiveOrder != null) reason = "Уже есть активный заказ. Робот ждёт на верстаке";
            else if (!isActiveAndEnabled || workbench == null || offeredOrder == null || !offeredOrder.IsValid)
                reason = "Заказ недоступен: проверьте его данные и верстак";
            else if (offeredOrder.RequiresSolderingStation && !CanOfferSolderingOrders) reason = "Пайка пока недоступна";
            else if (workbench.LoadRobot(offeredOrder.RobotPrefab) == null)
                reason = "Освободите верстак перед приёмом заказа";
            if (reason.Length > 0) return false;
            ActiveOrder = offeredOrder;
            ActiveOrderId = System.Guid.NewGuid().ToString("N");
            ActiveOrderReward = offeredOrder.Reward;
            return true;
        }

        public bool TryBuySolderingStation(out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled) reason = "Мастерская недоступна";
            else if (HasSolderingStation) reason = "Паяльная станция уже установлена";
            else if (Balance < SolderingStationPrice) reason = $"Не хватает {SolderingStationPrice - Balance} ед. Выполняйте заказы на стойке";
            if (reason.Length > 0) return false;
            Balance -= SolderingStationPrice;
            HasSolderingStation = true;
            return true;
        }

        public bool CanDeliverOrder(out string reason)
        {
            reason = "";
            RobotRepair robot = ActiveRobot;
            if (ActiveOrder == null) reason = "Нет активного заказа для выдачи";
            else if (!isActiveAndEnabled || workbench == null || !workbench.isActiveAndEnabled || robot == null || !robot.isActiveAndEnabled)
                reason = "Робот недоступен для выдачи";
            else if (workbench.IsOccupied) reason = "Сначала выйдите из режима верстака и вернитесь на стойку";
            else if (!robot.CanRunTest(out reason)) return false;
            else if (!robot.IsFunctional) reason = "Неисправность не устранена: проверьте привод";
            else if (robot.TestResult != RobotTestResult.Passed) reason = "Соберите робота и завершите успешный тест руки";
            else if (ActiveOrder.RequiresCleaning && !robot.CheckCleaning(out reason)) return false;
            return reason.Length == 0;
        }

        public bool TryDeliverOrder(string orderInstanceId, out string reason)
        {
            if (string.IsNullOrEmpty(orderInstanceId) || orderInstanceId != ActiveOrderId)
            {
                reason = "Этот заказ уже выдан или больше не активен";
                return false;
            }
            if (!CanDeliverOrder(out reason)) return false;
            if (ActiveOrderReward > int.MaxValue - Balance)
            {
                reason = "Невозможно начислить награду: достигнут предел баланса";
                return false;
            }
            if (!workbench.ClearRobot())
            {
                reason = "Верстак занят: выдача отменена";
                return false;
            }
            int completedIndex = orders != null ? System.Array.IndexOf(orders, ActiveOrder) : -1;
            Balance += ActiveOrderReward;
            CompletedOrders++;
            ActiveOrder = null;
            ActiveOrderId = null;
            ActiveOrderReward = 0;
            nextOrder = orders != null && orders.Length > 0 ? (completedIndex + 1) % orders.Length : 0;
            return true;
        }
    }
}
