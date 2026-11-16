using UnityEngine;

namespace OverSmithed.Items
{
    /// <summary>
    /// Makes the item's visual move nicely. Only the visual child moves, on each machine, with no network data:
    /// the item itself is always exactly where the host put it (rules stay instant).
    /// - When the item jumps somewhere (counter to hands, crate to hands, hands to counter…), the visual flies
    ///   from where it was drawn to its new place, on a small arc, then does a little "pop".
    /// - <see cref="HandsLift"/>: while carried, the player's animator moves it up and down with the hands.
    /// This is the only script that moves the visual child.
    /// </summary>
    // Runs after the other LateUpdates (the player's animator sets HandsLift there), right before drawing.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(NetworkItem))]
    public class ItemVisualMotion : MonoBehaviour
    {
        [Tooltip("A move longer than this (m) in one frame is a jump: the visual flies instead of teleporting.")]
        [SerializeField] private float _jumpDistance = 0.3f;
        [Tooltip("Time (seconds) of the flight to the new place.")]
        [SerializeField] private float _flyTime = 0.18f;
        [Tooltip("How high (m) the flight goes above the straight line, in the middle.")]
        [SerializeField] private float _arcHeight = 0.25f;
        [Tooltip("Size at the top of the pop when the item lands (1 = no pop).")]
        [SerializeField] private float _popScale = 1.15f;
        [Tooltip("Time (seconds) of the pop when the item lands.")]
        [SerializeField] private float _popTime = 0.15f;

        private Transform _visual;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private Vector3 _restScale;

        private Vector3 _lastAnchor;
        private Vector3 _shownPosition;
        private Quaternion _shownRotation;

        private bool _flying;
        private float _flyElapsed;
        private Vector3 _flyFromPosition;
        private Quaternion _flyFromRotation;
        private float _popElapsed = float.MaxValue;
        private float _noFlightUntil = float.MinValue;

        /// <summary>Extra height (m, world) of the visual: set every frame by the player carrying the item, 0 otherwise.</summary>
        public float HandsLift { get; set; }

        private void Awake()
        {
            _visual = GetComponent<NetworkItem>().VisualRoot;
            if (_visual == null)
            {
                Debug.LogWarning("[ItemVisualMotion] The item has no visual child: nothing to move.", this);
                enabled = false;
                return;
            }
            _restPosition = _visual.localPosition;
            _restRotation = _visual.localRotation;
            _restScale = _visual.localScale;
            Snap();
        }

        /// <summary>
        /// The item is where it should be: no flight from the previous place (called when the item spawns:
        /// a client may first create it at the scene origin, then move it).
        /// </summary>
        public void Snap()
        {
            if (_visual == null)
            {
                return;
            }
            _flying = false;
            _lastAnchor = transform.position;
            _shownPosition = _visual.parent.TransformPoint(_restPosition) + Vector3.up * HandsLift;
            _shownRotation = _visual.parent.rotation * _restRotation;
        }

        /// <summary>
        /// The item was put somewhere far away on purpose (back on its counter after a fall): it appears there with
        /// a pop, without flying. Works whether the new position arrived before or after this call (next 0.5 s).
        /// </summary>
        public void SkipNextFlight()
        {
            Snap();
            _noFlightUntil = Time.time + 0.5f;
            _popElapsed = 0f;
        }

        /// <summary>The visual flies from <paramref name="worldPosition"/> to the item (for example out of a crate).</summary>
        public void FlyFrom(Vector3 worldPosition)
        {
            if (_visual == null || _flyTime <= 0f)
            {
                return;
            }
            _flying = true;
            _flyElapsed = 0f;
            _flyFromPosition = worldPosition;
            _flyFromRotation = _shownRotation;
            _lastAnchor = transform.position;
        }

        private void LateUpdate()
        {
            var deltaTime = Time.deltaTime;
            var anchor = transform.position;
            if ((anchor - _lastAnchor).sqrMagnitude > _jumpDistance * _jumpDistance && Time.time < _noFlightUntil)
            {
                Snap();
                _noFlightUntil = float.MinValue;
                _popElapsed = 0f;
            }
            else if ((anchor - _lastAnchor).sqrMagnitude > _jumpDistance * _jumpDistance && _flyTime > 0f)
            {
                _flying = true;
                _flyElapsed = 0f;
                _flyFromPosition = _shownPosition;
                _flyFromRotation = _shownRotation;
            }
            _lastAnchor = anchor;

            var parent = _visual.parent;
            var targetPosition = parent.TransformPoint(_restPosition) + Vector3.up * HandsLift;
            var targetRotation = parent.rotation * _restRotation;

            Vector3 position;
            Quaternion rotation;
            if (_flying)
            {
                var t = Mathf.Clamp01(_flyElapsed / _flyTime);
                var eased = 1f - (1f - t) * (1f - t); // fast start, soft landing
                position = Vector3.LerpUnclamped(_flyFromPosition, targetPosition, eased)
                           + Vector3.up * (_arcHeight * 4f * t * (1f - t));
                rotation = Quaternion.Slerp(_flyFromRotation, targetRotation, eased);
                _flyElapsed += deltaTime;
                if (t >= 1f)
                {
                    _flying = false;
                    _popElapsed = 0f;
                }
            }
            else
            {
                position = targetPosition;
                rotation = targetRotation;
            }

            var scale = 1f;
            if (_popElapsed < _popTime)
            {
                scale += (_popScale - 1f) * Mathf.Sin(Mathf.PI * _popElapsed / _popTime);
                _popElapsed += deltaTime;
            }

            _visual.SetPositionAndRotation(position, rotation);
            _visual.localScale = _restScale * scale;
            _shownPosition = position;
            _shownRotation = rotation;
        }
    }
}
