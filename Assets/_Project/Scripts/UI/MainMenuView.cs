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

            // Online play comes with the Relay step.
            _hostButton.SetEnabled(false);
            _joinButton.SetEnabled(false);
            _codeField.SetEnabled(false);

            _statusLabel.text = string.Empty;
            _localButton.Focus();
        }

        private void OnDisable()
        {
            _localButton.clicked -= OnLocalClicked;
        }

        private void OnLocalClicked()
        {
            _localButton.SetEnabled(false);
            if (SessionService.Instance.StartLocal())
            {
                _statusLabel.text = "Démarrage…";
            }
            else
            {
                _statusLabel.text = "Impossible de démarrer la partie locale.";
                _localButton.SetEnabled(true);
            }
        }
    }
}
