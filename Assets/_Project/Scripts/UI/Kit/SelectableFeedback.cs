using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// Cartoon feedback for any Selectable (button, toggle, slider...): a focus frame on the selected element,
    /// a small grow when selected, a squash when pressed, and a faded look when it can't be used. The mouse
    /// selects what it hovers, so mouse, keyboard and gamepad always show one single highlighted element, but only
    /// WHILE it hovers: when the pointer leaves, the selection goes back to what the keyboard / gamepad had selected
    /// before (else Space / Enter / A would press a button the mouse only passed over).
    /// Mouse-only buttons (<see cref="_selectOnHover"/> off, e.g. the lobby where A / Space / Enter join or start)
    /// are never selected at all: they only grow under the pointer.
    /// </summary>
    [RequireComponent(typeof(Selectable), typeof(CanvasGroup))]
    [DisallowMultipleComponent]
    public class SelectableFeedback : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler,
        IPointerExitHandler
    {
        [Tooltip("Shown while this element is selected (keyboard, gamepad or mouse hover).")]
        [SerializeField] private GameObject _focusFrame;
        [Tooltip("What grows and squashes. Empty: this object.")]
        [SerializeField] private RectTransform _scaleTarget;
        [SerializeField] private float _selectedScale = 1.05f;
        [SerializeField] private float _pressedScale = 0.95f;
        [SerializeField] private float _scaleSpeed = 18f;
        [Tooltip("Opacity of the whole element when it can't be used.")]
        [SerializeField, Range(0f, 1f)] private float _disabledAlpha = 0.5f;
        [Tooltip("On: hovering selects this element (menus usable with mouse, keys and gamepad). Off: a mouse-only " +
                 "button, never selected, so the submit keys (A / Space / Enter) can never press it.")]
        [SerializeField] private bool _selectOnHover = true;

        // The element the mouse selected by hovering it, and what was selected before (given back on exit).
        private static SelectableFeedback _hoverOwner;
        private static GameObject _selectedBeforeHover;

        private Selectable _selectable;
        private CanvasGroup _canvasGroup;
        private bool _pressed;
        private bool _hovered;

        /// <summary>True when <paramref name="target"/> is selected only because the mouse is over it.</summary>
        public static bool IsHoverSelection(GameObject target)
        {
            return _hoverOwner != null && target != null && _hoverOwner.gameObject == target;
        }

        // Domain reload is off: forget the hover state when Play Mode starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _hoverOwner = null;
            _selectedBeforeHover = null;
        }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_scaleTarget == null)
            {
                _scaleTarget = (RectTransform)transform;
            }
        }

        private void OnDisable()
        {
            if (_hoverOwner == this)
            {
                EndHover();
            }
            _hovered = false;
            _pressed = false;
            _scaleTarget.localScale = Vector3.one;
            if (_focusFrame != null)
            {
                _focusFrame.SetActive(false);
            }
        }

        // Unscaled time: the feedback keeps working while the game is paused (Time.timeScale = 0).
        private void LateUpdate()
        {
            var interactable = _selectable.IsInteractable();
            var eventSystem = EventSystem.current;
            var selected = interactable && eventSystem != null && eventSystem.currentSelectedGameObject == gameObject;
            if (!_selectOnHover)
            {
                selected = interactable && _hovered;
            }

            if (_focusFrame != null && _focusFrame.activeSelf != selected)
            {
                _focusFrame.SetActive(selected);
            }
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = interactable ? 1f : _disabledAlpha;
            }

            var goal = !interactable ? 1f : _pressed ? _pressedScale : selected ? _selectedScale : 1f;
            var current = _scaleTarget.localScale.x;
            var next = Mathf.Lerp(current, goal, 1f - Mathf.Exp(-_scaleSpeed * Time.unscaledDeltaTime));
            _scaleTarget.localScale = new Vector3(next, next, 1f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            var eventSystem = EventSystem.current;
            if (!_selectOnHover || !_selectable.IsInteractable() || eventSystem == null)
            {
                return;
            }
            var current = eventSystem.currentSelectedGameObject;
            if (current == gameObject)
            {
                return;
            }
            // Moving from one hovered element to the next keeps the selection the keyboard / gamepad had first.
            if (_hoverOwner == null)
            {
                _selectedBeforeHover = current;
            }
            _hoverOwner = this;
            eventSystem.SetSelectedGameObject(gameObject);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = eventData.button == PointerEventData.InputButton.Left;
            // Clicking a text field, slider or dropdown really selects it (you type in it, drag it, open it): it must
            // stay selected when the mouse moves away. Buttons and toggles still give the selection back on exit.
            if (_hoverOwner == this && _selectable is not Button && _selectable is not Toggle)
            {
                _hoverOwner = null;
                _selectedBeforeHover = null;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            // A click selects the clicked element (EventSystem): a mouse-only button must not stay selected.
            var eventSystem = EventSystem.current;
            if (!_selectOnHover && eventSystem != null && eventSystem.currentSelectedGameObject == gameObject)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pressed = false;
            _hovered = false;
            if (_hoverOwner == this)
            {
                EndHover();
            }
        }

        // Gives the selection back to what the keyboard / gamepad had, unless they moved it in the meantime.
        private void EndHover()
        {
            var eventSystem = EventSystem.current;
            var before = _selectedBeforeHover;
            _hoverOwner = null;
            _selectedBeforeHover = null;
            if (eventSystem == null || eventSystem.currentSelectedGameObject != gameObject)
            {
                return;
            }
            eventSystem.SetSelectedGameObject(before != null && before.activeInHierarchy ? before : null);
        }
    }
}
