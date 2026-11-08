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
        InputAction move, look, interact, cancel, carry;
        RobotRepair pointedRobot;
        RobotRepair carryCandidate;
        float carryPressedAt;
        bool carryAttempted;
        ServiceWindow pointedWindow;
        WorkshopDoor pointedDoor;
        VentilationSwitch pointedVentilationSwitch;
        bool pointedSendButton;
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
        Vector2 grabPointer;
        Vector3 heldTarget;
        Quaternion heldRotation;
        float holdHeight;
        bool dragged, rotatingPart;
        const float PlacementTolerance = .5f;
        string feedback;
        float feedbackUntil;
        RepairPart inspectionTarget;
        SolderJoint jointInspectionTarget;
        float observationTime;
        const float ObservationDuration = .5f;
        readonly List<RaycastResult> uiHits = new();
        readonly List<Renderer> socketPreview = new();

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
            carry = shop.AddAction("CarryRobot", InputActionType.Button, "<Mouse>/leftButton");
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
                    else if (HeldPart != null || selectedTool != null && selectedTool.IsActive)
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
                pointedRobot = null;
                pointedWindow = null;
                pointedDoor = null;
                pointedVentilationSwitch = null;
                pointedSendButton = false;
                if (!PointerOverUI() && Physics.Raycast(player.View.transform.position, player.View.transform.forward,
                    out RaycastHit hit, reach, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
                {
                    PointedWorkbench = hit.collider.GetComponentInParent<Workbench>();
                    PointedOrderDesk = hit.collider.GetComponentInParent<OrderDesk>();
                    PointedEquipmentShop = hit.collider.GetComponentInParent<EquipmentShop>();
                    pointedRobot = hit.collider.GetComponentInParent<RobotRepair>();
                    pointedWindow = hit.collider.GetComponentInParent<ServiceWindow>();
                    pointedDoor = hit.collider.GetComponentInParent<WorkshopDoor>();
                    pointedVentilationSwitch = hit.collider.GetComponentInParent<VentilationSwitch>();
                    pointedSendButton = pointedWindow != null && hit.collider == pointedWindow.SendButton;
                }

                RobotTransport transport = session != null ? session.Transport : null;
                bool carrying = transport != null && transport.IsCarrying;
                ServiceWindow window = session != null ? session.Window : null;
                if (carrying)
                {
                    transport.Follow(player.View);
                    Ray placementRay = new(player.View.transform.position, player.View.transform.forward);
                    transport.PreviewDrop(placementRay, reach);
                    if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                    {
                        transport.CancelCarry();
                        Say("Робот возвращён на место");
                    }
                    else if (!carry.IsPressed() && !transport.TryDrop(placementRay, reach))
                        Say("Робот возвращён на место");
                    carryCandidate = null;
                }
                else if (transport != null)
                {
                    if (carry.WasPressedThisFrame())
                    {
                        carryCandidate = pointedRobot != null && pointedRobot == transport.Robot ? pointedRobot : null;
                        carryPressedAt = Time.unscaledTime;
                        carryAttempted = false;
                        if (pointedSendButton)
                        {
                            if (!pointedWindow.TrySend(out string reason)) Say(reason);
                        }
                    }
                    if (carryCandidate != null && carry.IsPressed() && !carryAttempted
                        && Time.unscaledTime - carryPressedAt >= .3f)
                    {
                        carryAttempted = true;
                        if (pointedRobot != carryCandidate) Say("Для подхвата удерживайте ЛКМ на корпусе робота");
                        else
                        {
                            string reason = "";
                            bool pickedUp = window != null && window.IsBusy ? false
                                : window != null && window.IsVisitor(carryCandidate) ? window.TryTakeVisitor(out reason)
                                : transport.TryPickUp(out reason);
                            if (pickedUp) transport.Follow(player.View);
                            else Say(window != null && window.IsBusy ? "Дождитесь остановки ленты и закрытия двери" : reason);
                        }
                    }
                    if (!carry.IsPressed() && carryCandidate != null)
                    {
                        if (!carryAttempted && window != null && (window.IsVisitor(carryCandidate) || transport.IsAtDelivery))
                        {
                            if (!window.Talk(out string reason)) Say(reason);
                        }
                        carryCandidate = null;
                    }
                    carrying = transport.IsCarrying;
                }
                if (carrying) { PointedWorkbench = null; PointedOrderDesk = null; PointedEquipmentShop = null; }
                if (!carrying && pointedDoor != null && interact.WasPressedThisFrame())
                {
                    if (!pointedDoor.TryToggle(GetComponent<CharacterController>()))
                        Say("Освободите место перед дверью");
                }
                else if (!carrying && pointedVentilationSwitch != null && interact.WasPressedThisFrame())
                    pointedVentilationSwitch.Toggle();
                else if (!carrying && pointedWindow != null && interact.WasPressedThisFrame())
                {
                    bool done = pointedSendButton ? pointedWindow.TrySend(out string reason) : pointedWindow.Talk(out reason);
                    if (!done) Say(reason);
                }
                else if (!carrying && PointedEquipmentShop != null && interact.WasPressedThisFrame() && PointedEquipmentShop.Open(this))
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
                    Say("Верстак пуст. Поговорите с роботом у окна приёма и заберите его в ремонт");
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
                        : session != null && session.Transport != null && session.Transport.IsCarrying ? "Отпустите ЛКМ над верстаком или лентой окна · ESC — вернуть"
                        : pointedSendButton ? "ЛКМ / E — отправить робота"
                        : pointedRobot != null && session != null && session.Window != null && session.Window.IsVisitor(pointedRobot)
                            ? session.Window.IsBusy ? "Робот прибывает — дождитесь остановки ленты"
                                : session.Window.HasHeardRequest ? "ЛКМ — продолжить разговор · удерживайте ЛКМ — принять и нести"
                                    : "ЛКМ — поговорить с роботом"
                        : pointedRobot != null && session != null && pointedRobot == session.ActiveRobot
                            ? PointedWorkbench != null && PointedWorkbench.HasRobot ? "Удерживайте ЛКМ — нести · E — верстак" : "Удерживайте ЛКМ — нести"
                        : pointedDoor != null ? pointedDoor.Hint
                        : pointedVentilationSwitch != null ? pointedVentilationSwitch.Hint
                        : pointedWindow != null ? "E — окно обслуживания"
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
            if (PointedScrew != null) PointedScrew.SetHovered(false);
            PointedScrew = null;
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
                    && (standHit.collider.GetComponentInParent<DriveTestStand>() == DriveStand
                        || standHit.collider.GetComponentInParent<RepairPart>() == DriveStand.Part))
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
            if (Keyboard.current?.gKey.wasPressedThisFrame == true) RecoverLooseParts();
            Vector2 pointer = point.ReadValue<Vector2>();
            PointedPart = null;
            PointedConnector = null;
            PointedDirt = null;
            PointedJoint = null;
            bool overUI = PointerOverUI();
            PointedTool = null;
            if (HeldPart != null)
            {
                ClearObservation();
                if (overUI) { CancelPart(); Say("Деталь отпущена"); }
                else
                {
                    Ray ray = player.View.ScreenPointToRay(pointer);
                    bool rotate = orbit.IsPressed();
                    if (rotatingPart && !rotate && dragPlane.Raycast(ray, out float resumedDistance))
                        grabOffset = heldTarget - ray.GetPoint(resumedDistance);
                    rotatingPart = rotate;
                    if (!rotate)
                    {
                        dragged |= Vector2.Distance(pointer, grabPointer) > 12f;
                        float wheel = scroll.ReadValue<Vector2>().y;
                        if (wheel != 0f)
                        {
                            holdHeight = Mathf.Clamp(holdHeight + Mathf.Sign(wheel) * .05f,
                                activeWorkbench.transform.position.y + .15f, activeWorkbench.transform.position.y + 2.5f);
                            dragPlane = new Plane(Vector3.up, new Vector3(0f, holdHeight, 0f));
                        }
                        if (dragPlane.Raycast(ray, out float distance) && distance <= RepairReach * 1.5f)
                            heldTarget = ray.GetPoint(distance) + grabOffset;
                    }
                    else
                    {
                        Vector2 delta = orbitDelta.ReadValue<Vector2>();
                        heldRotation = Quaternion.AngleAxis(-delta.x * .35f, Vector3.up)
                            * Quaternion.AngleAxis(delta.y * .35f, player.View.transform.right) * heldRotation;
                    }
                    Transform candidate = dragged && !rotate ? FindDropAnchor(ray) : null;
                    if (candidate != null && CanPlaceHeld(candidate))
                    {
                        heldTarget = candidate == HeldPart.InstallationAnchor
                            ? candidate.position - candidate.forward * HeldPart.ExtractionDistance + Vector3.up * .18f
                            : candidate.position;
                        heldRotation = candidate.rotation;
                    }
                    HeldPart.MoveHeld(heldTarget, heldRotation);
                    SetDropAnchor(candidate != null && CloseToAnchor(candidate) ? candidate : null);
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
            if (canObserve && ScrewdriverSelected && PointedScrew != null && PointedScrew.IsRemoved
                && robot != null && robot.CanInsertScrew(PointedScrew, out _))
                PointedScrew.SetHovered(true);
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
                if (PointedScrew.IsRemoved)
                {
                    if (robot.TryInsertScrew(PointedScrew, out string reason)) Say("");
                    else Say(reason);
                }
                else if (robot.CanTurn(PointedScrew, out string reason))
                {
                    screwdriver.Begin(PointedScrew, player.View, pointer);
                    Say("");
                }
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
                    activeWorkbench.ReleaseInspectionPart(PointedPart);
                    if (PointedPart.IsInstalled && !robot.TryPlace(PointedPart, false, out reason))
                    { Say(reason); return; }
                    HeldPart = PointedPart;
                    grabPointer = pointer;
                    dragged = false;
                    rotatingPart = false;
                    heldTarget = HeldPart.transform.position;
                    heldRotation = HeldPart.transform.rotation;
                    holdHeight = heldTarget.y;
                    dragPlane = new Plane(Vector3.up, heldTarget);
                    Ray ray = player.View.ScreenPointToRay(pointer);
                    grabOffset = dragPlane.Raycast(ray, out float distance) ? HeldPart.transform.position - ray.GetPoint(distance) : Vector3.zero;
                    HeldPart.SetDragging(true);
                    HeldPart.MoveHeld(heldTarget, heldRotation);
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
            if (PointedScrew != null) PointedScrew.SetHovered(false);
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
            Transform target = CaptureAnchor(HeldPart.InstallationAnchor, ray, out float installDistance)
                ? HeldPart.InstallationAnchor : null;
            if (DriveStand != null && HeldPart.Kind == RepairPartKind.Drive
                && CaptureAnchor(DriveStand.Socket, ray, out float standDistance)
                && (target == null || standDistance < installDistance)) target = DriveStand.Socket;
            return target;
        }

        bool CaptureAnchor(Transform anchor, Ray ray, out float distance)
        {
            distance = 0f;
            if (anchor == null) return false;
            Collider collider = anchor.GetComponent<Collider>();
            Bounds bounds = collider != null ? collider.bounds : new Bounds(anchor.position, Vector3.one * .18f);
            bounds.Expand(.12f);
            return bounds.IntersectRay(ray, out distance) && distance <= RepairReach && !DropIsBlocked(anchor, ray);
        }

        bool RaycastWithoutHeld(Ray ray, out RaycastHit nearest)
        {
            nearest = default;
            bool found = false;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, RepairReach, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
            {
                if (HeldPart != null && hit.collider.GetComponentInParent<RepairPart>() == HeldPart) continue;
                if (!found || hit.distance < nearest.distance) { nearest = hit; found = true; }
            }
            return found;
        }

        bool DropIsBlocked(Transform anchor, Ray ray)
        {
            if (!RaycastWithoutHeld(ray, out RaycastHit obstacle)) return false;
            bool targetHit = obstacle.collider.transform == anchor || obstacle.collider.transform.IsChildOf(anchor)
                || DriveStand != null && anchor == DriveStand.Socket
                    && obstacle.collider.GetComponentInParent<DriveTestStand>() == DriveStand;
            return !targetHit && obstacle.distance < Vector3.Dot(anchor.position - ray.origin, ray.direction) - .12f;
        }

        bool CloseToAnchor(Transform anchor) => HeldPart != null
            && Vector3.Distance(HeldPart.Body.position, anchor.position) <= PlacementTolerance;

        bool CanPlaceHeld(Transform anchor) => DriveStand != null && anchor == DriveStand.Socket
            ? DriveStand.CanLoad(robot, HeldPart, out _) : robot.CanPlace(HeldPart, true, out _);

        void SetDropAnchor(Transform anchor)
        {
            if (DropAnchor == anchor) return;
            foreach (Renderer renderer in socketPreview) if (renderer != null) renderer.enabled = false;
            socketPreview.Clear();
            DropAnchor = anchor;
            if (anchor == null || HeldPart == null) return;
            if (!CanPlaceHeld(anchor)) return;
            foreach (Renderer renderer in anchor.GetComponentsInChildren<Renderer>())
                if (!renderer.enabled && renderer.name.EndsWith("Rim"))
                { renderer.enabled = true; socketPreview.Add(renderer); }
        }

        void FinishPart(Ray ray)
        {
            if (DropAnchor != null && DropIsBlocked(DropAnchor, ray))
            {
                CancelPart(); Say("Посадочное место перекрыто предметом"); return;
            }
            if (DropAnchor != null && !CloseToAnchor(DropAnchor)) SetDropAnchor(null);
            if (DriveStand != null && DropAnchor == DriveStand.Socket)
            {
                RepairPart part = HeldPart;
                SetDropAnchor(null);
                HeldPart = null;
                if (!DriveStand.TryLoad(robot, part, out string reason)) { part.RestorePose(); Say(reason); }
                else Say("");
                return;
            }
            if (DropAnchor == null) Say("Деталь отпущена");
            else
            {
                if (robot.TryPlace(HeldPart, true, out string reason)) Say("Деталь установлена");
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
            SetDropAnchor(null);
        }

        public void RecoverLooseParts()
        {
            if (!IsAtWorkbench || robot == null || robot.IsTesting || robot.IsOnDriveStand) return;
            if (selectedTool != null) selectedTool.End();
            ClearObservation();
            CancelPart();
            activeWorkbench.ReturnToOverview(true);
            foreach (RepairPart part in robot.GetComponentsInChildren<RepairPart>())
                if (!part.IsInstalled) part.RecoverToTable();
            activeWorkbench.Focus(null);
            Say("Снятые детали возвращены на стол");
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
                carryCandidate = null;
                if (PointedScrew != null) PointedScrew.SetHovered(false);
                if (session != null && session.Transport != null) session.Transport.CancelCarry();
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
            if (session != null && session.Transport != null) session.Transport.CancelCarry();
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
