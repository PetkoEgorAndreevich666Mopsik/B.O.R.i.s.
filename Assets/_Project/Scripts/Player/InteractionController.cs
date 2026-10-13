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
        [SerializeField] RepairHUD repairHUD;
        PlayerController player;
        InputActionAsset actions;
        InputActionMap shop;
        InputActionMap bench;
        InputAction move, look, interact, cancel;
        InputAction point, primary, selectScrewdriver, selectHand, selectBrush;
        Workbench activeWorkbench;
        ScrewdriverTool screwdriver;
        BrushTool brush;
        RepairTool selectedTool;
        RobotRepair robot;
        Plane dragPlane;
        Vector3 grabOffset;
        string feedback;
        float feedbackUntil;
        readonly List<RaycastResult> uiHits = new();

        public bool IsAtWorkbench => bench != null && bench.enabled;
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
        public Transform DropAnchor { get; private set; }
        public string Feedback => Time.unscaledTime < feedbackUntil ? feedback : "";

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
            selectScrewdriver = bench.FindAction("Screwdriver", true);
            selectHand = bench.FindAction("Hand", true);
            selectBrush = bench.FindAction("Brush", true);
            // shortcut: mouse-only look for this PC prototype; calibrate sticks when adding gamepad support.
            look.ApplyBindingOverride(new InputBinding { path = "<Gamepad>/rightStick", overridePath = "" });
            if (exitButton != null) exitButton.onClick.AddListener(ExitWorkbench);
        }

        void OnEnable()
        {
            shop.Enable();
            SetCursor(false);
        }

        void Update()
        {
            if (IsAtWorkbench)
            {
                if (activeWorkbench == null || !activeWorkbench.isActiveAndEnabled || !activeWorkbench.IsOccupied || cancel.WasPressedThisFrame())
                    ExitWorkbench();
                else TickBench();
            }
            else
            {
                player.Tick(move.ReadValue<Vector2>(), look.ReadValue<Vector2>());
                PointedWorkbench = null;
                if (!PointerOverUI() && Physics.Raycast(player.View.transform.position, player.View.transform.forward,
                    out RaycastHit hit, reach, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
                    PointedWorkbench = hit.collider.GetComponentInParent<Workbench>();

                if (PointedWorkbench != null && interact.WasPressedThisFrame() && PointedWorkbench.Enter(player.View))
                {
                    activeWorkbench = PointedWorkbench;
                    screwdriver = activeWorkbench.GetComponentInChildren<ScrewdriverTool>();
                    brush = activeWorkbench.GetComponentInChildren<BrushTool>();
                    robot = activeWorkbench.GetComponentInChildren<RobotRepair>();
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
                    if (repairHUD != null) repairHUD.Hide();
                    hint.text = PointedWorkbench != null ? "E — сесть за верстак" : "WASD — движение  |  Мышь — обзор";
                }
            }
            if (exitButton != null) exitButton.gameObject.SetActive(IsAtWorkbench);
        }

        void TickBench()
        {
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
            if (HeldPart != null)
            {
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
                if (!primary.IsPressed() || overUI) selectedTool.End();
                else selectedTool.Drag(pointer);
                return;
            }
            if (overUI) return;
            if (!Physics.Raycast(player.View.ScreenPointToRay(pointer), out RaycastHit hit, reach,
                ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)) return;
            // Restrict tools to this table; the closest collider also blocks hidden screws.
            if (hit.collider.GetComponentInParent<Workbench>() != activeWorkbench) return;
            PointedScrew = hit.collider.GetComponentInParent<ScrewFastener>();
            PointedPart = hit.collider.GetComponentInParent<RepairPart>();
            PointedConnector = hit.collider.GetComponentInParent<RepairConnector>();
            PointedDirt = hit.collider.GetComponent<DirtPatch>();
            if (!primary.WasPressedThisFrame()) return;
            if (hit.collider.GetComponentInParent<ScrewdriverTool>() == screwdriver && screwdriver != null)
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

        void SelectTool(RepairTool tool)
        {
            CancelPart();
            if (selectedTool != null) selectedTool.End();
            selectedTool = tool;
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
            if (HeldPart != null) HeldPart.RestorePose();
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
                if (selectedTool != null) selectedTool.End();
                CancelPart();
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
            SelectTool(null);
            screwdriver = null;
            brush = null;
            PointedScrew = null;
            PointedPart = null;
            PointedConnector = null;
            PointedDirt = null;
            robot = null;
            Say("");
            if (activeWorkbench != null) activeWorkbench.Exit();
            activeWorkbench = null;
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
            ExitWorkbench();
            actions.Disable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnDestroy()
        {
            if (exitButton != null) exitButton.onClick.RemoveListener(ExitWorkbench);
            Destroy(actions);
        }
    }
}
