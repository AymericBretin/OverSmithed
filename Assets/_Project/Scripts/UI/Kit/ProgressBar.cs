using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// A bar that fills from left to right (station progress, customer patience...). Set <see cref="Value"/>
    /// between 0 and 1. With a low threshold, the bar changes color when it is almost empty.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProgressBar : MonoBehaviour
    {
        [SerializeField] private RectTransform _fill;
        [SerializeField] private ThemedImage _fillImage;
        [SerializeField, Range(0f, 1f)] private float _value = 1f;
        [SerializeField] private UiColor _color = UiColor.Success;
        [Tooltip("Below this value, the bar uses the low color. 0: never.")]
        [SerializeField, Range(0f, 1f)] private float _lowThreshold;
        [SerializeField] private UiColor _lowColor = UiColor.Danger;

        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp01(value);
                Refresh();
            }
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_fill != null)
            {
                // The fill is stretched by its anchors, so a 9-sliced sprite keeps its borders.
                _fill.anchorMax = new Vector2(_value, _fill.anchorMax.y);
            }
            if (_fillImage != null)
            {
                _fillImage.SetColor(_value < _lowThreshold ? _lowColor : _color);
            }
        }

#if UNITY_EDITOR
        // Preview in the editor when the value is changed in the Inspector.
        private void OnValidate()
        {
            if (!gameObject.scene.IsValid())
            {
                return;
            }
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    Refresh();
                }
            };
        }
#endif
    }
}
