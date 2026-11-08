using TMPro;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// Gives a TextMeshPro text the font, size and color of the theme. Pick a style and a color here instead of
    /// setting them on the text by hand.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    [DisallowMultipleComponent]
    public class ThemedText : MonoBehaviour, IThemed
    {
        [SerializeField] private UiTheme _theme;
        [SerializeField] private UiTextStyle _style = UiTextStyle.Body;
        [SerializeField] private UiColor _color = UiColor.TextDark;

        public UiTheme Theme => _theme;

        /// <summary>Changes the color at runtime, for example to show a warning in red.</summary>
        public void SetColor(UiColor color)
        {
            _color = color;
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            if (_theme == null)
            {
                return;
            }
            var text = GetComponent<TMP_Text>();
            if (_theme.TryGetTextStyle(_style, out var style))
            {
                if (style.Font != null)
                {
                    text.font = style.Font;
                }
                if (style.Material != null)
                {
                    text.fontSharedMaterial = style.Material;
                }
                text.fontSize = style.Size;
                text.fontStyle = style.Style;
            }
            text.color = _theme.GetColor(_color);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ThemedEditorHooks.OnValidate(this, ref _theme);
        }
#endif
    }
}
