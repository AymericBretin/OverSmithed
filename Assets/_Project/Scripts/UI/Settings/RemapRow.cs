using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// One line of the controls screen: the action's name and a button showing its key. <see cref="ControlsScreen"/>
    /// does the work (listening for the new key, saving); this only holds what the line is about and shows texts.
    /// </summary>
    public class RemapRow : MonoBehaviour
    {
        [Tooltip("Action in the input actions asset, for example \"Player/Interact\".")]
        [SerializeField] private string _actionName = "Player/Interact";
        [Tooltip("Part of a composite (\"up\", \"down\", \"left\", \"right\" for Move), empty for a normal action.")]
        [SerializeField] private string _compositePart;
        [Tooltip("Only on the keyboard tabs (gamepad Move stays the stick and the D-pad).")]
        [SerializeField] private bool _keyboardOnly;
        [SerializeField] private Button _button;
        [SerializeField] private TMP_Text _keyText;
        [SerializeField] private ThemedText _keyTheme;
        [Tooltip("Seconds a message (\"Used by...\") stays on the button.")]
        [SerializeField] private float _messageTime = 1.6f;

        private string _key;
        private float _messageLeft;
        // The button label's normal color (light text on the wooden button).
        private const UiColor KeyColor = UiColor.TextLight;

        public string ActionName => _actionName;
        public string CompositePart => string.IsNullOrEmpty(_compositePart) ? null : _compositePart;
        public bool KeyboardOnly => _keyboardOnly;
        public Button Button => _button;

        /// <summary>The action's name in messages ("Throw", "Move up").</summary>
        public string DisplayName
        {
            get
            {
                var slash = _actionName.LastIndexOf('/');
                var name = slash >= 0 ? _actionName.Substring(slash + 1) : _actionName;
                return CompositePart != null ? $"{name} {CompositePart}" : name;
            }
        }

        /// <summary>Shows the key (and ends a message or the "press a key" text).</summary>
        public void ShowKey(string key)
        {
            _key = key;
            _messageLeft = 0f;
            SetText(key, KeyColor);
        }

        /// <summary>Shows a text instead of the key until <see cref="ShowKey"/> (for "Press a key...").</summary>
        public void ShowWaiting(string text)
        {
            _messageLeft = 0f;
            SetText(text, UiColor.Gold);
        }

        /// <summary>Shows a short message on the button, then the key again.</summary>
        public void ShowMessage(string text, UiColor color)
        {
            SetText(text, color);
            _messageLeft = _messageTime;
        }

        private void Update()
        {
            if (_messageLeft <= 0f)
            {
                return;
            }
            _messageLeft -= Time.unscaledDeltaTime;
            if (_messageLeft <= 0f)
            {
                SetText(_key, KeyColor);
            }
        }

        private void OnDisable()
        {
            if (_messageLeft > 0f)
            {
                _messageLeft = 0f;
                SetText(_key, KeyColor);
            }
        }

        private void SetText(string text, UiColor color)
        {
            _keyText.text = text;
            if (_keyTheme != null)
            {
                _keyTheme.SetColor(color);
            }
        }
    }
}
