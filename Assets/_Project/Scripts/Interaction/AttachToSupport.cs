using UnityEngine;

namespace OverSmithed.Interaction
{
    /// <summary>
    /// Attaches this interactable to the interactable it rests on (for example an item on a counter), so they
    /// light up together and players aim at the counter. Looks straight down whenever the object moves.
    /// Runs on every machine from the position the network already syncs: nothing is sent.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class AttachToSupport : MonoBehaviour
    {
        [Tooltip("The ray starts this high (m) above the object's pivot: some surfaces are a bit above the item's bottom.")]
        [SerializeField] private float _rayStartHeight = 0.3f;
        [Tooltip("The ray looks this far (m) below the object's pivot.")]
        [SerializeField] private float _rayDepth = 0.2f;
        [Tooltip("A move smaller than this (m) does not look again.")]
        [SerializeField] private float _moveThreshold = 0.005f;

        private Interactable _interactable;
        private Vector3 _lastCheckedPosition;
        private bool _checkedOnce;

        private void Awake()
        {
            _interactable = GetComponent<Interactable>();
        }

        private void OnDisable()
        {
            _checkedOnce = false;
            if (_interactable.Holder != null)
            {
                _interactable.Holder.Detach(_interactable);
            }
        }

        // LateUpdate: after the NetworkTransform moved the object this frame.
        private void LateUpdate()
        {
            var position = transform.position;
            if (_checkedOnce && (position - _lastCheckedPosition).sqrMagnitude < _moveThreshold * _moveThreshold)
            {
                return;
            }
            _checkedOnce = true;
            _lastCheckedPosition = position;

            var support = FindSupport(position);
            if (support == _interactable.Holder)
            {
                return;
            }
            if (support != null)
            {
                support.Attach(_interactable);
            }
            else if (_interactable.Holder != null)
            {
                _interactable.Holder.Detach(_interactable);
            }
        }

        private Interactable FindSupport(Vector3 position)
        {
            var origin = position + Vector3.up * _rayStartHeight;
            if (!Physics.Raycast(origin, Vector3.down, out var hit, _rayStartHeight + _rayDepth,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return null;
            }
            var support = hit.collider.GetComponentInParent<Interactable>();
            return support != _interactable ? support : null;
        }
    }
}
