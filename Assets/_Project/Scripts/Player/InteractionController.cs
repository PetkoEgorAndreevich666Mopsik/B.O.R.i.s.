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
        PlayerController player;
        InputActionAsset actions;
        InputActionMap shop;
        InputActionMap bench;
        InputAction move, look, interact, cancel;
        Workbench activeWorkbench;
        readonly List<RaycastResult> uiHits = new();

        public bool IsAtWorkbench => bench != null && bench.enabled;
        public Workbench PointedWorkbench { get; private set; }

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
                    shop.Disable();
                    bench.Enable();
                    SetCursor(true);
                }
            }
            if (hint != null)
                hint.text = IsAtWorkbench ? "ВЕРСТАК  |  ESC — вернуться\nРобот и винт — заготовки для дня 2"
                    : PointedWorkbench != null ? "E — сесть за верстак" : "WASD — движение  |  Мышь — обзор";
            if (exitButton != null) exitButton.gameObject.SetActive(IsAtWorkbench);
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
