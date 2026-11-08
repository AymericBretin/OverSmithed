using UnityEngine;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// Gives an Image the sprite and color of the theme. With the sprite set to None, the Image keeps its own
    /// sprite and only the color comes from the theme.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class ThemedImage : MonoBehaviour, IThemed
    {
        [SerializeField] private UiTheme _theme;
        [SerializeField] private UiSprite _sprite = UiSprite.None;
        [SerializeField] private UiColor _color = UiColor.White;

        public UiTheme Theme => _theme;

        /// <summary>Changes the color at runtime, for example a patience bar turning red.</summary>
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
            var image = GetComponent<Image>();
            var sprite = _theme.GetSprite(_sprite);
            if (sprite != null)
            {
                image.sprite = sprite;
            }
            image.color = _theme.GetColor(_color);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ThemedEditorHooks.OnValidate(this, ref _theme);
        }
#endif
    }
}
