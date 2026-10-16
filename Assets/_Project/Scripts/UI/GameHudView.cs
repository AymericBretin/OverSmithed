using OverSmithed.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace OverSmithed.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class GameHudView : MonoBehaviour
    {
        private Button _leaveButton;
        private Label _messageLabel;
        private SessionRoster _roster;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _leaveButton = root.Q<Button>("leave-button");
            _messageLabel = root.Q<Label>("message-label");
            _leaveButton.focusable = false;
            _leaveButton.clicked += OnLeaveClicked;
            _messageLabel.text = string.Empty;

            var networkManager = NetworkManager.Singleton;
            var isHost = networkManager != null && networkManager.IsHost;
            _leaveButton.text = isHost ? "Fermer la partie" : "Quitter";

            var mode = SessionService.Instance.Mode == SessionMode.Local ? "Partie locale" : "Partie en ligne";
            var role = isHost ? "hôte" : $"client {networkManager?.LocalClientId}";
            root.Q<Label>("info-label").text = $"{mode} — {role}";

            _roster = SessionRoster.Instance;
            if (_roster != null)
            {
                _roster.PlayerRemoved += AnnounceDeparture;
            }
        }

        private void OnDisable()
        {
            _leaveButton.clicked -= OnLeaveClicked;
            if (_roster != null)
            {
                _roster.PlayerRemoved -= AnnounceDeparture;
                _roster = null;
            }
        }

        private void AnnounceDeparture(RosterEntry entry)
        {
            _messageLabel.text = $"Joueur {entry.PlayerNumber} a quitté la partie.";
        }

        private void OnLeaveClicked()
        {
            _leaveButton.SetEnabled(false);
            SessionService.Instance.Leave();
        }
    }
}
