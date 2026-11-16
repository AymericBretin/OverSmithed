using System.Threading.Tasks;
using OverSmithed.Networking;
using OverSmithed.Players;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// Main menu (Menu scene): play local, host online, join with a code, settings, credits, quit.
    /// Join, Settings and Credits open a panel next to the column; Back (button, Escape or the gamepad's East
    /// button) closes it and selects the button that opened it again. Works with mouse, keyboard and gamepad.
    /// </summary>
    public class MainMenuScreen : MonoBehaviour
    {
        [Header("Main column")]
        [Tooltip("Holds the 6 buttons. Turned off while a panel is open or while connecting.")]
        [SerializeField] private CanvasGroup _column;
        [SerializeField] private Button _playLocalButton;
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _creditsButton;
        [SerializeField] private Button _quitButton;
        [Tooltip("Shows what is happening (connecting...), errors and why we came back to the menu.")]
        [SerializeField] private TMP_Text _statusText;

        [Header("Join panel")]
        [SerializeField] private GameObject _joinPanel;
        [SerializeField] private TMP_InputField _codeField;
        [SerializeField] private Button _joinConfirmButton;
        [SerializeField] private Button _joinBackButton;

        [Header("Settings and credits panels")]
        [SerializeField] private GameObject _settingsPanel;
        [SerializeField] private Button _settingsBackButton;
        [SerializeField] private GameObject _creditsPanel;
        [SerializeField] private Button _creditsBackButton;

        [SerializeField] private KeepSelection _keepSelection;

        private GameObject _openPanel;
        private Button _openedBy;
        private bool _busy;

        private void Awake()
        {
            _playLocalButton.onClick.AddListener(PlayLocal);
            _hostButton.onClick.AddListener(HostOnline);
            _joinButton.onClick.AddListener(() => OpenPanel(_joinPanel, _joinButton, _codeField.gameObject));
            _settingsButton.onClick.AddListener(() => OpenPanel(_settingsPanel, _settingsButton, _settingsBackButton.gameObject));
            _creditsButton.onClick.AddListener(() => OpenPanel(_creditsPanel, _creditsButton, _creditsBackButton.gameObject));
            _quitButton.onClick.AddListener(Quit);

            _joinConfirmButton.onClick.AddListener(JoinWithCode);
            _codeField.onSubmit.AddListener(_ => JoinWithCode());
            _joinBackButton.onClick.AddListener(ClosePanel);
            _settingsBackButton.onClick.AddListener(ClosePanel);
            _creditsBackButton.onClick.AddListener(ClosePanel);

            _joinPanel.SetActive(false);
            _settingsPanel.SetActive(false);
            _creditsPanel.SetActive(false);
            _keepSelection.DefaultSelection = _playLocalButton.gameObject;
        }

        private void OnEnable()
        {
            // Why we are back here, if we did not leave on purpose (host closed the game, connection lost...).
            var sessionService = SessionService.Instance;
            if (sessionService != null)
            {
                _statusText.text = sessionService.ConsumeDisconnectMessage() ?? string.Empty;
                sessionService.Disconnected += ShowStatus;
            }
            RefreshInteractable();
        }

        private void OnDisable()
        {
            if (SessionService.Instance != null)
            {
                SessionService.Instance.Disconnected -= ShowStatus;
            }
        }

        private void Update()
        {
            // Back: Escape / gamepad East (the UI "Cancel" action), while a panel is open and nothing is running.
            if (_openPanel != null && !_busy && CancelPressed())
            {
                ClosePanel();
            }
        }

        private static bool CancelPressed()
        {
            var module = EventSystem.current != null
                ? EventSystem.current.currentInputModule as InputSystemUIInputModule
                : null;
            var cancel = module != null && module.cancel != null ? module.cancel.action : null;
            return cancel != null && cancel.WasPressedThisFrame();
        }

        private void ShowStatus(string message)
        {
            _statusText.text = message ?? string.Empty;
        }

        private void OpenPanel(GameObject panel, Button openedBy, GameObject firstSelected)
        {
            if (_busy)
            {
                return;
            }
            ClosePanelSilently();
            _openPanel = panel;
            _openedBy = openedBy;
            panel.SetActive(true);
            ShowStatus(string.Empty);
            RefreshInteractable();
            _keepSelection.Select(firstSelected);
        }

        private void ClosePanel()
        {
            var openedBy = _openedBy;
            ClosePanelSilently();
            RefreshInteractable();
            if (openedBy != null)
            {
                _keepSelection.Select(openedBy.gameObject);
            }
        }

        private void ClosePanelSilently()
        {
            if (_openPanel != null)
            {
                _openPanel.SetActive(false);
            }
            _openPanel = null;
            _openedBy = null;
        }

        // The column can't be used while a panel is open (the gamepad stays in the panel) or while connecting.
        private void RefreshInteractable()
        {
            _column.interactable = !_busy && _openPanel == null;
            _codeField.interactable = !_busy;
            _joinConfirmButton.interactable = !_busy;
            _joinBackButton.interactable = !_busy;
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;
            ShowStatus(message);
            RefreshInteractable();
        }

        private void PlayLocal()
        {
            SetBusy(true, "Starting...");
            if (!SessionService.Instance.StartLocal())
            {
                SetBusy(false, "Could not start the local game.");
            }
        }

        // Online, each machine usually has one player: the device that clicked joins the lobby automatically.
        private void HostOnline()
        {
            LocalPlayersManager.Instance.QueueAutoJoinFromLastInput();
            _ = RunOnlineAsync("Creating the online game...", SessionService.Instance.HostOnlineAsync(), _hostButton);
        }

        private void JoinWithCode()
        {
            if (_busy)
            {
                return;
            }
            LocalPlayersManager.Instance.QueueAutoJoinFromLastInput();
            _ = RunOnlineAsync("Joining the game...", SessionService.Instance.JoinByCodeAsync(_codeField.text),
                _joinConfirmButton);
        }

        private async Task RunOnlineAsync(string pendingMessage, Task<string> operation, Button selectAfterError)
        {
            SetBusy(true, pendingMessage);
            var error = await operation;
            if (error == null)
            {
                // The network moves us to the lobby and this screen is destroyed.
                return;
            }

            LocalPlayersManager.Instance?.CancelAutoJoin();
            if (this != null)
            {
                SetBusy(false, error);
                _keepSelection.Select(selectAfterError.gameObject);
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
