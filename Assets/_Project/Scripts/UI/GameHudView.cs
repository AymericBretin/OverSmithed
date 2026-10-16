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

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _leaveButton = root.Q<Button>("leave-button");
            _leaveButton.focusable = false;
            _leaveButton.clicked += OnLeaveClicked;

            var networkManager = NetworkManager.Singleton;
            var isHost = networkManager != null && networkManager.IsHost;
            _leaveButton.text = isHost ? "Fermer la partie" : "Quitter";

            var mode = SessionService.Instance.Mode == SessionMode.Local ? "Partie locale" : "Partie en ligne";
            var role = isHost ? "hôte" : $"client {networkManager?.LocalClientId}";
            root.Q<Label>("info-label").text = $"{mode} — {role}";
        }

        private void OnDisable()
        {
            _leaveButton.clicked -= OnLeaveClicked;
        }

        private void OnLeaveClicked()
        {
            _leaveButton.SetEnabled(false);
            SessionService.Instance.Leave();
        }
    }
}
