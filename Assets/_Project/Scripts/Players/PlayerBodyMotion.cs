using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Cartoon body motion, for looks only: the visual child leans forward when speeding up and back when
    /// stopping, banks into turns, hops a little at each running step, dives forward while dashing, and
    /// squashes / stretches (start, stop, dash end). Runs on every machine from the movement it sees (the NetworkTransform already syncs the position),
    /// so everyone sees the same thing and nothing goes on the network. The player root (its real position) is
    /// never moved; only the "Visual" child is.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerMovement))]
    public class PlayerBodyMotion : MonoBehaviour
    {
        [Tooltip("The child that holds the skin. Empty = the child named \"Visual\".")]
        [SerializeField] private Transform _visual;

        [Header("Lean")]
        [Tooltip("Degrees of lean per m/s of speed change (forward when speeding up, back when stopping).")]
        [SerializeField] private float _leanPerSpeedChange = 2.5f;
        [Tooltip("Small forward lean (degrees) while running at top speed.")]
        [SerializeField] private float _runLean = 5f;
        [SerializeField] private float _maxLean = 15f;
        [Tooltip("Degrees of side lean per 100 degrees/second of turning, at top speed.")]
        [SerializeField] private float _bankPerTurn = 3f;
        [SerializeField] private float _maxBank = 14f;
        [Tooltip("Time (seconds) to smooth the lean and the bank.")]
        [SerializeField] private float _leanSmoothing = 0.08f;

        [Header("Running hop")]
        [Tooltip("Height (m) of the little hop at each step, at top speed.")]
        [SerializeField] private float _hopHeight = 0.07f;
        [Tooltip("Distance (m) covered by one step.")]
        [SerializeField] private float _stepLength = 0.9f;

        [Header("Squash and stretch")]
        [Tooltip("Squash when stopping hard (0.1 = 10 % lower).")]
        [SerializeField] private float _stopSquash = 0.12f;
        [Tooltip("Stretch up when starting to run.")]
        [SerializeField] private float _startStretch = 0.08f;
        [Tooltip("Small squash when a dash ends (0.05 = 5 % lower).")]
        [SerializeField] private float _dashEndSquash = 0.05f;
        [Tooltip("Extra forward lean (degrees) while dashing: a dive.")]
        [SerializeField] private float _dashLean = 15f;
        [Tooltip("Spring of the squash: higher = faster wobble.")]
        [SerializeField] private float _springStiffness = 260f;
        [Tooltip("Damping of the squash: lower = more wobble.")]
        [SerializeField] private float _springDamping = 11f;

        private PlayerMovement _movement;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private Vector3 _restScale;

        private Vector3 _lastPosition;
        private float _lastYaw;
        private Vector3 _velocity;
        private Vector3 _slowVelocity;
        private Vector3 _slowVelocityChange;

        private float _lean;
        private float _leanChange;
        private float _bank;
        private float _bankChange;
        private float _stepPhase;
        private float _squash;
        private float _squashSpeed;
        private float _dashAmount;
        private bool _wasMoving;
        private bool _wasDashing;

        /// <summary>
        /// Every machine: the player is dashing, as seen from the movement (faster than 1.4 × the run speed), so it
        /// works for remote players too without network data. Used by <see cref="PlayerDashTrail"/>.
        /// </summary>
        public bool IsDashing => _wasDashing;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            if (_visual == null)
            {
                _visual = transform.Find("Visual");
            }
            if (_visual == null)
            {
                Debug.LogWarning("[PlayerBodyMotion] No visual child.", this);
                enabled = false;
                return;
            }
            _restPosition = _visual.localPosition;
            _restRotation = _visual.localRotation;
            _restScale = _visual.localScale;
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
            _lastYaw = transform.eulerAngles.y;
        }

        private void OnDisable()
        {
            if (_visual != null)
            {
                _visual.SetLocalPositionAndRotation(_restPosition, _restRotation);
                _visual.localScale = _restScale;
            }
        }

        // LateUpdate: after the player moved this frame (owner input or NetworkTransform smoothing).
        private void LateUpdate()
        {
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f || _movement.MaxSpeed <= 0f)
            {
                return;
            }
            var maxSpeed = _movement.MaxSpeed;

            var position = transform.position;
            var moved = position - _lastPosition;
            moved.y = 0f;
            _lastPosition = position;
            var measured = moved / deltaTime;
            // A jump much faster than a dash is a teleport (spawn, respawn): not a movement.
            if (measured.magnitude > maxSpeed * 5f)
            {
                measured = Vector3.zero;
                _slowVelocity = Vector3.zero;
            }
            _velocity = measured;
            _slowVelocity = Vector3.SmoothDamp(_slowVelocity, _velocity, ref _slowVelocityChange, 0.12f);

            var yaw = transform.eulerAngles.y;
            var turnRate = Mathf.DeltaAngle(_lastYaw, yaw) / deltaTime;
            _lastYaw = yaw;

            var speed = _velocity.magnitude;
            var speed01 = Mathf.Clamp01(speed / maxSpeed);
            var forward = transform.forward;

            // Lean: the difference between the speed now and a moment ago = speeding up (+) or stopping (-).
            var change = Vector3.Dot(_velocity - _slowVelocity, forward);
            var targetLean = Mathf.Clamp(change * _leanPerSpeedChange + speed01 * _runLean, -_maxLean, _maxLean)
                             + _dashAmount * _dashLean;
            _lean = Mathf.SmoothDamp(_lean, targetLean, ref _leanChange, _leanSmoothing);
            // Bank: lean into the turn, more when running fast.
            var targetBank = Mathf.Clamp(-turnRate / 100f * _bankPerTurn * speed01, -_maxBank, _maxBank);
            _bank = Mathf.SmoothDamp(_bank, targetBank, ref _bankChange, _leanSmoothing);

            // Running hop: one small hop per step, as high as the speed allows.
            _stepPhase += moved.magnitude / Mathf.Max(0.1f, _stepLength) * Mathf.PI;
            var hop = Mathf.Abs(Mathf.Sin(_stepPhase)) * _hopHeight * Mathf.Min(speed01, 1f);
            if (speed01 < 0.05f)
            {
                _stepPhase = 0f;
            }

            // Squash kicks: start running (stretch up), stop hard (squash), dash end (squash).
            var moving = speed01 > 0.4f;
            var dashing = speed > maxSpeed * 1.4f;
            if (moving && !_wasMoving)
            {
                _squashSpeed -= _startStretch * 25f;
            }
            if (!moving && _wasMoving && Vector3.Dot(_slowVelocity, forward) > maxSpeed * 0.5f)
            {
                _squashSpeed += _stopSquash * 25f;
            }
            if (!dashing && _wasDashing)
            {
                _squashSpeed += _dashEndSquash * 25f;
            }
            _wasMoving = moving;
            _wasDashing = dashing;
            // Spring back to the rest shape, with a little wobble.
            _squashSpeed += (-_springStiffness * _squash - _springDamping * _squashSpeed) * deltaTime;
            _squash += _squashSpeed * deltaTime;
            _dashAmount = Mathf.MoveTowards(_dashAmount, dashing ? 1f : 0f, deltaTime / 0.06f);

            // Apply on the visual only. Squash > 0 = lower and wider.
            var scale = new Vector3(1f + _squash * 0.5f, 1f - _squash, 1f + _squash * 0.5f);
            _visual.localScale = Vector3.Scale(_restScale, scale);
            _visual.SetLocalPositionAndRotation(_restPosition + Vector3.up * hop,
                _restRotation * Quaternion.Euler(_lean, 0f, _bank));
        }
    }
}
