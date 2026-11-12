using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// A square icon (item, station...). Shows the sprite when there is one; otherwise a colored rounded
    /// square with the first letter of the name, so the HUD works before the real icons exist (ART-9).
    /// </summary>
    public class IconView : MonoBehaviour
    {
        [SerializeField] private Image _image;
        [SerializeField] private Image _placeholder;
        [SerializeField] private TMP_Text _letter;
        [SerializeField] private LayoutElement _layout;

        // Readable on the parchment, and different enough from each other.
        private static readonly Color[] PlaceholderColors =
        {
            new(0.80f, 0.33f, 0.27f), new(0.27f, 0.55f, 0.80f), new(0.36f, 0.65f, 0.33f),
            new(0.62f, 0.40f, 0.75f), new(0.85f, 0.58f, 0.20f), new(0.25f, 0.62f, 0.60f),
            new(0.55f, 0.42f, 0.30f), new(0.75f, 0.35f, 0.55f),
        };

        /// <summary>Size used by layout groups (the icon is square).</summary>
        public void SetSize(float size)
        {
            _layout.preferredWidth = size;
            _layout.preferredHeight = size;
            ((RectTransform)transform).sizeDelta = new Vector2(size, size);
        }

        /// <summary>Shows the sprite, or a placeholder with a color picked from the name.</summary>
        public void Show(Sprite sprite, string label)
        {
            Show(sprite, label, ColorFromName(label));
        }

        public void Show(Sprite sprite, string label, Color placeholderColor)
        {
            var hasSprite = sprite != null;
            _image.enabled = hasSprite;
            _image.sprite = sprite;
            _placeholder.enabled = !hasSprite;
            _letter.enabled = !hasSprite;
            if (!hasSprite)
            {
                _placeholder.color = placeholderColor;
                _letter.text = string.IsNullOrEmpty(label) ? "?" : label.Substring(0, 1).ToUpperInvariant();
            }
        }

        // Same name, same color, on every machine and every run (string.GetHashCode is not stable).
        private static Color ColorFromName(string label)
        {
            var hash = 0;
            foreach (var c in label ?? string.Empty)
            {
                hash = hash * 31 + c;
            }
            return PlaceholderColors[(hash & int.MaxValue) % PlaceholderColors.Length];
        }
    }
}
