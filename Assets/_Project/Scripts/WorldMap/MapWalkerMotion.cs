using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// Cartoon motion of the world map walker (and of its followers, <see cref="MapFollower"/>), for looks only (like PlayerBodyMotion in the levels, lighter): leans
    /// when speeding up / stopping, banks into turns, hops at each step with a small dust puff, squashes and stretches.
    /// When a level starts it jumps with a spin and lands with a dust burst. Runs on every machine from the movement it
    /// sees (the NetworkTransform syncs the position): nothing goes on the network. Only the "Visual" child is moved.
    /// </summary>
    public class MapWalkerMotion : MonoBehaviour
    {
        [Tooltip("The child that holds the skin. Empty = the child named \"Visual\".")]
        [SerializeField] private Transform _visual;
        [Tooltip("Without a MapWalker on this object (a follower): top speed (m/s) and skin scale. Set by Setup.")]
        [SerializeField] private float _maxSpeed = 5f;
        [SerializeField] private float _visualScale = 0.6f;

        [Header("Lean")]
        [Tooltip("Degrees of lean per m/s of speed change (forward when speeding up, back when stopping).")]
        [SerializeField] private float _leanPerSpeedChange = 2.5f;
        [Tooltip("Small forward lean (degrees) while walking at top speed.")]
        [SerializeField] private float _walkLean = 6f;
        [SerializeField] private float _maxLean = 15f;
        [Tooltip("Degrees of side lean per 100 degrees/second of turning, at top speed.")]
        [SerializeField] private float _bankPerTurn = 3f;
        [SerializeField] private float _maxBank = 12f;
        [SerializeField] private float _leanSmoothing = 0.08f;

        [Header("Steps")]
        [Tooltip("Height (m) of the little hop at each step, at top speed.")]
        [SerializeField] private float _hopHeight = 0.06f;
        [Tooltip("Distance (m) covered by one step.")]
        [SerializeField] private float _stepLength = 0.7f;
        [Tooltip("Dust puffs (world space particle system, emitted by script). Empty = no dust.")]
        [SerializeField] private ParticleSystem _dust;
        [Tooltip("Puffs at each step while walking fast.")]
        [SerializeField, Min(0)] private int _dustPerStep = 2;
        [Tooltip("Puffs when stopping hard and when landing.")]
        [SerializeField, Min(0)] private int _dustBurst = 8;

        [Header("Squash and stretch")]
        [SerializeField] private float _stopSquash = 0.12f;
        [SerializeField] private float _startStretch = 0.08f;
        [SerializeField] private float _springStiffness = 260f;
        [SerializeField] private float _springDamping = 11f;

        [Header("Level start jump")]
        [Tooltip("Seconds before the jump (the walker goes to the center of the circle).")]
        [SerializeField, Min(0f)] private float _jumpDelay = 0.2f;
        [SerializeField] private float _jumpHeight = 0.9f;
        [SerializeField, Min(0.05f)] private float _jumpTime = 0.5f;
        [Tooltip("Turns around itself during the jump.")]
        [SerializeField] private float _jumpSpins = 1f;
        [Tooltip("Squash before jumping (it crouches) and stretch in the air.")]
        [SerializeField] private float _jumpSquash = 0.2f;

        private MapWalker _walker;
        private Vector3 _restPosition;
        private Quaternion _restRotation;

        private Vector3 _lastPosition;
        private float _lastYaw;
        private Vector3 _slowVelocity;
        private Vector3 _slowVelocityChange;
        private float _lean;
        private float _leanChange;
        private float _bank;
        private float _bankChange;
        private float _stepPhase;
        private float _squash;
        private float _squashSpeed;
        private bool _wasMoving;
        // Level start jump: time since the launch (negative = no jump).
        private float _jumpClock = -1f;
        private bool _landed;
        // Followers jump one after the other: extra wait before this one's jump.
        private float _extraJumpDelay;

        private float MaxSpeed => _walker != null ? _walker.MaxSpeed : _maxSpeed;
        private float VisualScale => _walker != null ? _walker.VisualScale : _visualScale;

        /// <summary>For an object without a MapWalker (a follower): its top speed, skin scale and jump wait.</summary>
        public void Setup(float maxSpeed, float visualScale, float extraJumpDelay)
        {
            _maxSpeed = maxSpeed;
            _visualScale = visualScale;
            _extraJumpDelay = extraJumpDelay;
        }

        private void Awake()
        {
            _walker = GetComponent<MapWalker>();
            if (_visual == null)
            {
                _visual = transform.Find("Visual");
            }
            if (_visual == null)
            {
                Debug.LogWarning("[MapWalkerMotion] No visual child.", this);
                enabled = false;
                return;
            }
            _restPosition = _visual.localPosition;
            _restRotation = _visual.localRotation;
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
            _lastYaw = transform.eulerAngles.y;
            MapWalker.LevelLaunching += HandleLevelLaunching;
        }

        private void OnDisable()
        {
            MapWalker.LevelLaunching -= HandleLevelLaunching;
            if (_visual != null)
            {
                _visual.SetLocalPositionAndRotation(_restPosition, _restRotation);
                _visual.localScale = Vector3.one * VisualScale;
            }
        }

        private void HandleLevelLaunching(LevelNode node)
        {
            // Null = the start was cancelled: stop the jump.
            _jumpClock = node != null ? 0f : -1f;
            _landed = false;
        }

        // LateUpdate: after the walker moved this frame (host simulation or NetworkTransform smoothing).
        private void LateUpdate()
        {
            var deltaTime = Time.deltaTime;
            var maxSpeed = MaxSpeed;
            if (deltaTime <= 0f || maxSpeed <= 0f)
            {
                return;
            }

            var position = transform.position;
            var moved = position - _lastPosition;
            moved.y = 0f;
            _lastPosition = position;
            var velocity = moved / deltaTime;
            // Much faster than walking = a teleport (start spot): not a movement.
            if (velocity.magnitude > maxSpeed * 3f)
            {
                velocity = Vector3.zero;
                moved = Vector3.zero;
                _slowVelocity = Vector3.zero;
            }
            _slowVelocity = Vector3.SmoothDamp(_slowVelocity, velocity, ref _slowVelocityChange, 0.12f);

            var yaw = transform.eulerAngles.y;
            var turnRate = Mathf.DeltaAngle(_lastYaw, yaw) / deltaTime;
            _lastYaw = yaw;

            var speed01 = Mathf.Clamp01(velocity.magnitude / maxSpeed);
            var forward = transform.forward;

            var change = Vector3.Dot(velocity - _slowVelocity, forward);
            var targetLean = Mathf.Clamp(change * _leanPerSpeedChange + speed01 * _walkLean, -_maxLean, _maxLean);
            _lean = Mathf.SmoothDamp(_lean, targetLean, ref _leanChange, _leanSmoothing);
            var targetBank = Mathf.Clamp(-turnRate / 100f * _bankPerTurn * speed01, -_maxBank, _maxBank);
            _bank = Mathf.SmoothDamp(_bank, targetBank, ref _bankChange, _leanSmoothing);

            // One hop per step; a dust puff each time a foot lands while walking fast.
            var previousStep = Mathf.FloorToInt(_stepPhase / Mathf.PI);
            _stepPhase += moved.magnitude / Mathf.Max(0.1f, _stepLength) * Mathf.PI;
            if (Mathf.FloorToInt(_stepPhase / Mathf.PI) != previousStep && speed01 > 0.5f)
            {
                EmitDust(_dustPerStep);
            }
            var hop = Mathf.Abs(Mathf.Sin(_stepPhase)) * _hopHeight * speed01;
            if (speed01 < 0.05f)
            {
                _stepPhase = 0f;
            }

            var moving = speed01 > 0.4f;
            if (moving && !_wasMoving)
            {
                _squashSpeed -= _startStretch * 25f;
            }
            if (!moving && _wasMoving && Vector3.Dot(_slowVelocity, forward) > maxSpeed * 0.5f)
            {
                _squashSpeed += _stopSquash * 25f;
                EmitDust(_dustBurst / 2);
            }
            _wasMoving = moving;

            var jumpHeight = 0f;
            var spin = 0f;
            UpdateJump(deltaTime, ref jumpHeight, ref spin);

            // Small steps: one long frame (hitch, scene load) must not make the stiff spring explode.
            var remaining = Mathf.Min(deltaTime, 0.1f);
            while (remaining > 0f)
            {
                var step = Mathf.Min(remaining, 1f / 120f);
                remaining -= step;
                _squashSpeed += (-_springStiffness * _squash - _springDamping * _squashSpeed) * step;
                _squash += _squashSpeed * step;
            }
            _squash = Mathf.Clamp(_squash, -0.35f, 0.35f);

            // Squash > 0 = lower and wider.
            var scale = new Vector3(1f + _squash * 0.5f, 1f - _squash, 1f + _squash * 0.5f) * VisualScale;
            _visual.localScale = scale;
            _visual.SetLocalPositionAndRotation(_restPosition + Vector3.up * (hop + jumpHeight),
                _restRotation * Quaternion.Euler(_lean, spin, _bank));
        }

        // Crouch, jump with a spin, land with a dust burst. The squash spring does the crouch and the landing wobble.
        private void UpdateJump(float deltaTime, ref float height, ref float spin)
        {
            if (_jumpClock < 0f)
            {
                return;
            }
            var previous = _jumpClock;
            _jumpClock += deltaTime;
            const float crouchTime = 0.12f;
            var crouchAt = _jumpDelay + _extraJumpDelay;
            var takeOff = crouchAt + crouchTime;
            // Crouch kick, then stretch kick when leaving the ground.
            if (previous < crouchAt && _jumpClock >= crouchAt)
            {
                _squashSpeed += _jumpSquash * 20f;
            }
            if (previous < takeOff && _jumpClock >= takeOff)
            {
                _squashSpeed -= _jumpSquash * 35f;
            }
            var t = (_jumpClock - takeOff) / _jumpTime;
            if (t <= 0f)
            {
                return;
            }
            if (t < 1f)
            {
                height = 4f * t * (1f - t) * _jumpHeight;
                // Ease in-out spin, the same way round whatever the facing.
                spin = Mathf.SmoothStep(0f, 360f * _jumpSpins, t);
                return;
            }
            if (!_landed)
            {
                _landed = true;
                _squashSpeed += _jumpSquash * 30f;
                EmitDust(_dustBurst);
            }
        }

        private void EmitDust(int count)
        {
            if (_dust == null || count <= 0)
            {
                return;
            }
            var emit = new ParticleSystem.EmitParams
            {
                position = transform.position + Vector3.up * 0.05f,
                applyShapeToPosition = true
            };
            _dust.Emit(emit, count);
        }
    }
}
