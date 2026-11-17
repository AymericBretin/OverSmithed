using System.Collections.Generic;
using OverSmithed.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// The settings panel (prefab UI/Prefabs/Screens/SettingsPanel): music and effects volume, display mode and
    /// resolution, a button to the controls panel (<see cref="ControlsScreen"/>), reset to default. Every change is applied and saved right away through <see cref="GameSettings"/>.
    /// The screen that opens it (main menu, later the pause menu) selects <see cref="FirstSelectable"/> and listens to
    /// <see cref="BackButton"/>.
    /// </summary>
    public class SettingsScreen : MonoBehaviour
    {
        // The sliders go from 0 to 20: one step of the keyboard / gamepad = 5 %.
        private const float SliderSteps = 20f;

        [SerializeField] private Slider _musicSlider;
        [SerializeField] private TMP_Text _musicValue;
        [SerializeField] private Slider _effectsSlider;
        [SerializeField] private TMP_Text _effectsValue;
        [SerializeField] private TMP_Dropdown _displayDropdown;
        [SerializeField] private TMP_Dropdown _resolutionDropdown;
        [Tooltip("Opens the controls panel (the screen that opened this panel listens to it).")]
        [SerializeField] private Button _controlsButton;
        [SerializeField] private Button _resetButton;
        [SerializeField] private Button _backButton;

        private readonly List<Vector2Int> _resolutions = new();

        /// <summary>What a gamepad should select when the panel opens.</summary>
        public Selectable FirstSelectable => _musicSlider;

        /// <summary>The Back button: the screen that opened this panel closes it on click.</summary>
        public Button BackButton => _backButton;

        /// <summary>The Controls button: the screen that opened this panel opens the controls panel on click.</summary>
        public Button ControlsButton => _controlsButton;

        /// <summary>True while a dropdown list is open (then Cancel closes the list, not the panel).</summary>
        public bool IsDropdownOpen => _displayDropdown.IsExpanded || _resolutionDropdown.IsExpanded;

        private void Awake()
        {
            foreach (var slider in new[] { _musicSlider, _effectsSlider })
            {
                slider.minValue = 0f;
                slider.maxValue = SliderSteps;
                slider.wholeNumbers = true;
            }
            _displayDropdown.ClearOptions();
            _displayDropdown.AddOptions(new List<string> { "Fullscreen", "Windowed" });

            _musicSlider.onValueChanged.AddListener(value =>
            {
                GameSettings.MusicVolume = value / SliderSteps;
                ShowPercent(_musicValue, value);
            });
            _effectsSlider.onValueChanged.AddListener(value =>
            {
                GameSettings.EffectsVolume = value / SliderSteps;
                ShowPercent(_effectsValue, value);
            });
            _displayDropdown.onValueChanged.AddListener(index => GameSettings.Fullscreen = index == 0);
            _resolutionDropdown.onValueChanged.AddListener(index =>
            {
                if (index >= 0 && index < _resolutions.Count)
                {
                    GameSettings.Resolution = _resolutions[index];
                }
            });
            _resetButton.onClick.AddListener(() =>
            {
                GameSettings.ResetToDefaults();
                Refresh();
            });
        }

        private void OnEnable()
        {
            Refresh();
        }

        /// <summary>Shows the saved settings (without changing them).</summary>
        public void Refresh()
        {
            var music = Mathf.Round(GameSettings.MusicVolume * SliderSteps);
            var effects = Mathf.Round(GameSettings.EffectsVolume * SliderSteps);
            _musicSlider.SetValueWithoutNotify(music);
            _effectsSlider.SetValueWithoutNotify(effects);
            ShowPercent(_musicValue, music);
            ShowPercent(_effectsValue, effects);
            _displayDropdown.SetValueWithoutNotify(GameSettings.Fullscreen ? 0 : 1);

            _resolutions.Clear();
            _resolutions.AddRange(GameSettings.AvailableResolutions());
            var labels = new List<string>();
            foreach (var size in _resolutions)
            {
                labels.Add($"{size.x} × {size.y}");
            }
            _resolutionDropdown.ClearOptions();
            _resolutionDropdown.AddOptions(labels);
            _resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, _resolutions.IndexOf(GameSettings.Resolution)));
        }

        private static void ShowPercent(TMP_Text label, float steps)
        {
            if (label != null)
            {
                label.text = $"{Mathf.RoundToInt(steps / SliderSteps * 100f)}%";
            }
        }
    }
}
