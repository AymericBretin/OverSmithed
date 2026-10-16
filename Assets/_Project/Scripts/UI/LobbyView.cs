using OverSmithed.Networking;
using OverSmithed.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace OverSmithed.UI
{
    /// <summary>
    /// Lobby screen: lists every player of the session (local and remote) and opens local joining.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LobbyView : MonoBehaviour
    {
        private Button _leaveButton;
        private VisualElement _playerList;
        private Label _messageLabel;
        private SessionRoster _roster;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _leaveButton = root.Q<Button>("leave-button");
            _playerList = root.Q("player-list");
            _messageLabel = root.Q<Label>("message-label");

            // Mouse only: Enter / gamepad A are join buttons and must not trigger a focused button.
            _leaveButton.focusable = false;
            _leaveButton.clicked += OnLeaveClicked;

            var isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            _leaveButton.text = isHost ? "Fermer la partie" : "Quitter";
            root.Q<Label>("mode-label").text = SessionService.Instance.Mode == SessionMode.Local
                ? "Partie locale"
                : "Partie en ligne";
            _messageLabel.text = string.Empty;

            var localPlayers = LocalPlayersManager.Instance;
            localPlayers.SlotsChanged += Refresh;
            localPlayers.Notification += ShowMessage;
            localPlayers.SetJoiningEnabled(true);

            SessionRoster.InstanceChanged += BindRoster;
            BindRoster(SessionRoster.Instance);
        }

        private void OnDisable()
        {
            _leaveButton.clicked -= OnLeaveClicked;

            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers != null)
            {
                localPlayers.SlotsChanged -= Refresh;
                localPlayers.Notification -= ShowMessage;
                localPlayers.SetJoiningEnabled(false);
            }

            SessionRoster.InstanceChanged -= BindRoster;
            BindRoster(null);
        }

        private void BindRoster(SessionRoster roster)
        {
            if (_roster != null)
            {
                _roster.Changed -= Refresh;
            }
            _roster = roster;
            if (_roster != null)
            {
                _roster.Changed += Refresh;
            }
            Refresh();
        }

        private void Refresh()
        {
            _playerList.Clear();
            var localClientId = NetworkManager.Singleton.LocalClientId;

            for (byte number = 1; number <= NetworkConstants.MaxPlayers; number++)
            {
                var row = new Label { text = $"Joueur {number} — libre" };
                row.AddToClassList("player-row");

                if (_roster != null && TryFindByNumber(number, out var entry))
                {
                    var origin = entry.ClientId == NetworkManager.ServerClientId ? "hôte" : $"client {entry.ClientId}";
                    var isLocal = entry.ClientId == localClientId;
                    row.text = $"Joueur {number} — {entry.DeviceLabel} — {origin}{(isLocal ? " (cette machine)" : string.Empty)}";
                    row.AddToClassList(isLocal ? "player-row--local" : "player-row--remote");
                }
                _playerList.Add(row);
            }
        }

        private bool TryFindByNumber(byte number, out RosterEntry entry)
        {
            for (var i = 0; i < _roster.Count; i++)
            {
                if (_roster[i].PlayerNumber == number)
                {
                    entry = _roster[i];
                    return true;
                }
            }
            entry = default;
            return false;
        }

        private void ShowMessage(string message)
        {
            _messageLabel.text = message;
        }

        private void OnLeaveClicked()
        {
            _leaveButton.SetEnabled(false);
            SessionService.Instance.Leave();
        }
    }
}
