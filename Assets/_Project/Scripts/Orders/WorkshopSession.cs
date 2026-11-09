using UnityEngine;

namespace BORis
{
    public sealed class WorkshopSession : MonoBehaviour
    {
        [SerializeField] Workbench workbench;
        [SerializeField] RobotTransport transport;
        [SerializeField] ServiceWindow serviceWindow;
        public RobotTransport Transport => transport;
        public ServiceWindow Window => serviceWindow;
        [SerializeField] OrderDefinition[] orders = System.Array.Empty<OrderDefinition>();
        // shortcut: scene-authored readiness; enable only after the soldering tool and its orders are integrated.
        [SerializeField] bool solderingRepairsReady;
        int nextOrder;
        WorkshopProgressStore progressStore;
        string lastDeliveredInstance;
        OrderDefinition visitorOrder;
        RobotRepair visitorRobot;

        public const int SolderingStationPrice = 180;
        public bool HasSolderingStation { get; private set; }
        public bool SolderingStationOrdered { get; private set; }
        public bool EquipmentOfferPending { get; private set; }
        public System.Collections.Generic.IReadOnlyList<OrderDefinition> Orders => orders;
        public bool SolderingRepairsReady => solderingRepairsReady;
        public bool CanOfferSolderingOrders => HasSolderingStation && solderingRepairsReady;
        public OrderDefinition OfferedOrder
        {
            get
            {
                if (VisitorRobot != null && workbench != null && workbench.Robot == visitorRobot) return visitorOrder;
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
        public RobotRepair ActiveRobot => ActiveOrder != null && workbench != null ? workbench.Robot : null;
        public RobotRepair VisitorRobot => ActiveOrder == null ? visitorRobot : null;
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
                reason = "Сохранение не подходит текущим заказам. Файл не изменён. Повторите загрузку у окна.";
            else if (workbench == null || !workbench.isActiveAndEnabled || !workbench.ClearRobot()
                || restoredOrder != null && workbench.LoadRobot(restoredOrder.RobotPrefab) == null)
                reason = "Не удалось восстановить верстак. Повторите загрузку у окна.";
            else
            {
                Balance = data != null ? data.balance : 0;
                HasSolderingStation = data != null && data.hasSolderingStation;
                SolderingStationOrdered = data != null && data.solderingStationOrdered;
                EquipmentOfferPending = data != null && data.equipmentOfferPending;
                CompletedOrders = data != null ? data.completedOrders : 0;
                nextOrder = restoredNext;
                ActiveOrder = restoredOrder;
                ActiveOrderId = data?.activeInstance;
                ActiveOrderReward = data != null ? data.activeReward : 0;
                if (restoredOrder != null && transport != null) transport.ReceiveRobot();
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
            solderingStationOrdered = SolderingStationOrdered, equipmentOfferPending = EquipmentOfferPending,
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

        public bool TrySaveDebugCheckpoint(int orderIndex, out string reason)
        {
            reason = "";
            if (orders == null || orderIndex < 0 || orderIndex >= orders.Length)
                reason = "Такого заказа нет";
            if (reason.Length > 0) return false;
            long balance = 0;
            bool station = false;
            for (int i = 0; i <= orderIndex; i++)
            {
                OrderDefinition order = orders[i];
                if (order == null || !order.IsValid) { reason = "Проверьте данные заказов"; return false; }
                if (i < orderIndex) balance += order.Reward;
                station |= order.RequiresSolderingStation;
            }
            if (station && !solderingRepairsReady) { reason = "Пайка пока недоступна в этой сцене"; return false; }
            if (station) balance -= SolderingStationPrice;
            if (balance < 0 || balance > int.MaxValue) { reason = "Не удалось рассчитать баланс для этого заказа"; return false; }
            return new WorkshopProgressStore(SavePath).TryReset(new WorkshopProgress
            {
                version = 1, nextOrder = orders[orderIndex].OrderId, balance = (int)balance,
                completedOrders = orderIndex, hasSolderingStation = station
            }, out reason);
        }

        public bool TryPrepareVisitor(out string reason)
        {
            reason = "";
            OrderDefinition offered = OfferedOrder;
            if (!IsProgressReady) reason = SaveStatus;
            else if (ActiveOrder != null) reason = "Уже есть активный заказ";
            else if (offered == null || !offered.IsValid || workbench == null || workbench.IsOccupied)
                reason = "Посетитель пока недоступен";
            else if (workbench.HasRobot) reason = "Освободите верстак перед прибытием посетителя";
            if (reason.Length > 0) return false;
            if (visitorOrder == offered && visitorRobot != null && workbench.Robot == visitorRobot) return true;
            visitorRobot = workbench.LoadRobot(offered.RobotPrefab);
            visitorOrder = visitorRobot != null ? offered : null;
            if (visitorRobot == null) { reason = "Не удалось подготовить посетителя"; return false; }
            if (transport != null) transport.ReceiveRobot();
            return true;
        }

        public bool TryAcceptOrder(out string reason)
        {
            OrderDefinition offeredOrder = OfferedOrder;
            bool prepared = visitorOrder == offeredOrder && visitorRobot != null && workbench != null
                && workbench.Robot == visitorRobot;
            reason = "";
            if (ActiveOrder != null) reason = "Уже есть активный заказ";
            else if (!IsProgressReady) reason = SaveStatus;
            else if (!isActiveAndEnabled || workbench == null || offeredOrder == null || !offeredOrder.IsValid)
                reason = "Заказ недоступен: проверьте его данные и верстак";
            else if (offeredOrder.RequiresSolderingStation && !CanOfferSolderingOrders) reason = "Пайка пока недоступна";
            else if (!prepared && workbench.LoadRobot(offeredOrder.RobotPrefab) == null)
                reason = "Освободите верстак перед приёмом заказа";
            if (reason.Length > 0) return false;
            var snapshot = Snapshot();
            snapshot.equipmentOfferPending = false;
            snapshot.activeOrder = offeredOrder.OrderId;
            snapshot.activeInstance = System.Guid.NewGuid().ToString("N");
            snapshot.activeReward = offeredOrder.Reward;
            if (!Save(snapshot, "Заказ сохранён. После запуска ремонт начнётся заново.", out reason))
            {
                if (!prepared) workbench.ClearRobot();
                return false;
            }
            ActiveOrder = offeredOrder;
            EquipmentOfferPending = false;
            ActiveOrderId = snapshot.activeInstance;
            ActiveOrderReward = offeredOrder.Reward;
            visitorOrder = null;
            visitorRobot = null;
            if (transport != null) transport.ReceiveRobot();
            return true;
        }

        public bool TryBuySolderingStation(out string reason)
        {
            reason = "";
            if (!isActiveAndEnabled) reason = "Мастерская недоступна";
            else if (!IsProgressReady) reason = SaveStatus;
            else if (HasSolderingStation) reason = "Паяльная станция уже установлена";
            else if (SolderingStationOrdered) reason = "Станция уже оплачена";
            else if (Balance < SolderingStationPrice) reason = $"Не хватает {SolderingStationPrice - Balance} ед. Выполняйте заказы у окна";
            if (reason.Length > 0) return false;
            var snapshot = Snapshot();
            snapshot.balance -= SolderingStationPrice;
            snapshot.solderingStationOrdered = true;
            snapshot.equipmentOfferPending = false;
            if (!Save(snapshot, "Заказ оборудования и баланс сохранены.", out reason)) return false;
            Balance = snapshot.balance;
            SolderingStationOrdered = true;
            EquipmentOfferPending = false;
            return true;
        }

        public bool TryInstallSolderingStation(out string reason)
        {
            reason = "Сначала закажите станцию";
            if (!isActiveAndEnabled || !IsProgressReady || !SolderingStationOrdered) return false;
            var snapshot = Snapshot();
            snapshot.solderingStationOrdered = false;
            snapshot.hasSolderingStation = true;
            if (!Save(snapshot, "Установка оборудования сохранена.", out reason)) return false;
            SolderingStationOrdered = false;
            HasSolderingStation = true;
            return true;
        }

        public bool TryContinueWithoutEquipment(out string reason)
        {
            reason = "";
            if (!EquipmentOfferPending) return true;
            var snapshot = Snapshot();
            snapshot.equipmentOfferPending = false;
            if (!Save(snapshot, "Очередь продолжена.", out reason)) return false;
            EquipmentOfferPending = false;
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
            else if (workbench.IsOccupied) reason = "Сначала выйдите из режима верстака и вернитесь к окну";
            else if (!robot.CanRunTest(out reason)) return false;
            else if (!robot.IsFunctional) reason = "Неисправность не устранена: проверьте привод и контакты";
            else if (robot.TestResult != RobotTestResult.Passed) reason = "Соберите робота и завершите успешный тест руки";
            else if (ActiveOrder.RequiresCleaning && !robot.CheckCleaning(out reason)) return false;
            else if (transport != null && (!transport.IsAtDelivery || transport.IsCarrying)) reason = "Отнесите робота в зону выдачи на ленте окна";
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
            snapshot.equipmentOfferPending = !HasSolderingStation && !SolderingStationOrdered && solderingRepairsReady
                && Balance < SolderingStationPrice && snapshot.balance >= SolderingStationPrice;
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
            EquipmentOfferPending = snapshot.equipmentOfferPending;
            CompletedOrders++;
            ActiveOrder = null;
            ActiveOrderId = null;
            ActiveOrderReward = 0;
            nextOrder = followingOrder;
            return true;
        }
    }
}
