using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BORis
{
    public sealed class SettingsMenu : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] GameObject menu;
        [SerializeField] Dropdown resolution;
        [SerializeField] Dropdown screenMode;
        [SerializeField] Slider volume;
        [SerializeField] Text volumeLabel;
        readonly List<Vector2Int> resolutions = new();
        const string VolumeKey = "BORis.Audio.Volume";
        const string WidthKey = "BORis.Display.Width";
        const string HeightKey = "BORis.Display.Height";
        const string ModeKey = "BORis.Display.Mode";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LoadSettings()
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            if (!PlayerPrefs.HasKey(WidthKey)) return;
            int width = PlayerPrefs.GetInt(WidthKey);
            int height = PlayerPrefs.GetInt(HeightKey);
            foreach (Resolution supported in Screen.resolutions)
                if (supported.width == width && supported.height == height)
                {
                    Screen.SetResolution(width, height, Mode(PlayerPrefs.GetInt(ModeKey, 0)));
                    break;
                }
        }

        void Awake()
        {
            panel.SetActive(false);
            volume.onValueChanged.AddListener(PreviewVolume);
        }

        public void Open()
        {
            resolutions.Clear();
            foreach (Resolution supported in Screen.resolutions)
            {
                var size = new Vector2Int(supported.width, supported.height);
                if (!resolutions.Contains(size)) resolutions.Add(size);
            }
            var current = new Vector2Int(Screen.width, Screen.height);
            if (!resolutions.Contains(current)) resolutions.Add(current);
            var options = new List<string>();
            foreach (var size in resolutions) options.Add($"{size.x} × {size.y}");
            resolution.ClearOptions();
            resolution.AddOptions(options);
            resolution.value = resolutions.IndexOf(current);
            screenMode.value = Screen.fullScreenMode == FullScreenMode.Windowed ? 2
                : Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? 1 : 0;
            volume.SetValueWithoutNotify(AudioListener.volume);
            PreviewVolume(volume.value);
            menu.SetActive(false);
            panel.SetActive(true);
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(resolution.gameObject);
        }

        public void Apply()
        {
            if (resolution.value < 0 || resolution.value >= resolutions.Count) return;
            Vector2Int size = resolutions[resolution.value];
            Screen.SetResolution(size.x, size.y, Mode(screenMode.value));
            PlayerPrefs.SetInt(WidthKey, size.x);
            PlayerPrefs.SetInt(HeightKey, size.y);
            PlayerPrefs.SetInt(ModeKey, screenMode.value);
            PlayerPrefs.SetFloat(VolumeKey, volume.value);
            PlayerPrefs.Save();
            Close();
        }

        public void Close()
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            panel.SetActive(false);
            menu.SetActive(true);
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        void PreviewVolume(float value)
        {
            AudioListener.volume = value;
            volumeLabel.text = $"{value:P0}";
        }

        void Update()
        {
            if (panel.activeSelf && Keyboard.current?.escapeKey.wasPressedThisFrame == true) Close();
        }

        static FullScreenMode Mode(int value) => value == 1 ? FullScreenMode.ExclusiveFullScreen
            : value == 2 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;

        void OnDestroy() => volume.onValueChanged.RemoveListener(PreviewVolume);
    }
}
