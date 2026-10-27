using UnityEngine;
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

        void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (status != null) status.text = "";
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
