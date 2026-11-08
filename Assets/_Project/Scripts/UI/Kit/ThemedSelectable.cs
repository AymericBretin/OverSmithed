using UnityEngine;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// Gives a Button (or any Selectable using "Sprite Swap") its pressed and disabled sprites from the theme.
    /// The normal sprite comes from the <see cref="ThemedImage"/> of its target graphic.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    [DisallowMultipleComponent]
    public class ThemedSelectable : MonoBehaviour, IThemed
    {
        [SerializeField] private UiTheme _theme;
        [SerializeField] private UiSprite _pressed = UiSprite.ButtonPressed;
        [SerializeField] private UiSprite _disabled = UiSprite.ButtonDisabled;

        public UiTheme Theme => _theme;

        public void ApplyTheme()
        {
            if (_theme == null)
            {
                return;
            }
            var selectable = GetComponent<Selectable>();
            var sprites = selectable.spriteState;
            sprites.pressedSprite = _theme.GetSprite(_pressed);
            sprites.disabledSprite = _theme.GetSprite(_disabled);
            selectable.spriteState = sprites;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ThemedEditorHooks.OnValidate(this, ref _theme);
        }
#endif
    }
}
