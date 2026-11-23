using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BORis
{
    public sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] string workshopScene = "WorkshopPrototype";
        [SerializeField] OrderDefinition firstOrder;
        [SerializeField] Button newGameButton;
        [SerializeField] Text status;
        bool starting;
        Vector2 lastPointer;

        void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            lastPointer = Mouse.current?.position.ReadValue() ?? Vector2.zero;
            if (status != null) status.text = "";
        }

        void Update()
        {
            if (Mouse.current == null || newGameButton == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            if (pointer == lastPointer) return;
            lastPointer = pointer;
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(newGameButton.transform.parent))
                events.SetSelectedGameObject(null);
        }

        public void StartNewGame()
        {
            if (starting) return;
            if (firstOrder == null || !firstOrder.IsValid || firstOrder.RequiresSolderingStation
                || !Application.CanStreamedLevelBeLoaded(workshopScene))
            {
                if (status != null) status.text = "Новая игра недоступна: проверьте сцену мастерской и первый заказ.";
                return;
            }
            var store = new WorkshopProgressStore(WorkshopProgressStore.DefaultPath);
            if (!store.TryReset(firstOrder.OrderId, out string reason))
            {
                if (status != null) status.text = reason;
                return;
            }
            starting = true;
            if (newGameButton != null) newGameButton.interactable = false;
            SceneManager.LoadSceneAsync(workshopScene);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
