using OverSmithed.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace OverSmithed.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class LobbyView : MonoBehaviour
    {
        private Button _leaveButton;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _leaveButton = root.Q<Button>("leave-button");
            _leaveButton.clicked += OnLeaveClicked;

            var isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            _leaveButton.text = isHost ? "Fermer la partie" : "Quitter";

            root.Q<Label>("mode-label").text = SessionService.Instance.Mode == SessionMode.Local
                ? "Partie locale"
                : "Partie en ligne";
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
