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
        [SerializeField] Button alignInspectionButton;
        [SerializeField] RepairHUD repairHUD;
        [SerializeField] WorkshopSession session;
        [SerializeField] Text workshopProgress;
        [SerializeField] Text saveStatus;
        PlayerController player;
        InputActionAsset actions;
        InputActionMap shop;
        InputActionMap bench;
        InputAction move, look, interact, cancel;
        InputAction point, primary, scroll, selectScrewdriver, selectHand, selectBrush, selectSoldering;
        InputAction orbit, orbitDelta, alignInspection;
        Workbench activeWorkbench;
        OrderDesk activeOrderDesk;
        EquipmentShop activeEquipmentShop;
        ScrewdriverTool screwdriver;
        BrushTool brush;
        SolderingTool soldering;
        RepairTool selectedTool;
        RobotRepair robot;
        Plane dragPlane;
        Vector3 grabOffset;
        string feedback;
        float feedbackUntil;
        RepairPart inspectionTarget;
        SolderJoint jointInspectionTarget;
        float observationTime;
        const float ObservationDuration = .5f;
        readonly List<RaycastResult> uiHits = new();

        public bool IsAtWorkbench => activeWorkbench != null && bench != null && bench.enabled;
        public bool IsAtOrderDesk => activeOrderDesk != null;
        public bool IsAtEquipmentShop => activeEquipmentShop != null;
        public bool IsInspecting => IsAtWorkbench && activeWorkbench.IsFocused;
        float RepairReach => reach + (activeWorkbench != null ? activeWorkbench.CameraOffset : 0f);
        public string InspectionName => IsInspecting ? activeWorkbench.InspectionName : "";
        public OrderDefinition ActiveOrder => session != null ? session.ActiveOrder : null;
        public WorkshopSession Session => session;
        public OrderDesk PointedOrderDesk { get; private set; }
        public EquipmentShop PointedEquipmentShop { get; private set; }
        public Workbench PointedWorkbench { get; private set; }
        public ScrewFastener PointedScrew { get; private set; }
        public bool ScrewdriverSelected => screwdriver != null && selectedTool == screwdriver;
        public ScrewdriverTool Screwdriver => screwdriver;
        public BrushTool Brush => brush;
        public SolderingTool Soldering => soldering;
        public bool SolderingAvailable => soldering != null && soldering.IsAvailable;
        public bool SolderingSelected => soldering != null && selectedTool == soldering;
        public SolderJoint PointedJoint { get; private set; }
        public bool BrushSelected => brush != null && selectedTool == brush;
        public DirtPatch PointedDirt { get; private set; }
        public RobotRepair Robot => robot;
        public RepairPart HeldPart { get; private set; }
        public RepairPart PointedPart { get; private set; }
        public RepairConnector PointedConnector { get; private set; }
        public RepairTool PointedTool { get; private set; }
        public Transform DropAnchor { get; private set; }
        public bool PointedTestButton { get; private set; }
        public DriveTestStand DriveStand => activeWorkbench != null ? activeWorkbench.DriveStand : null;
        public DriveTestStand PointedStand { get; private set; }
        public Transform PointedObject { get; private set; }
        public bool IsViewMoving { get; private set; }
        public Camera View => player.View;
        public string Feedback => Time.unscaledTime < feedbackUntil ? feedback : "";
        public float ObservationProgress => inspectionTarget != null || jointInspectionTarget != null
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
            selectSoldering = bench.FindAction("Soldering");
            orbit = bench.FindAction("Orbit", true);
            orbitDelta = bench.FindAction("OrbitDelta", true);
            alignInspection = bench.FindAction("AlignInspection", true);
            // shortcut: mouse-only look for this PC prototype; calibrate sticks when adding gamepad support.
            look.ApplyBindingOverride(new InputBinding { path = "<Gamepad>/rightStick", overridePath = "" });
            if (exitButton != null) exitButton.onClick.AddListener(ExitWorkbench);
            if (restartButton != null) restartButton.onClick.AddListener(ResetRepair);
            if (alignInspectionButton != null) alignInspectionButton.onClick.AddListener(AlignInspection);
        }

        void OnEnable()
        {
            shop.Enable();
            SetCursor(false);
        }

        void Update()
        {
            if (player.IsReturning) return;
            if (saveStatus != null && session != null)
            {
                saveStatus.gameObject.SetActive(!session.IsProgressReady && !IsAtWorkbench && !IsAtOrderDesk && !IsAtEquipmentShop);
                saveStatus.text = session.SaveStatus;
            }
            if (workshopProgress != null && session != null)
            {
                workshopProgress.gameObject.SetActive(!IsAtWorkbench && !IsAtOrderDesk && !IsAtEquipmentShop);
                workshopProgress.text = $"Баланс: {session.Balance} ед.";
            }
            if (IsAtEquipmentShop)
            {
                if (!activeEquipmentShop.IsOpen || cancel.WasPressedThisFrame()) CloseEquipmentShop();
            }
            else if (IsAtOrderDesk)
            {
                if (!activeOrderDesk.IsOpen || cancel.WasPressedThisFrame()) CloseOrderDesk();
            }
            else if (bench.enabled && activeWorkbench == null) ExitWorkbench();
            else if (IsAtWorkbench)
            {
                if (!activeWorkbench.isActiveAndEnabled || !activeWorkbench.IsOccupied)
                    ExitWorkbench();
                else if (cancel.WasPressedThisFrame())
                {
                    if (repairHUD != null && repairHUD.CloseOverlay()) { }
                    else if (DriveStand != null && DriveStand.Part != null) DriveStand.Cancel();
                    else if (IsInspecting && (HeldPart != null || selectedTool != null && selectedTool.IsActive))
                    {
                        CancelPart();
                        if (selectedTool != null) selectedTool.End();
                    }
                    else ExitWorkbench();
                }
                else TickBench();
            }
            else
            {
                player.Tick(move.ReadValue<Vector2>(), look.ReadValue<Vector2>());
                PointedWorkbench = null;
                PointedOrderDesk = null;
                PointedEquipmentShop = null;
                if (!PointerOverUI() && Physics.Raycast(player.View.transform.position, player.View.transform.forward,
                    out RaycastHit hit, reach, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
                {
                    PointedWorkbench = hit.collider.GetComponentInParent<Workbench>();
                    PointedOrderDesk = hit.collider.GetComponentInParent<OrderDesk>();
                    PointedEquipmentShop = hit.collider.GetComponentInParent<EquipmentShop>();
                }

                if (PointedEquipmentShop != null && interact.WasPressedThisFrame() && PointedEquipmentShop.Open(this))
                {
                    activeEquipmentShop = PointedEquipmentShop;
                    shop.Disable();
                    bench.Enable();
                    SetCursor(true);
                }
                else if (PointedOrderDesk != null && interact.WasPressedThisFrame() && PointedOrderDesk.Open(this))
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
                    soldering = activeWorkbench.GetComponentInChildren<SolderingTool>(true);
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
                    string message = IsAtOrderDesk || IsAtEquipmentShop ? ""
                        : Feedback.Length > 0 ? Feedback
                        : PointedEquipmentShop != null ? "E — оборудование мастерской"
                        : PointedOrderDesk != null ? "E — стойка приёма заказов"
                        : PointedWorkbench != null ? PointedWorkbench.HasRobot ? "E — верстак" : "Верстак пуст" : "";
                    if (repairHUD != null) repairHUD.ShowShop(message);
                    else hint.text = message;
                }
            }
            if (exitButton != null) exitButton.gameObject.SetActive(IsAtWorkbench);
            bool showHelpActions = IsAtWorkbench && repairHUD != null && repairHUD.IsHelpOpen;
            if (restartButton != null) restartButton.gameObject.SetActive(showHelpActions);
            if (alignInspectionButton != null) alignInspectionButton.gameObject.SetActive(showHelpActions);
        }

        void TickBench()
        {
            IsViewMoving = false;
            PointedObject = null;
            PointedTestButton = false;
            PointedStand = null;
            if (robot != null && robot.IsTesting)
            {
                IsViewMoving = activeWorkbench.UpdateView(Vector2.zero, 0f);
                ClearObservation();
                return;
            }
            if (DriveStand != null && DriveStand.Part != null)
            {
                IsViewMoving = activeWorkbench.UpdateView(Vector2.zero, 0f);
                ClearObservation();
                PointedObject = DriveStand.transform;
                PointedStand = DriveStand;
                if (!PointerOverUI() && primary.WasPressedThisFrame()
                    && Physics.Raycast(player.View.ScreenPointToRay(point.ReadValue<Vector2>()), out var standHit, RepairReach,
                        ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)
                    && standHit.collider.GetComponentInParent<DriveTestStand>() == DriveStand)
                {
                    if (!DriveStand.RunTest(out string reason)) Say(reason);
                    else Say("");
                }
                return;
            }
            if (selectHand.WasPressedThisFrame())
                SelectTool(null);
            if (selectScrewdriver.WasPressedThisFrame() && screwdriver != null)
                SelectTool(screwdriver);
            if (selectBrush.WasPressedThisFrame() && brush != null)
                SelectTool(brush);
            if (selectSoldering != null && selectSoldering.WasPressedThisFrame())
                SelectToolShortcut(3);
            Vector2 pointer = point.ReadValue<Vector2>();
            PointedScrew = null;
            PointedPart = null;
            PointedConnector = null;
            PointedDirt = null;
            PointedJoint = null;
            bool overUI = PointerOverUI();
            PointedTool = null;
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
            bool canMoveView = !overUI && !primary.IsPressed();
            if (IsInspecting && canMoveView && alignInspection.WasPressedThisFrame()) AlignInspection();
            bool rotating = IsInspecting && canMoveView && orbit.IsPressed();
            bool viewMoving = !primary.IsPressed() && activeWorkbench.UpdateView(
                rotating ? orbitDelta.ReadValue<Vector2>() : Vector2.zero,
                IsInspecting && canMoveView ? scroll.ReadValue<Vector2>().y : 0f);
            IsViewMoving = viewMoving || rotating;
            if (selectedTool != null) selectedTool.FollowPointer(pointer);
            RaycastHit hit = default;
            bool hasHit = !overUI && Physics.Raycast(player.View.ScreenPointToRay(pointer), out hit, RepairReach,
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
                PointedJoint = hit.collider.GetComponent<SolderJoint>();
                PointedTestButton = hit.collider == activeWorkbench.TestButton;
                PointedTool = hit.collider.GetComponentInParent<RepairTool>();
                PointedStand = hit.collider.GetComponentInParent<DriveTestStand>();
            }
            bool canObserve = hasHit && !IsViewMoving && !primary.IsPressed();
            ObserveDrive(canObserve ? PointedPart : null, canObserve ? PointedJoint : null);
            if (rotating || overUI || !hasHit) return;
            if (!primary.WasPressedThisFrame()) return;
            if (PointedTestButton)
            {
                SelectTool(null);
                if (!robot.RunTest(out string reason)) Say(reason);
                else Say("");
            }
            else if (PointedStand != null) Say("Рукой перенесите снятый привод в гнездо стенда, затем нажмите на стенд");
            else if (hit.collider.GetComponentInParent<ScrewdriverTool>() == screwdriver && screwdriver != null)
                SelectTool(screwdriver);
            else if (hit.collider.GetComponentInParent<BrushTool>() == brush && brush != null)
                SelectTool(brush);
            else if (hit.collider.GetComponentInParent<SolderingTool>() == soldering && SolderingAvailable)
                SelectTool(soldering);
            else if (SolderingSelected && PointedJoint != null)
            {
                if (robot.CanSolder(PointedJoint, session, out string reason)) soldering.Begin(PointedJoint, player.View, pointer);
                else Say(reason);
            }
            else if (BrushSelected && hit.collider.GetComponentInParent<RobotRepair>() == robot)
            {
                if (PointedDirt != null && !robot.CanClean(PointedDirt, out string reason)) Say(reason);
                else brush.Begin(robot, player.View, pointer);
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
                    activeWorkbench.ReleaseInspectionPart(HeldPart);
                    dragPlane = new Plane(player.View.transform.forward, HeldPart.transform.position);
                    Ray ray = player.View.ScreenPointToRay(pointer);
                    grabOffset = dragPlane.Raycast(ray, out float distance) ? HeldPart.transform.position - ray.GetPoint(distance) : Vector3.zero;
                    HeldPart.SetDragging(true);
                    Say("");
                }
            }
        }

        void ObserveDrive(RepairPart part, SolderJoint joint)
        {
            if (robot != null && joint != null && !robot.JointFaultFound && !robot.Cover.IsInstalled && !joint.IsRepaired)
            {
                if (jointInspectionTarget != joint) { ClearObservation(); jointInspectionTarget = joint; }
                observationTime += Time.unscaledDeltaTime;
                if (observationTime >= ObservationDuration) robot.InspectJoint(joint);
                return;
            }
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
            jointInspectionTarget = null;
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
            if (!IsAtWorkbench || robot == null || robot.IsTesting || robot.IsOnDriveStand || slot < 0 || slot > 3) return;
            if (slot == 3 && !SolderingAvailable) { Say("Сначала приобретите паяльную станцию"); return; }
            SelectTool(slot == 1 ? screwdriver : slot == 2 ? brush : slot == 3 ? soldering : null);
        }

        public void AlignInspection()
        {
            if (IsInspecting && HeldPart == null && (selectedTool == null || !selectedTool.IsActive))
                activeWorkbench.AlignInspection();
        }

        Transform FindDropAnchor(Ray ray)
        {
            if (DriveStand != null && HeldPart.Kind == RepairPartKind.Drive
                && Physics.Raycast(ray, out RaycastHit standHit, RepairReach, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)
                && standHit.collider.GetComponentInParent<DriveTestStand>() == DriveStand)
                return DriveStand.Socket;
            // The lid and drive sockets overlap; query only the held part's compatible destinations.
            Collider install = HeldPart.InstallationAnchor.GetComponent<Collider>();
            Collider tray = HeldPart.TrayAnchor.GetComponent<Collider>();
            bool hitInstall = install != null && install.Raycast(ray, out _, RepairReach);
            bool hitTray = tray != null && tray.Raycast(ray, out _, RepairReach);
            if (!hitInstall) return hitTray ? HeldPart.TrayAnchor : null;
            if (!hitTray) return HeldPart.InstallationAnchor;
            install.Raycast(ray, out RaycastHit installedHit, RepairReach);
            tray.Raycast(ray, out RaycastHit trayHit, RepairReach);
            return installedHit.distance <= trayHit.distance ? HeldPart.InstallationAnchor : HeldPart.TrayAnchor;
        }

        void FinishPart(Ray ray)
        {
            if (DriveStand != null && DropAnchor == DriveStand.Socket)
            {
                RepairPart part = HeldPart;
                HeldPart = null;
                DropAnchor = null;
                if (!DriveStand.TryLoad(robot, part, out string reason)) { part.RestorePose(); Say(reason); }
                else Say("");
                return;
            }
            if (DropAnchor == null) Say("Перенос отменён: отпустите над посадочным местом или своим лотком");
            else
            {
                bool install = DropAnchor == HeldPart.InstallationAnchor;
                if (!robot.CanPlace(HeldPart, install, out string reason)) Say(reason);
                else if (Physics.Raycast(ray, out RaycastHit obstacle, RepairReach, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)
                    && DropAnchor.GetComponent<Collider>().Raycast(ray, out RaycastHit destination, RepairReach)
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
                if (activeWorkbench != null) activeWorkbench.AlignInspection();
                if (DriveStand != null) DriveStand.Cancel();
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
            PointedStand = null;
            PointedObject = null;
            IsViewMoving = false;
            ClearObservation();
            SelectTool(null);
            if (robot != null) robot.CancelTest();
            screwdriver = null;
            brush = null;
            soldering = null;
            PointedScrew = null;
            PointedPart = null;
            PointedConnector = null;
            PointedDirt = null;
            PointedJoint = null;
            PointedTestButton = false;
            robot = null;
            PointedTool = null;
            Say("");
            if (activeWorkbench != null) activeWorkbench.Exit(!isActiveAndEnabled || !activeWorkbench.isActiveAndEnabled);
            activeWorkbench = null;
            bench.Disable();
            shop.Enable();
            SetCursor(false);
        }

        public void ResetRepair()
        {
            if (!IsAtWorkbench || activeWorkbench == null) return;
            PointedStand = null;
            ClearObservation();
            SelectTool(null);
            RobotRepair replacement = activeWorkbench.ResetRobot();
            if (replacement == null) { Say("Повторение недоступно: отсутствует исходный робот"); return; }
            robot = replacement;
            PointedScrew = null;
            PointedPart = null;
            PointedConnector = null;
            PointedDirt = null;
            PointedJoint = null;
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

        public void CloseEquipmentShop()
        {
            if (activeEquipmentShop != null) activeEquipmentShop.Hide();
            activeEquipmentShop = null;
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
            CloseEquipmentShop();
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
            if (alignInspectionButton != null) alignInspectionButton.onClick.RemoveListener(AlignInspection);
            Destroy(actions);
        }
    }
}
