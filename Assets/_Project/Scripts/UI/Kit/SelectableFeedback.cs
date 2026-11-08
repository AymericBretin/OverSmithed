using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// Cartoon feedback for any Selectable (button, toggle, slider...): a focus frame on the selected element,
    /// a small grow when selected, a squash when pressed, and a faded look when it can't be used. The mouse
    /// selects what it hovers, so mouse, keyboard and gamepad always show one single highlighted element.
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

        private Selectable _selectable;
        private CanvasGroup _canvasGroup;
        private bool _pressed;

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
            if (_selectable.IsInteractable() && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(gameObject);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = eventData.button == PointerEventData.InputButton.Left;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pressed = false;
        }
    }
}
