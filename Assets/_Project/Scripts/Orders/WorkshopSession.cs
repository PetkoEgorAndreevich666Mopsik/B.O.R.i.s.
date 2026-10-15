using UnityEngine;

namespace BORis
{
    public sealed class WorkshopSession : MonoBehaviour
    {
        [SerializeField] Workbench workbench;
        [SerializeField] OrderDefinition offeredOrder;

        public OrderDefinition OfferedOrder => offeredOrder;
        public OrderDefinition ActiveOrder { get; private set; }
        public string ActiveOrderId { get; private set; }
        public RobotRepair ActiveRobot => workbench != null ? workbench.Robot : null;

        void Start()
        {
            if (ActiveOrder == null && workbench != null) workbench.ClearRobot();
        }

        public bool TryAcceptOrder(out string reason)
        {
            reason = "";
            if (ActiveOrder != null) reason = "Уже есть активный заказ. Робот ждёт на верстаке";
            else if (!isActiveAndEnabled || workbench == null || offeredOrder == null || !offeredOrder.IsValid)
                reason = "Заказ недоступен: проверьте его данные и верстак";
            else if (offeredOrder.RequiresSolderingStation) reason = "Для заказа нужна паяльная станция";
            else if (workbench.LoadRobot(offeredOrder.RobotPrefab) == null)
                reason = "Освободите верстак перед приёмом заказа";
            if (reason.Length > 0) return false;
            ActiveOrder = offeredOrder;
            ActiveOrderId = System.Guid.NewGuid().ToString("N");
            return true;
        }
    }
}
