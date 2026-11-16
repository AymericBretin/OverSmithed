using UnityEngine;
using UnityEngine.EventSystems;

namespace OverSmithed.UI
{
    /// <summary>
    /// Keeps one element selected on a screen, so a gamepad or the keyboard can always navigate. A mouse click on an
    /// empty spot (or a selected element being hidden) would leave nothing selected: the last selected element, or
    /// the default one, is then selected again. Screens call <see cref="Select"/> when they open a panel.
    /// </summary>
    public class KeepSelection : MonoBehaviour
    {
        [Tooltip("Selected when the screen starts, and when nothing else can be selected again.")]
        [SerializeField] private GameObject _defaultSelection;

        private GameObject _lastSelected;

        public GameObject DefaultSelection
        {
            get => _defaultSelection;
            set => _defaultSelection = value;
        }

        /// <summary>Selects <paramref name="target"/> now (for example the first button of a panel that opens).</summary>
        public void Select(GameObject target)
        {
            _lastSelected = target;
            if (EventSystem.current != null && target != null)
            {
                EventSystem.current.SetSelectedGameObject(target);
            }
        }

        private void Start()
        {
            Select(_defaultSelection);
        }

        private void LateUpdate()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }
            var current = eventSystem.currentSelectedGameObject;
            if (current != null && current.activeInHierarchy)
            {
                _lastSelected = current;
                return;
            }
            var fallback = _lastSelected != null && _lastSelected.activeInHierarchy ? _lastSelected : _defaultSelection;
            if (fallback != null && fallback.activeInHierarchy && fallback != current)
            {
                eventSystem.SetSelectedGameObject(fallback);
            }
        }
    }
}
