using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>Shows 0 to 3 stars (results screen, level popup on the world map).</summary>
    [DisallowMultipleComponent]
    public class StarRating : MonoBehaviour
    {
        [SerializeField] private ThemedImage[] _stars = new ThemedImage[3];
        [SerializeField, Range(0, 3)] private int _value;

        public int Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp(value, 0, _stars.Length);
                Refresh();
            }
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            for (var i = 0; i < _stars.Length; i++)
            {
                if (_stars[i] == null)
                {
                    continue;
                }
                var full = i < _value;
                _stars[i].SetLook(full ? UiSprite.StarFull : UiSprite.StarEmpty, full ? UiColor.Gold : UiColor.Disabled);
            }
        }

#if UNITY_EDITOR
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
