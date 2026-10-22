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
        WorkshopProgressStore progressStore;
        string lastDeliveredInstance;

        public const int SolderingStationPrice = 180;
        public bool HasSolderingStation { get; private set; }
        public bool SolderingRepairsReady => solderingRepairsReady;
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
        public bool IsProgressReady { get; private set; }
        public string SaveStatus { get; private set; } = "Загрузка прогресса…";
        public string SavePath => progressStore != null ? progressStore.FilePath : WorkshopProgressStore.DefaultPath;
        public const string SaveRule = "Деньги и покупки сохраняются. После запуска незавершённый ремонт начинается заново.";

        void Start()
        {
            progressStore = new WorkshopProgressStore(WorkshopProgressStore.DefaultPath);
            if (workbench != null) workbench.ClearRobot();
            TryReloadProgress(out _);
        }

        public bool TryReloadProgress(out string reason)
        {
            reason = "";
            if (IsProgressReady) { reason = "Прогресс уже загружен"; return false; }
            if (progressStore == null || !progressStore.TryLoad(out WorkshopProgress data, out reason))
            {
                SaveStatus = reason;
                return false;
            }
            int restoredNext = data != null && orders != null ? System.Array.FindIndex(orders, o => o != null && o.OrderId == data.nextOrder) : 0;
            OrderDefinition restoredOrder = data != null && !string.IsNullOrEmpty(data.activeOrder) && orders != null
                ? System.Array.Find(orders, o => o != null && o.OrderId == data.activeOrder) : null;
            if (data != null && (restoredNext < 0 || !string.IsNullOrEmpty(data.activeOrder)
                && (restoredOrder == null || !restoredOrder.IsValid || restoredOrder.RequiresSolderingStation && !data.hasSolderingStation)))
                reason = "Сохранение не подходит текущим заказам. Файл не изменён. Повторите загрузку на стойке.";
            else if (workbench == null || !workbench.isActiveAndEnabled || !workbench.ClearRobot()
                || restoredOrder != null && workbench.LoadRobot(restoredOrder.RobotPrefab) == null)
                reason = "Не удалось восстановить верстак. Повторите загрузку на стойке.";
            else
            {
                Balance = data != null ? data.balance : 0;
                HasSolderingStation = data != null && data.hasSolderingStation;
                CompletedOrders = data != null ? data.completedOrders : 0;
                nextOrder = restoredNext;
                ActiveOrder = restoredOrder;
                ActiveOrderId = data?.activeInstance;
                ActiveOrderReward = data != null ? data.activeReward : 0;
                lastDeliveredInstance = data?.lastDeliveredInstance;
                IsProgressReady = true;
                SaveStatus = reason.Length > 0 ? reason : restoredOrder != null
                    ? "Заказ восстановлен с начала ремонта. Деньги и оборудование загружены."
                    : data != null ? "Прогресс загружен. " + SaveRule : SaveRule;
                return true;
            }
            SaveStatus = reason;
            return false;
        }

        WorkshopProgress Snapshot() => new WorkshopProgress
        {
            version = 1, balance = Balance, hasSolderingStation = HasSolderingStation, completedOrders = CompletedOrders,
            nextOrder = orders != null && orders.Length > 0 && orders[nextOrder] != null ? orders[nextOrder].OrderId : "",
            activeOrder = ActiveOrder != null ? ActiveOrder.OrderId : "", activeInstance = ActiveOrderId,
            activeReward = ActiveOrderReward, lastDeliveredInstance = lastDeliveredInstance
        };

        bool Save(WorkshopProgress progress, string success, out string reason)
        {
            bool saved = progressStore.TrySave(progress, out reason);
            SaveStatus = saved ? success : reason;
            return saved;
        }

        public bool TryAcceptOrder(out string reason)
        {
            OrderDefinition offeredOrder = OfferedOrder;
            reason = "";
            if (ActiveOrder != null) reason = "Уже есть активный заказ. Робот ждёт на верстаке";
            else if (!IsProgressReady) reason = SaveStatus;
            else if (!isActiveAndEnabled || workbench == null || offeredOrder == null || !offeredOrder.IsValid)
                reason = "Заказ недоступен: проверьте его данные и верстак";
            else if (offeredOrder.RequiresSolderingStation && !CanOfferSolderingOrders) reason = "Пайка пока недоступна";
            else if (workbench.LoadRobot(offeredOrder.RobotPrefab) == null)
                reason = "Освободите верстак перед приёмом заказа";
            if (reason.Length > 0) return false;
            var snapshot = Snapshot();
            snapshot.activeOrder = offeredOrder.OrderId;
            snapshot.activeInstance = System.Guid.NewGuid().ToString("N");
            snapshot.activeReward = offeredOrder.Reward;
            if (!Save(snapshot, "Заказ сохранён. После запуска ремонт начнётся заново.", out reason))
            {
                workbench.ClearRobot();
                return false;
            }
            ActiveOrder = offeredOrder;
            ActiveOrderId = snapshot.activeInstance;
            ActiveOrderReward = offeredOrder.Reward;
            return true;
        }

        public bool TryBuySolderingStation(out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled) reason = "Мастерская недоступна";
            else if (!IsProgressReady) reason = SaveStatus;
            else if (HasSolderingStation) reason = "Паяльная станция уже установлена";
            else if (Balance < SolderingStationPrice) reason = $"Не хватает {SolderingStationPrice - Balance} ед. Выполняйте заказы на стойке";
            if (reason.Length > 0) return false;
            var snapshot = Snapshot();
            snapshot.balance -= SolderingStationPrice;
            snapshot.hasSolderingStation = true;
            if (!Save(snapshot, "Покупка и баланс сохранены.", out reason)) return false;
            Balance -= SolderingStationPrice;
            HasSolderingStation = true;
            return true;
        }

        public bool CanDeliverOrder(out string reason)
        {
            reason = "";
            RobotRepair robot = ActiveRobot;
            if (ActiveOrder == null) reason = "Нет активного заказа для выдачи";
            else if (!IsProgressReady) reason = SaveStatus;
            else if (!isActiveAndEnabled || workbench == null || !workbench.isActiveAndEnabled || robot == null || !robot.isActiveAndEnabled)
                reason = "Робот недоступен для выдачи";
            else if (workbench.IsOccupied) reason = "Сначала выйдите из режима верстака и вернитесь на стойку";
            else if (!robot.CanRunTest(out reason)) return false;
            else if (!robot.IsFunctional) reason = "Неисправность не устранена: проверьте привод и контакты";
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
            if (ActiveOrderReward > int.MaxValue - Balance || CompletedOrders == int.MaxValue)
            {
                reason = "Невозможно начислить награду: достигнут предел баланса";
                return false;
            }
            int completedIndex = orders != null ? System.Array.IndexOf(orders, ActiveOrder) : -1;
            int followingOrder = orders != null && orders.Length > 0 ? (completedIndex + 1) % orders.Length : 0;
            var snapshot = Snapshot();
            snapshot.balance += ActiveOrderReward;
            snapshot.completedOrders++;
            snapshot.activeOrder = snapshot.activeInstance = "";
            snapshot.activeReward = 0;
            snapshot.lastDeliveredInstance = ActiveOrderId;
            snapshot.nextOrder = orders != null && orders.Length > 0 ? orders[followingOrder].OrderId : "";
            if (!Save(snapshot, "Оплата и прогресс сохранены.", out reason)) return false;
            // CanDeliverOrder checked occupancy; no frame or callback runs between validation and this commit.
            workbench.ClearRobot();
            lastDeliveredInstance = ActiveOrderId;
            Balance += ActiveOrderReward;
            CompletedOrders++;
            ActiveOrder = null;
            ActiveOrderId = null;
            ActiveOrderReward = 0;
            nextOrder = followingOrder;
            return true;
        }
    }
}
