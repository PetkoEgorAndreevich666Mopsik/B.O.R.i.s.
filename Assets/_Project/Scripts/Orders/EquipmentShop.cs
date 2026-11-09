using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BORis
{
    public sealed class EquipmentShop : MonoBehaviour
    {
        [SerializeField] WorkshopSession session;
        [SerializeField] GameObject panel;
        [SerializeField] Text description;
        [SerializeField] Text status;
        [SerializeField] Button buyButton;
        [SerializeField] Button closeButton;
        [SerializeField] GameObject installedStation;
        [SerializeField] GameObject stationPreview;
        InteractionController visitor;
        Button ordersTab, equipmentTab;
        RectTransform frame, screen;
        Text equipmentTabLabel, buyLabel, balanceLabel, projectionLabel;
        TerminalProjection projection;
        bool showingEquipment;
        string feedback = "";

        static readonly Color Cyan = new(.52f, .94f, .92f);
        static readonly Color Amber = new(1f, .73f, .39f);
        static readonly Color Dark = new(.095f, .17f, .23f);

        public bool IsOpen => visitor != null && panel != null && panel.activeSelf;
        public Transform InstallationPoint => stationPreview.transform;

        void Awake()
        {
            panel.SetActive(false);
            buyButton.onClick.AddListener(Buy);
            closeButton.onClick.AddListener(Close);
            buyLabel = buyButton.GetComponentInChildren<Text>();
            frame = (RectTransform)description.transform.parent;
            screen = (RectTransform)panel.transform;
            var header = AddLabel(frame, "Terminal heading", "Б.О.Р.И.С.  /  ТЕРМИНАЛ МАСТЕРСКОЙ", new Vector2(-135, -17), new Vector2(465, 32), 19, Cyan);
            header.alignment = TextAnchor.MiddleLeft;
            balanceLabel = AddLabel(frame, "Workshop balance", "", new Vector2(243, -17), new Vector2(230, 32), 18, Amber);
            balanceLabel.alignment = TextAnchor.MiddleRight;
            ordersTab = AddTab(frame, "OrdersTab", "Заказы", -188, false);
            equipmentTab = AddTab(frame, "EquipmentTab", "Оборудование", 188, true);
            equipmentTabLabel = equipmentTab.GetComponentInChildren<Text>();
            Place(description.rectTransform, new Vector2(-142, -112), new Vector2(450, 340));
            description.alignment = TextAnchor.UpperLeft;
            description.resizeTextForBestFit = true;
            description.resizeTextMinSize = 17;
            description.resizeTextMaxSize = 23;
            description.supportRichText = true;
            Place(status.rectTransform, new Vector2(0, -456), new Vector2(720, 61));
            status.color = Cyan;
            status.alignment = TextAnchor.MiddleLeft;
            var image = new GameObject("Terminal model projection", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(frame, false);
            Place(image.rectTransform, new Vector2(234, -117), new Vector2(246, 246));
            image.raycastTarget = false;
            projectionLabel = AddLabel(frame, "Projection label", "", new Vector2(234, -369), new Vector2(244, 54), 15, Cyan);
            projection = new TerminalProjection(transform, image);
        }

        void Update()
        {
            if (installedStation.activeSelf != session.HasSolderingStation)
                installedStation.SetActive(session.HasSolderingStation);
            if (stationPreview.activeSelf)
                stationPreview.SetActive(false);
            if (IsOpen) Refresh();
        }

        public bool Open(InteractionController interaction)
        {
            if (!isActiveAndEnabled || interaction == null || session == null || !session.isActiveAndEnabled) return false;
            visitor = interaction;
            feedback = "";
            panel.SetActive(true);
            ShowTab(false);
            return true;
        }

        void ShowTab(bool equipment)
        {
            showingEquipment = equipment;
            feedback = "";
            ordersTab.image.color = equipment ? Dark : new Color(.19f, .36f, .40f);
            equipmentTab.image.color = equipment ? new Color(.19f, .36f, .40f) : Dark;
            Refresh();
            EventSystem.current?.SetSelectedGameObject((equipment ? equipmentTab : ordersTab).gameObject);
        }

        void Buy()
        {
            if (!IsOpen || !showingEquipment) return;
            session.TryBuySolderingStation(out feedback);
            Refresh();
        }

        void Refresh()
        {
            float scale = Mathf.Clamp(Mathf.Min((screen.rect.width - 32f) / frame.rect.width,
                (screen.rect.height - 32f) / frame.rect.height), .3f, 1f);
            frame.localScale = Vector3.one * scale;
            var window = session.Window;
            bool owned = session.HasSolderingStation;
            bool ordered = session.SolderingStationOrdered;
            int missing = Mathf.Max(0, WorkshopSession.SolderingStationPrice - session.Balance);
            balanceLabel.text = $"БАЛАНС  {session.Balance} ед.";
            equipmentTabLabel.text = session.EquipmentOfferPending ? "Оборудование  •" : "Оборудование";
            buyButton.gameObject.SetActive(showingEquipment);
            if (showingEquipment)
            {
                description.text = "<b>ПАЯЛЬНАЯ СТАНЦИЯ</b>\n\nВосстанавливает повреждённые контакты."
                    + (session.SolderingRepairsReady ? "\nПосле установки откроет паяльник и заказы на пайку." : "")
                    + (owned ? "\n\n<b>Установлена на верстаке</b>"
                        : ordered ? "\n\n<b>Посылка оплачена</b>"
                        : $"\n\nСтоимость  <b>{WorkshopSession.SolderingStationPrice} ед.</b>"
                          + (missing > 0 ? $"\nНе хватает  {missing} ед." : $"\nПосле покупки останется  <b>{session.Balance - WorkshopSession.SolderingStationPrice} ед.</b>"));
                buyButton.interactable = session.IsProgressReady && !owned && !ordered && missing == 0;
                buyLabel.text = owned ? "Установлена" : ordered ? "Оплачено" : missing > 0 ? $"Нужно ещё {missing} ед." : "Заказать · 180 ед.";
                status.text = feedback.Length > 0 ? feedback : !session.IsProgressReady ? session.SaveStatus
                    : owned ? "Станция справа на верстаке." : ordered ? window.ParcelStatus
                    : missing > 0 ? "Выполните заказы, чтобы накопить на станцию."
                    : "Заберите коробку с ленты и поставьте справа на верстаке.";
                projection.Show(installedStation.transform);
                projectionLabel.text = "ПРОЕКЦИЯ / ОБОРУДОВАНИЕ";
            }
            else
            {
                OrderDefinition order = session.ActiveOrder ?? (window.Robot != null ? window.DisplayedOrder : null);
                var brief = new StringBuilder();
                if (order == null) brief.Append("<b>ОКНО СВОБОДНО</b>");
                else
                {
                    brief.Append($"<b>{order.ClientName.ToUpperInvariant()}  /  {order.Reward} ед.</b>\n");
                    if (session.ActiveOrder != null || window.HasHeardRequest)
                        brief.Append($"\n{order.Symptom}\n{order.Requirements}");
                    else brief.Append("\nПоговорите с роботом у окна.");
                }
                brief.Append("\n\n<b>ЛИНЕЙНАЯ ОЧЕРЕДЬ</b>\n");
                foreach (OrderDefinition item in session.Orders)
                {
                    if (item == null) continue;
                    string state = item == order ? session.ActiveOrder == item ? "в работе" : "у окна"
                        : item.RequiresSolderingStation && !session.CanOfferSolderingOrders ? "требует пайку" : "по очереди";
                    brief.Append($"{item.OrderId}   {item.ClientName}  ·  {state}\n");
                }
                description.text = brief.ToString();
                status.text = !session.IsProgressReady ? session.SaveStatus : ordered ? window.ParcelStatus
                    : session.EquipmentOfferPending ? "Хватает на станцию · вкладка «Оборудование»" : window.StatusText;
                projection.Show(order != null ? order.RobotPrefab.transform : null);
                projectionLabel.text = order != null ? "ПРОЕКЦИЯ / ПОСЕТИТЕЛЬ" : "ПРОЕКЦИЯ / НЕТ ПОСЕТИТЕЛЯ";
            }
        }

        Button AddTab(RectTransform parent, string name, string caption, float x, bool equipment)
        {
            var tab = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            tab.transform.SetParent(parent, false);
            Place((RectTransform)tab.transform, new Vector2(x, -56), new Vector2(360, 40));
            tab.targetGraphic = tab.GetComponent<Image>();
            AddLabel((RectTransform)tab.transform, name + " label", caption, Vector2.zero, new Vector2(350, 40), 20, Color.white);
            tab.onClick.AddListener(() => ShowTab(equipment));
            return tab;
        }

        Text AddLabel(RectTransform parent, string name, string caption, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            Place(label.rectTransform, position, size);
            label.font = description.font;
            label.text = caption;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.color = color;
            return label;
        }

        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        void Close()
        {
            if (visitor != null) visitor.CloseEquipmentShop();
        }

        public void Hide()
        {
            visitor = null;
            projection?.Hide();
            if (panel != null) panel.SetActive(false);
        }

        void OnDisable() => Close();
        void OnDestroy() => projection?.Dispose();
    }
}
