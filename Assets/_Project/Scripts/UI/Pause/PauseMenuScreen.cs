using System.Collections.Generic;
using OverSmithed.Core;
using OverSmithed.Networking;
using OverSmithed.Players;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// Pause menu of a level (prefab UI/Prefabs/Screens/PauseMenu, under the level's HUD canvas; the scene needs a
    /// UIEventSystem). Any player of this machine opens it with Pause (Escape, gamepad Start). Buttons: Resume,
    /// Settings, Controls, Back to map (host only, hidden on clients), Quit to menu (asks first).
    /// A local game is paused (time stops); online the game keeps running for the others and only this machine's
    /// players stop (<see cref="GamePause"/>). Every device of this machine can use the menu.
    /// Pause / Cancel (Escape, Start, gamepad East) go back one step: sub-panel → list → closed.
    /// </summary>
    public class PauseMenuScreen : MonoBehaviour
    {
        [Tooltip("Everything shown while the menu is open (shade + panels). Hidden when closed.")]
        [SerializeField] private GameObject _content;
        [SerializeField] private KeepSelection _keepSelection;

        [Header("Pause list")]
        [SerializeField] private GameObject _listPanel;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _controlsButton;
        [SerializeField] private Button _backToMapButton;
        [SerializeField] private Button _quitButton;
        [Tooltip("Shown in online games: the game does not stop.")]
        [SerializeField] private GameObject _onlineNote;

        [Header("Panels")]
        [Tooltip("The SettingsPanel prefab (same as the main menu).")]
        [SerializeField] private SettingsScreen _settingsScreen;
        [Tooltip("The ControlsPanel prefab (same as the main menu).")]
        [SerializeField] private ControlsScreen _controlsScreen;

        [Header("Quit confirmation")]
        [SerializeField] private GameObject _confirmPanel;
        [SerializeField] private TMP_Text _confirmText;
        [SerializeField] private Button _confirmQuitButton;
        [SerializeField] private Button _confirmCancelButton;

        private const string PauseAction = "Pause";

        private readonly Dictionary<LocalPlayerSlot, InputAction> _pauseActions = new();
        private GameObject _openPanel;
        // The controls panel goes back to where it was opened from: the pause list or the settings panel.
        private bool _controlsFromSettings;
        private int _openedFrame = -1;
        private bool _leaving;

        private void Awake()
        {
            _resumeButton.onClick.AddListener(Close);
            _settingsButton.onClick.AddListener(() =>
                OpenPanel(_settingsScreen.gameObject, _settingsScreen.FirstSelectable.gameObject));
            _controlsButton.onClick.AddListener(() => OpenControls(fromSettings: false));
            _backToMapButton.onClick.AddListener(BackToMap);
            _quitButton.onClick.AddListener(() => OpenPanel(_confirmPanel, _confirmCancelButton.gameObject));

            _settingsScreen.BackButton.onClick.AddListener(() => ShowList(_settingsButton));
            _settingsScreen.ControlsButton.onClick.AddListener(() => OpenControls(fromSettings: true));
            _controlsScreen.BackButton.onClick.AddListener(BackFromControls);
            _confirmQuitButton.onClick.AddListener(Quit);
            _confirmCancelButton.onClick.AddListener(() => ShowList(_quitButton));

            _content.SetActive(false);
        }

        private void OnDestroy()
        {
            // The level is unloaded (back to the map, back to the menu, connection lost): time must run again.
            if (_content != null && _content.activeSelf)
            {
                GamePause.Close();
            }
        }

        private void Update()
        {
            var pausePressed = AnyPausePressed();
            if (!_content.activeSelf)
            {
                if (pausePressed && CanOpen())
                {
                    Open();
                }
                return;
            }

            // The same press that opened the menu, a running leave, an open dropdown list or the controls panel waiting
            // for a key: Escape / Start / B belong to them.
            if (Time.frameCount == _openedFrame || _leaving || _settingsScreen.IsDropdownOpen ||
                _controlsScreen.IsListening)
            {
                return;
            }
            if (pausePressed || CancelPressed())
            {
                Back();
            }
        }

        // ---------- Open / close ----------

        private static bool CanOpen()
        {
            var networkManager = NetworkManager.Singleton;
            return networkManager != null && networkManager.IsListening && !networkManager.ShutdownInProgress;
        }

        private void Open()
        {
            _openedFrame = Time.frameCount;
            _leaving = false;
            var isLocal = SessionService.Instance == null || SessionService.Instance.Mode == SessionMode.Local;
            GamePause.Open(stopTime: isLocal);

            var isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            _backToMapButton.gameObject.SetActive(isHost);
            _onlineNote.SetActive(!isLocal);
            _confirmText.text = isLocal
                ? "The progress of this level will be lost."
                : isHost
                    ? "This ends the game for every player."
                    : "You will leave this online game.";
            SetButtonsInteractable(true);
            BuildListNavigation();

            _content.SetActive(true);
            ShowList(_resumeButton);
        }

        private void Close()
        {
            if (_leaving)
            {
                return;
            }
            HidePanels();
            _content.SetActive(false);
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            GamePause.Close();
        }

        // One step back: controls → where it came from, settings / confirmation → list, list → resume.
        private void Back()
        {
            if (_openPanel == _controlsScreen.gameObject)
            {
                BackFromControls();
            }
            else if (_openPanel == _settingsScreen.gameObject)
            {
                ShowList(_settingsButton);
            }
            else if (_openPanel == _confirmPanel)
            {
                ShowList(_quitButton);
            }
            else
            {
                Close();
            }
        }

        // ---------- Panels ----------

        private void ShowList(Button select)
        {
            HidePanels();
            _listPanel.SetActive(true);
            _keepSelection.DefaultSelection = _resumeButton.gameObject;
            _keepSelection.Select(select.gameObject);
        }

        private void OpenPanel(GameObject panel, GameObject firstSelected)
        {
            if (_leaving)
            {
                return;
            }
            HidePanels();
            _listPanel.SetActive(false);
            _openPanel = panel;
            panel.SetActive(true);
            _keepSelection.DefaultSelection = firstSelected;
            _keepSelection.Select(firstSelected);
        }

        private void OpenControls(bool fromSettings)
        {
            _controlsFromSettings = fromSettings;
            OpenPanel(_controlsScreen.gameObject, _controlsScreen.FirstSelectable.gameObject);
        }

        private void BackFromControls()
        {
            if (_controlsFromSettings)
            {
                OpenPanel(_settingsScreen.gameObject, _settingsScreen.ControlsButton.gameObject);
            }
            else
            {
                ShowList(_controlsButton);
            }
        }

        private void HidePanels()
        {
            _settingsScreen.gameObject.SetActive(false);
            _controlsScreen.gameObject.SetActive(false);
            _confirmPanel.SetActive(false);
            _openPanel = null;
        }

        // Up / down through the visible buttons, wrapping around (Back to map is hidden on clients).
        private void BuildListNavigation()
        {
            var buttons = new List<Button>();
            foreach (var button in new[] { _resumeButton, _settingsButton, _controlsButton, _backToMapButton, _quitButton })
            {
                if (button.gameObject.activeSelf)
                {
                    buttons.Add(button);
                }
            }
            for (var i = 0; i < buttons.Count; i++)
            {
                buttons[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = buttons[(i + buttons.Count - 1) % buttons.Count],
                    selectOnDown = buttons[(i + 1) % buttons.Count]
                };
            }
        }

        // ---------- Leaving ----------

        private void BackToMap()
        {
            if (_leaving)
            {
                return;
            }
            // A scene must not load with time stopped. The menu stays open (players stay still) until the level unloads.
            _leaving = true;
            SetButtonsInteractable(false);
            GamePause.SetTimeStopped(false);
            if (!SceneFlow.GoToWorldMap())
            {
                _leaving = false;
                SetButtonsInteractable(true);
                GamePause.SetTimeStopped(SessionService.Instance == null ||
                                         SessionService.Instance.Mode == SessionMode.Local);
                _keepSelection.Select(_backToMapButton.gameObject);
            }
        }

        private void Quit()
        {
            if (_leaving)
            {
                return;
            }
            _leaving = true;
            SetButtonsInteractable(false);
            GamePause.SetTimeStopped(false);
            SessionService.Instance.Leave();
        }

        private void SetButtonsInteractable(bool interactable)
        {
            foreach (var button in new[]
                     {
                         _resumeButton, _settingsButton, _controlsButton, _backToMapButton, _quitButton,
                         _confirmQuitButton, _confirmCancelButton
                     })
            {
                button.interactable = interactable;
            }
        }

        // ---------- Input ----------

        // Pause (Escape / Start) of any joined player of this machine.
        private bool AnyPausePressed()
        {
            var manager = LocalPlayersManager.Instance;
            if (manager == null)
            {
                return false;
            }
            var pressed = false;
            foreach (var slot in manager.Slots)
            {
                if (slot == null || slot.State != LocalPlayerSlot.SlotState.Joined)
                {
                    continue;
                }
                if (!_pauseActions.TryGetValue(slot, out var action))
                {
                    action = slot.Input.actions.FindAction(PauseAction);
                    _pauseActions[slot] = action;
                }
                // Read every slot (no early exit): each action's press must be used up this frame.
                pressed |= action != null && action.WasPressedThisFrame();
            }
            return pressed;
        }

        // The UI module's Cancel action (Escape, gamepad East), from every device.
        private static bool CancelPressed()
        {
            var module = EventSystem.current != null
                ? EventSystem.current.currentInputModule as InputSystemUIInputModule
                : null;
            var cancel = module != null && module.cancel != null ? module.cancel.action : null;
            return cancel != null && cancel.WasPressedThisFrame();
        }
    }
}
