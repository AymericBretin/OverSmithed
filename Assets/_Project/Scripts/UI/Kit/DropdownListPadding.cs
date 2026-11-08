using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// Goes on the Template of a dropdown. TMP_Dropdown sizes its list as if the viewport filled the whole list,
    /// but our viewport sits inside a margin (so the highlight doesn't touch the rounded corners): a short list
    /// would be a bit too small and scroll. This grows the list back by the missing height and, when nothing
    /// needs to scroll, gives the scrollbar's space back to the viewport. Long lists still scroll.
    /// </summary>
    [DisallowMultipleComponent]
    public class DropdownListPadding : MonoBehaviour
    {
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;

        // Start runs after TMP_Dropdown.Show() has sized the list copy.
        private void Start()
        {
            if (_viewport == null || _content == null)
            {
                return;
            }
            var list = (RectTransform)transform;
            var margin = list.rect.height - _viewport.rect.height;
            var missing = _content.rect.height - _viewport.rect.height;
            if (missing > margin + 0.5f)
            {
                return; // Long list: it scrolls, keep the room for the scrollbar.
            }
            if (missing > 0f)
            {
                list.sizeDelta = new Vector2(list.sizeDelta.x, list.sizeDelta.y + missing);
            }
            // The viewport is not a direct child of the ScrollRect, so the ScrollRect can't widen it itself.
            _viewport.offsetMax = new Vector2(0f, _viewport.offsetMax.y);
        }
    }
}
