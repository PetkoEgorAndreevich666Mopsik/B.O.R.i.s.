using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BORis
{
    [RequireComponent(typeof(WorkshopSession))]
    public sealed class WorkshopDebugPanel : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        WorkshopSession session;
        InteractionController interaction;
        GameObject panel;
        UnityEngine.UI.Text status;
        bool restoreInteraction, switching;
        CursorLockMode previousCursor;
        bool previousCursorVisible;

        public bool IsOpen => panel != null && panel.activeSelf;
        public string Status => status != null ? status.text : "";

        void Awake()
        {
            session = GetComponent<WorkshopSession>();
            interaction = FindFirstObjectByType<InteractionController>();
            BuildUI();
        }

        void Update()
        {
            if (!switching && Keyboard.current?.f8Key.wasPressedThisFrame == true) SetOpen(!IsOpen);
            if (IsOpen && Keyboard.current?.escapeKey.wasPressedThisFrame == true) SetOpen(false);
        }

        public void SetOpen(bool open)
        {
            if (panel == null || open == IsOpen || switching) return;
            if (open)
            {
                previousCursor = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                restoreInteraction = interaction != null && interaction.enabled;
                if (restoreInteraction) interaction.enabled = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                status.text = "Переход заменяет текущее сохранение.\nВыбранный заказ ещё не принят.";
            }
            else if (restoreInteraction && interaction != null) interaction.enabled = true;
            else
            {
                Cursor.lockState = previousCursor;
                Cursor.visible = previousCursorVisible;
            }
            panel.SetActive(open);
        }

        public bool JumpToOrder(int orderIndex)
        {
            if (!CanReload()) return false;
            if (!session.TrySaveDebugCheckpoint(orderIndex, out string reason))
            {
                status.text = reason;
                return false;
            }
            Reload();
            return true;
        }

        public bool RestartOrder()
        {
            if (!CanReload()) return false;
            Reload();
            return true;
        }

        bool CanReload()
        {
            if (switching) return false;
            if (Application.CanStreamedLevelBeLoaded(gameObject.scene.path)) return true;
            status.text = "Сцена мастерской недоступна для загрузки";
            return false;
        }

        void Reload()
        {
            switching = true;
            SceneManager.LoadSceneAsync(gameObject.scene.path);
        }

        void BuildUI()
        {
            var canvasObject = new GameObject("WorkshopDeveloperCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var launcher = Button(canvasObject.transform, "DeveloperToggle", "DEV · F8", new Vector2(0, -28), new Vector2(150, 36), font);
            launcher.GetComponent<RectTransform>().anchorMin = launcher.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, 1f);
            launcher.onClick.AddListener(() => SetOpen(!IsOpen));
            panel = Image(canvasObject.transform, "DeveloperPanel", new Color(0f, 0f, 0f, .72f), Vector2.zero, Vector2.zero);
            var backdrop = panel.GetComponent<RectTransform>();
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            var card = Image(panel.transform, "DeveloperCard", new Color(.045f, .085f, .09f, 1f), Vector2.zero, new Vector2(560, 620));
            Label(card.transform, "DeveloperTitle", "СОСТОЯНИЯ ИГРЫ", new Vector2(0, 264), new Vector2(500, 45), 28, font);
            status = Label(card.transform, "DeveloperStatus", "", new Vector2(0, 205), new Vector2(500, 62), 19, font);
            string[] captions =
            {
                "Новая игра · заказ 1 / A", "Заказ 2 / B · замена привода", "Заказ 3 / C · пайка",
                "Заказ 4 / D · диагностика привода", "Заказ 5 / E · диагностика контакта"
            };
            for (int i = 0; i < captions.Length; i++)
            {
                int index = i;
                Button(card.transform, "DeveloperOrder" + (i + 1), captions[i], new Vector2(0, 128 - i * 54), new Vector2(480, 44), font)
                    .onClick.AddListener(() => JumpToOrder(index));
            }
            Button(card.transform, "DeveloperRestart", "Перезапустить текущий ремонт", new Vector2(0, -154), new Vector2(480, 44), font)
                .onClick.AddListener(() => RestartOrder());
            Button(card.transform, "DeveloperClose", "Закрыть · F8 / ESC", new Vector2(0, -248), new Vector2(480, 42), font)
                .onClick.AddListener(() => SetOpen(false));
            panel.SetActive(false);
        }

        static GameObject Image(Transform parent, string name, Color color, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            obj.GetComponent<UnityEngine.UI.Image>().color = color;
            return obj;
        }

        static UnityEngine.UI.Text Label(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize, Font font)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = obj.GetComponent<UnityEngine.UI.Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.text = value;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        static UnityEngine.UI.Button Button(Transform parent, string name, string caption, Vector2 position, Vector2 size, Font font)
        {
            GameObject obj = Image(parent, name, new Color(.12f, .26f, .28f), position, size);
            var button = obj.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = obj.GetComponent<UnityEngine.UI.Image>();
            var colors = button.colors;
            colors.highlightedColor = new Color(.65f, 1f, .94f);
            colors.pressedColor = new Color(.35f, .8f, .72f);
            button.colors = colors;
            Label(obj.transform, name + "Label", caption, Vector2.zero, size - new Vector2(16, 4), 21, font);
            return button;
        }

        void OnDisable()
        {
            if (IsOpen)
            {
                switching = false;
                SetOpen(false);
            }
        }
#endif
    }
}
