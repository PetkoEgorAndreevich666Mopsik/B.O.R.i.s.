using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BORis
{
    [RequireComponent(typeof(PlayerController))]
    public sealed class InteractionController : MonoBehaviour
    {
        [SerializeField] InputActionAsset input;
        [SerializeField] float reach = 3f;
        [SerializeField] Text hint;
        [SerializeField] Button exitButton;
        [SerializeField] Button restartButton;
        [SerializeField] RepairHUD repairHUD;
        [SerializeField] WorkshopSession session;
        [SerializeField] Text workshopProgress;
        PlayerController player;
        InputActionAsset actions;
        InputActionMap shop;
        InputActionMap bench;
        InputAction move, look, interact, cancel;
        InputAction point, primary, scroll, selectScrewdriver, selectHand, selectBrush;
        InputAction focus, orbit, orbitDelta;
        Workbench activeWorkbench;
        OrderDesk activeOrderDesk;
        ScrewdriverTool screwdriver;
        BrushTool brush;
        RepairTool selectedTool;
        RobotRepair robot;
        Plane dragPlane;
        Vector3 grabOffset;
        string feedback;
        float feedbackUntil;
        int shownBalance;
        float balanceNoticeUntil;
        RepairPart inspectionTarget;
        float observationTime;
        const float ObservationDuration = .5f;
        readonly List<RaycastResult> uiHits = new();

        public bool IsAtWorkbench => activeWorkbench != null && bench != null && bench.enabled;
        public bool IsAtOrderDesk => activeOrderDesk != null;
        public bool IsInspecting => IsAtWorkbench && activeWorkbench.IsFocused;
        public string InspectionName => IsInspecting ? activeWorkbench.InspectionName : "";
        public OrderDefinition ActiveOrder => session != null ? session.ActiveOrder : null;
        public OrderDesk PointedOrderDesk { get; private set; }
        public Workbench PointedWorkbench { get; private set; }
        public ScrewFastener PointedScrew { get; private set; }
        public bool ScrewdriverSelected => screwdriver != null && selectedTool == screwdriver;
        public ScrewdriverTool Screwdriver => screwdriver;
        public BrushTool Brush => brush;
        public bool BrushSelected => brush != null && selectedTool == brush;
        public DirtPatch PointedDirt { get; private set; }
        public RobotRepair Robot => robot;
        public RepairPart HeldPart { get; private set; }
        public RepairPart PointedPart { get; private set; }
        public RepairConnector PointedConnector { get; private set; }
        public RepairTool PointedTool { get; private set; }
        public Transform DropAnchor { get; private set; }
        public bool PointedTestButton { get; private set; }
        public Transform PointedObject { get; private set; }
        public bool IsViewMoving { get; private set; }
        public Camera View => player.View;
        public string Feedback => Time.unscaledTime < feedbackUntil ? feedback : "";
        public float ObservationProgress => inspectionTarget != null
            ? Mathf.Clamp01(observationTime / ObservationDuration) : 0f;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            actions = Instantiate(input);
            shop = actions.FindActionMap("Player", true);
            bench = actions.FindActionMap("Bench", true);
            move = shop.FindAction("Move", true);
            look = shop.FindAction("Look", true);
            interact = shop.FindAction("Interact", true);
            cancel = bench.FindAction("Cancel", true);
            point = bench.FindAction("Point", true);
            primary = bench.FindAction("Primary", true);
            scroll = bench.FindAction("Scroll", true);
            selectScrewdriver = bench.FindAction("Screwdriver", true);
            selectHand = bench.FindAction("Hand", true);
            selectBrush = bench.FindAction("Brush", true);
            focus = bench.FindAction("Focus", true);
            orbit = bench.FindAction("Orbit", true);
            orbitDelta = bench.FindAction("OrbitDelta", true);
            // shortcut: mouse-only look for this PC prototype; calibrate sticks when adding gamepad support.
            look.ApplyBindingOverride(new InputBinding { path = "<Gamepad>/rightStick", overridePath = "" });
            if (exitButton != null) exitButton.onClick.AddListener(ExitWorkbench);
            if (restartButton != null) restartButton.onClick.AddListener(ResetRepair);
        }

        void OnEnable()
        {
            shop.Enable();
            SetCursor(false);
        }

        void Update()
        {
            if (workshopProgress != null && session != null)
            {
                if (shownBalance != session.Balance)
                {
                    shownBalance = session.Balance;
                    balanceNoticeUntil = Time.unscaledTime + 3f;
                }
                workshopProgress.gameObject.SetActive(IsAtOrderDesk || Time.unscaledTime < balanceNoticeUntil);
                workshopProgress.text = $"Баланс: {session.Balance} ед.   |   Заказов выполнено: {session.CompletedOrders}";
            }
            if (IsAtOrderDesk)
            {
                if (!activeOrderDesk.IsOpen || cancel.WasPressedThisFrame()) CloseOrderDesk();
            }
            else if (bench.enabled && activeWorkbench == null) ExitWorkbench();
            else if (IsAtWorkbench)
            {
                if (activeWorkbench == null || !activeWorkbench.isActiveAndEnabled || !activeWorkbench.IsOccupied || cancel.WasPressedThisFrame())
                    ExitWorkbench();
                else TickBench();
            }
            else
            {
                player.Tick(move.ReadValue<Vector2>(), look.ReadValue<Vector2>());
                PointedWorkbench = null;
                PointedOrderDesk = null;
                if (!PointerOverUI() && Physics.Raycast(player.View.transform.position, player.View.transform.forward,
                    out RaycastHit hit, reach, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
                {
                    PointedWorkbench = hit.collider.GetComponentInParent<Workbench>();
                    PointedOrderDesk = hit.collider.GetComponentInParent<OrderDesk>();
                }

                if (PointedOrderDesk != null && interact.WasPressedThisFrame() && PointedOrderDesk.Open(this))
                {
                    activeOrderDesk = PointedOrderDesk;
                    shop.Disable();
                    bench.Enable();
                    SetCursor(true);
                }
                else if (PointedWorkbench != null && interact.WasPressedThisFrame() && !PointedWorkbench.HasRobot)
                    Say("Верстак пуст. Примите заказ на стойке приёма");
                else if (PointedWorkbench != null && interact.WasPressedThisFrame() && PointedWorkbench.Enter(player.View))
                {
                    activeWorkbench = PointedWorkbench;
                    screwdriver = activeWorkbench.GetComponentInChildren<ScrewdriverTool>();
                    brush = activeWorkbench.GetComponentInChildren<BrushTool>();
                    robot = activeWorkbench.Robot;
                    shop.Disable();
                    bench.Enable();
                    SetCursor(true);
                }
            }
            if (hint != null)
            {
                if (IsAtWorkbench && repairHUD != null) repairHUD.Show(this);
                else
                {
                    string message = IsAtOrderDesk ? ""
                        : Feedback.Length > 0 ? Feedback
                        : PointedOrderDesk != null ? "E — стойка приёма заказов"
                        : PointedWorkbench != null ? PointedWorkbench.HasRobot ? "E — сесть за верстак" : "Верстак пуст · получите заказ на стойке"
                        : session != null && session.ActiveOrder == null ? "Примите заказ на стойке слева"
                        : "";
                    if (repairHUD != null) repairHUD.ShowShop(message);
                    else hint.text = message;
                }
            }
            if (exitButton != null) exitButton.gameObject.SetActive(IsAtWorkbench);
            if (restartButton != null) restartButton.gameObject.SetActive(IsAtWorkbench);
        }

        void TickBench()
        {
            IsViewMoving = false;
            PointedObject = null;
            PointedTestButton = false;
            if (robot != null && robot.IsTesting) { ClearObservation(); return; }
            if (selectHand.WasPressedThisFrame())
                SelectTool(null);
            if (selectScrewdriver.WasPressedThisFrame() && screwdriver != null)
                SelectTool(screwdriver);
            if (selectBrush.WasPressedThisFrame() && brush != null)
                SelectTool(brush);
            Vector2 pointer = point.ReadValue<Vector2>();
            PointedScrew = null;
            PointedPart = null;
            PointedConnector = null;
            PointedDirt = null;
            bool overUI = PointerOverUI();
            PointedTool = null;
            if (selectedTool != null && !selectedTool.IsActive) selectedTool.FollowPointer(pointer);
            if (HeldPart != null)
            {
                ClearObservation();
                if (overUI) { CancelPart(); Say("Перенос отменён"); }
                else
                {
                    Ray ray = player.View.ScreenPointToRay(pointer);
                    if (dragPlane.Raycast(ray, out float distance)) HeldPart.transform.position = ray.GetPoint(distance) + grabOffset;
                    DropAnchor = FindDropAnchor(ray);
                    if (!primary.IsPressed()) FinishPart(ray);
                }
                return;
            }
            if (selectedTool != null && selectedTool.IsActive)
            {
                ClearObservation();
                if (!primary.IsPressed() || overUI) selectedTool.End();
                else if (ScrewdriverSelected && scroll.ReadValue<Vector2>().y != 0f)
                    screwdriver.Scroll(scroll.ReadValue<Vector2>().y, pointer);
                else selectedTool.Drag(pointer);
                if (!selectedTool.IsActive) selectedTool.FollowPointer(pointer);
                return;
            }
            RaycastHit hit = default;
            bool hasHit = !overUI && Physics.Raycast(player.View.ScreenPointToRay(pointer), out hit, reach,
                ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore);
            // Restrict tools and focus to this table; the closest collider also blocks hidden screws.
            hasHit &= hit.collider != null && hit.collider.GetComponentInParent<Workbench>() == activeWorkbench;
            if (hasHit)
            {
                PointedObject = hit.transform;
                PointedScrew = hit.collider.GetComponentInParent<ScrewFastener>();
                PointedPart = hit.collider.GetComponentInParent<RepairPart>();
                PointedConnector = hit.collider.GetComponentInParent<RepairConnector>();
                PointedDirt = hit.collider.GetComponent<DirtPatch>();
                PointedTestButton = hit.collider == activeWorkbench.TestButton;
                PointedTool = hit.collider.GetComponentInParent<RepairTool>();
            }
            bool canMoveView = !overUI && !primary.IsPressed();
            if (canMoveView && focus.WasPressedThisFrame())
            {
                if (IsInspecting) activeWorkbench.ReturnToOverview();
                else if (hasHit && hit.collider.GetComponentInParent<RobotRepair>() == robot)
                    activeWorkbench.Focus(PointedPart);
            }
            bool orbiting = canMoveView && IsInspecting && orbit.IsPressed();
            bool viewMoving = activeWorkbench.UpdateView(orbiting ? orbitDelta.ReadValue<Vector2>() : Vector2.zero,
                canMoveView ? scroll.ReadValue<Vector2>().y : 0f);
            IsViewMoving = viewMoving || orbiting;
            if (selectedTool != null) selectedTool.FollowPointer(pointer);
            ObserveDrive(hasHit && !viewMoving && !orbiting && !primary.IsPressed() ? PointedPart : null);
            if (viewMoving || orbiting || overUI || !hasHit) return;
            if (!primary.WasPressedThisFrame()) return;
            if (PointedTestButton)
            {
                SelectTool(null);
                if (!robot.RunTest(out string reason)) Say(reason);
                else Say("");
            }
            else if (hit.collider.GetComponentInParent<ScrewdriverTool>() == screwdriver && screwdriver != null)
                SelectTool(screwdriver);
            else if (hit.collider.GetComponentInParent<BrushTool>() == brush && brush != null)
                SelectTool(brush);
            else if (BrushSelected && PointedDirt != null)
            {
                if (robot.CanClean(PointedDirt, out string reason)) brush.Begin(PointedDirt, player.View, pointer);
                else Say(reason);
            }
            else if (ScrewdriverSelected && PointedScrew != null)
            {
                if (robot.CanTurn(PointedScrew, out string reason)) screwdriver.Begin(PointedScrew, player.View, pointer);
                else Say(reason);
            }
            else if (selectedTool == null && robot != null)
            {
                if (PointedConnector != null)
                {
                    if (robot.ToggleConnection(PointedConnector, out string reason))
                        Say(PointedConnector.IsConnected ? "Разъём подключён" : "Разъём отключён");
                    else Say(reason);
                }
                else if (PointedPart != null)
                {
                    if (!robot.CanMove(PointedPart, out string reason)) { Say(reason); return; }
                    HeldPart = PointedPart;
                    dragPlane = new Plane(player.View.transform.forward, HeldPart.transform.position);
                    Ray ray = player.View.ScreenPointToRay(pointer);
                    grabOffset = dragPlane.Raycast(ray, out float distance) ? HeldPart.transform.position - ray.GetPoint(distance) : Vector3.zero;
                    HeldPart.SetDragging(true);
                    Say("");
                }
            }
        }

        void ObserveDrive(RepairPart part)
        {
            if (robot == null || part == null || part.Kind != RepairPartKind.Drive
                || !part.IsInstalled || robot.Cover.IsInstalled || robot.InspectedDrive == part)
            {
                ClearObservation();
                return;
            }
            if (inspectionTarget != part) { inspectionTarget = part; observationTime = 0f; }
            observationTime += Time.unscaledDeltaTime;
            if (observationTime >= ObservationDuration) robot.InspectDrive(part);
        }

        void ClearObservation()
        {
            inspectionTarget = null;
            observationTime = 0f;
        }

        void SelectTool(RepairTool tool)
        {
            CancelPart();
            if (selectedTool != null) selectedTool.Select(null);
            selectedTool = tool;
            if (selectedTool != null)
            {
                selectedTool.Select(player.View);
                selectedTool.FollowPointer(point.ReadValue<Vector2>());
            }
        }

        public void SelectToolShortcut(int slot)
        {
            if (!IsAtWorkbench || robot == null || robot.IsTesting || slot < 0 || slot > 2) return;
            SelectTool(slot == 1 ? screwdriver : slot == 2 ? brush : null);
        }

        Transform FindDropAnchor(Ray ray)
        {
            // The lid and drive sockets overlap; query only the held part's compatible destinations.
            Collider install = HeldPart.InstallationAnchor.GetComponent<Collider>();
            Collider tray = HeldPart.TrayAnchor.GetComponent<Collider>();
            bool hitInstall = install != null && install.Raycast(ray, out _, reach);
            bool hitTray = tray != null && tray.Raycast(ray, out _, reach);
            if (!hitInstall) return hitTray ? HeldPart.TrayAnchor : null;
            if (!hitTray) return HeldPart.InstallationAnchor;
            install.Raycast(ray, out RaycastHit installedHit, reach);
            tray.Raycast(ray, out RaycastHit trayHit, reach);
            return installedHit.distance <= trayHit.distance ? HeldPart.InstallationAnchor : HeldPart.TrayAnchor;
        }

        void FinishPart(Ray ray)
        {
            if (DropAnchor == null) Say("Перенос отменён: отпустите над посадочным местом или своим лотком");
            else
            {
                bool install = DropAnchor == HeldPart.InstallationAnchor;
                if (!robot.CanPlace(HeldPart, install, out string reason)) Say(reason);
                else if (Physics.Raycast(ray, out RaycastHit obstacle, reach, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)
                    && DropAnchor.GetComponent<Collider>().Raycast(ray, out RaycastHit destination, reach)
                    && obstacle.distance < destination.distance - .05f) Say("Посадочное место перекрыто предметом");
                else if (robot.TryPlace(HeldPart, install, out reason)) Say(install ? "Деталь установлена" : "Деталь в лотке");
                else Say(reason);
            }
            CancelPart();
        }

        void CancelPart()
        {
            if (HeldPart != null)
            {
                HeldPart.RestorePose();
                if (activeWorkbench != null) activeWorkbench.ReturnToOverview();
            }
            HeldPart = null;
            DropAnchor = null;
        }

        void Say(string message)
        {
            feedback = message;
            feedbackUntil = Time.unscaledTime + 3f;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                ClearObservation();
                if (selectedTool != null) selectedTool.End();
                CancelPart();
                if (robot != null) robot.CancelTest();
            }
        }

        bool PointerOverUI()
        {
            if (EventSystem.current == null) return false;
            Vector2 position = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : Mouse.current?.position.ReadValue() ?? Vector2.zero;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0;
        }

        public void ExitWorkbench()
        {
            PointedObject = null;
            IsViewMoving = false;
            ClearObservation();
            SelectTool(null);
            if (robot != null) robot.CancelTest();
            screwdriver = null;
            brush = null;
            PointedScrew = null;
            PointedPart = null;
            PointedConnector = null;
            PointedDirt = null;
            PointedTestButton = false;
            robot = null;
            PointedTool = null;
            Say("");
            if (activeWorkbench != null) activeWorkbench.Exit();
            activeWorkbench = null;
            bench.Disable();
            shop.Enable();
            SetCursor(false);
        }

        public void ResetRepair()
        {
            if (!IsAtWorkbench || activeWorkbench == null) return;
            ClearObservation();
            SelectTool(null);
            RobotRepair replacement = activeWorkbench.ResetRobot();
            if (replacement == null) { Say("Повторение недоступно: отсутствует исходный робот"); return; }
            robot = replacement;
            PointedScrew = null;
            PointedPart = null;
            PointedConnector = null;
            PointedDirt = null;
            PointedTestButton = false;
            Say("Новый ремонт: робот и инструменты готовы");
            PointedTool = null;
            if (ActiveOrder != null) Say("Текущий заказ начат заново");
        }

        public void CloseOrderDesk()
        {
            if (activeOrderDesk != null) activeOrderDesk.Hide();
            activeOrderDesk = null;
            bench.Disable();
            shop.Enable();
            SetCursor(false);
        }

        static void SetCursor(bool atBench)
        {
            Cursor.lockState = atBench ? CursorLockMode.Confined : CursorLockMode.Locked;
            Cursor.visible = atBench;
        }

        void OnDisable()
        {
            CloseOrderDesk();
            ExitWorkbench();
            actions.Disable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnDestroy()
        {
            if (exitButton != null) exitButton.onClick.RemoveListener(ExitWorkbench);
            if (restartButton != null) restartButton.onClick.RemoveListener(ResetRepair);
            Destroy(actions);
        }
    }
}
