using System.Threading.Tasks;
using OverSmithed.Networking;
using UnityEngine;
using UnityEngine.UIElements;

namespace OverSmithed.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuView : MonoBehaviour
    {
        private Button _localButton;
        private Button _hostButton;
        private Button _joinButton;
        private TextField _codeField;
        private Label _statusLabel;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _localButton = root.Q<Button>("local-button");
            _hostButton = root.Q<Button>("host-button");
            _joinButton = root.Q<Button>("join-button");
            _codeField = root.Q<TextField>("code-field");
            _statusLabel = root.Q<Label>("status-label");

            _localButton.clicked += OnLocalClicked;
            _hostButton.clicked += OnHostClicked;
            _joinButton.clicked += OnJoinClicked;

            // Why we are back here, if we did not leave on purpose (host closed the game, connection lost...).
            var sessionService = SessionService.Instance;
            _statusLabel.text = sessionService.ConsumeDisconnectMessage() ?? string.Empty;
            sessionService.Disconnected += ShowDisconnection;
            _localButton.Focus();
        }

        private void OnDisable()
        {
            _localButton.clicked -= OnLocalClicked;
            _hostButton.clicked -= OnHostClicked;
            _joinButton.clicked -= OnJoinClicked;
            if (SessionService.Instance != null)
            {
                SessionService.Instance.Disconnected -= ShowDisconnection;
            }
        }

        private void ShowDisconnection(string message)
        {
            _statusLabel.text = message;
        }

        private void OnLocalClicked()
        {
            SetInteractable(false);
            if (SessionService.Instance.StartLocal())
            {
                _statusLabel.text = "Démarrage…";
            }
            else
            {
                _statusLabel.text = "Impossible de démarrer la partie locale.";
                SetInteractable(true);
            }
        }

        private void OnHostClicked()
        {
            _ = RunOnlineAsync("Création de la partie en ligne…", SessionService.Instance.HostOnlineAsync());
        }

        private void OnJoinClicked()
        {
            _ = RunOnlineAsync("Connexion à la partie…", SessionService.Instance.JoinByCodeAsync(_codeField.value));
        }

        private async Task RunOnlineAsync(string pendingMessage, Task<string> operation)
        {
            SetInteractable(false);
            _statusLabel.text = pendingMessage;

            var error = await operation;

            // On success the network moves us to the lobby and this view is destroyed.
            if (error != null && this != null)
            {
                _statusLabel.text = error;
                SetInteractable(true);
            }
        }

        private void SetInteractable(bool interactable)
        {
            _localButton.SetEnabled(interactable);
            _hostButton.SetEnabled(interactable);
            _joinButton.SetEnabled(interactable);
            _codeField.SetEnabled(interactable);
        }
    }
}
