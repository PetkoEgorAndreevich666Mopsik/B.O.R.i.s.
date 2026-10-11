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
        InputAction point, primary, selectScrewdriver, selectHand;
        Workbench activeWorkbench;
        ScrewdriverTool screwdriver;
        bool screwdriverSelected;
        readonly List<RaycastResult> uiHits = new();

        public bool IsAtWorkbench => bench != null && bench.enabled;
        public Workbench PointedWorkbench { get; private set; }
        public ScrewFastener PointedScrew { get; private set; }
        public bool ScrewdriverSelected => screwdriverSelected;

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
                    shop.Disable();
                    bench.Enable();
                    SetCursor(true);
                }
            }
            if (hint != null)
            {
                if (IsAtWorkbench && repairHUD != null) repairHUD.Show(screwdriver, screwdriverSelected, PointedScrew);
                else hint.text = PointedWorkbench != null ? "E — сесть за верстак" : "WASD — движение  |  Мышь — обзор";
            }
            if (exitButton != null) exitButton.gameObject.SetActive(IsAtWorkbench);
        }

        void TickBench()
        {
            if (selectHand.WasPressedThisFrame())
            {
                if (screwdriver != null) screwdriver.End();
                screwdriverSelected = false;
            }
            if (selectScrewdriver.WasPressedThisFrame() && screwdriver != null) screwdriverSelected = true;
            Vector2 pointer = point.ReadValue<Vector2>();
            PointedScrew = null;
            bool overUI = PointerOverUI();
            if (screwdriver != null && screwdriver.IsTurning)
            {
                if (!primary.IsPressed() || overUI) screwdriver.End();
                else screwdriver.Drag(pointer);
                return;
            }
            if (overUI) return;
            if (!Physics.Raycast(player.View.ScreenPointToRay(pointer), out RaycastHit hit, reach,
                ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)) return;
            // Restrict tools to this table; the closest collider also blocks hidden screws.
            if (hit.collider.GetComponentInParent<Workbench>() != activeWorkbench) return;
            PointedScrew = hit.collider.GetComponentInParent<ScrewFastener>();
            if (!primary.WasPressedThisFrame()) return;
            if (hit.collider.GetComponentInParent<ScrewdriverTool>() == screwdriver && screwdriver != null)
                screwdriverSelected = true;
            else if (screwdriverSelected && screwdriver != null && PointedScrew != null)
                screwdriver.Begin(PointedScrew, player.View, pointer);
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && screwdriver != null) screwdriver.End();
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
            if (screwdriver != null) screwdriver.End();
            screwdriver = null;
            screwdriverSelected = false;
            PointedScrew = null;
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
