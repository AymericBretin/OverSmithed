using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.Players
{
    /// <summary>
    /// Owner-authoritative movement: the machine that owns the player moves it locally, right away, and
    /// the NetworkTransform (authority: Owner) replicates the result to every other machine.
    /// Whether moving is allowed at all is a rule decided by the host (<see cref="NetworkPlayer.CanMove"/>).
    /// The feel: a very short speed-up and a small slide when stopping, turns that are fast first and soft at
    /// the end, and a dash (short burst, then a cooldown). The dash is local too, so it is instant even online.
    /// Gravity: with no floor under them, players fall (off the map: see <see cref="PlayerFall"/>). Walls, counters
    /// and other players stop them (CharacterController collisions).
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(CharacterController))]
    public class PlayerMovement : NetworkBehaviour
    {
        [Header("Run")]
        [Tooltip("Top speed (m/s), stick or keys pushed all the way.")]
        [SerializeField] private float _speed = 6f;
        [Tooltip("Time (seconds) to reach top speed from standing still.")]
        [SerializeField] private float _accelerationTime = 0.08f;
        [Tooltip("Time (seconds) to stop from top speed when the stick is released (a small slide).")]
        [SerializeField] private float _stopTime = 0.12f;

        [Header("Turn")]
        [Tooltip("How fast the player turns to face where it goes. Higher = snappier. The turn is fast at first " +
                 "and soft at the end.")]
        [SerializeField] private float _turnSharpness = 20f;

        [Header("Dash")]
        [Tooltip("Speed (m/s) at the start of the dash; it slows down to the run speed by the end.")]
        [SerializeField] private float _dashSpeed = 18f;
        [Tooltip("Time (seconds) the dash lasts.")]
        [SerializeField] private float _dashTime = 0.16f;
        [Tooltip("Time (seconds) after a dash starts before the next one is possible.")]
        [SerializeField] private float _dashCooldown = 0.5f;

        [Header("Fall")]
        [Tooltip("Gravity (m/s²) when there is no floor under the player. Higher than real (9.8) feels less floaty.")]
        [SerializeField] private float _gravity = 30f;
        [Tooltip("Fastest fall speed (m/s).")]
        [SerializeField] private float _maxFallSpeed = 25f;

        private NetworkPlayer _player;
        private CharacterController _controller;
        private LocalPlayerSlot _slot;
        private InputAction _moveAction;
        private InputAction _dashAction;

        private Vector3 _runVelocity;
        private Vector3 _dashDirection;
        private float _dashLeft;
        private float _nextDashTime;
        private float _fallSpeed;

        /// <summary>Top speed in meters per second (stick or keys pushed all the way).</summary>
        public float MaxSpeed => _speed;

        /// <summary>
        /// Owner machine only: how fast this player moves on the ground (m/s), from the last move. Zero elsewhere.
        /// For example a dropped item keeps some of it.
        /// </summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Owner machine only: a dash is going on.</summary>
        public bool IsDashing => _dashLeft > 0f;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !_player.CanAct || !TryGetActions(out var moveAction, out var dashAction))
            {
                StopMoving();
                return;
            }
            var deltaTime = Time.deltaTime;

            // The camera looks along +Z without yaw, so stick/keys map directly onto the ground plane.
            var input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            var direction = new Vector3(input.x, 0f, input.y);

            // Run: speed up fast, slow down a bit slower (a small slide).
            var wanted = direction * _speed;
            var speedingUp = wanted.sqrMagnitude >= _runVelocity.sqrMagnitude;
            var rate = _speed / Mathf.Max(0.001f, speedingUp ? _accelerationTime : _stopTime);
            _runVelocity = Vector3.MoveTowards(_runVelocity, wanted, rate * deltaTime);

            // Dash: where the stick points, or straight ahead when it is released.
            if (dashAction != null && dashAction.WasPressedThisFrame() && Time.time >= _nextDashTime)
            {
                _dashDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
                _dashLeft = _dashTime;
                _nextDashTime = Time.time + _dashCooldown;
                transform.rotation = Quaternion.LookRotation(_dashDirection);
            }

            var velocity = _runVelocity;
            if (_dashLeft > 0f)
            {
                // From dash speed down to run speed: the dash ends in the run, not in a stop.
                var progress = 1f - _dashLeft / _dashTime;
                velocity = _dashDirection * Mathf.Lerp(_dashSpeed, _speed, progress * progress);
                _dashLeft -= deltaTime;
                if (_dashLeft <= 0f)
                {
                    _runVelocity = _dashDirection * _speed * Mathf.Max(direction.magnitude, 0.5f);
                }
            }

            // On the floor a small downward speed keeps the player on it (also down small steps).
            if (_controller.isGrounded && _fallSpeed < 0f)
            {
                _fallSpeed = -2f;
            }
            _fallSpeed = Mathf.Max(_fallSpeed - _gravity * deltaTime, -_maxFallSpeed);
            velocity.y = _fallSpeed;

            // A CharacterController must only move through Move(): it stops on walls, counters and other players.
            _controller.Move(velocity * deltaTime);
            var moved = _controller.velocity;
            Velocity = new Vector3(moved.x, 0f, moved.z);
            // Against a wall the run speed drops too, so the player doesn't "push" for a moment after.
            if (_dashLeft <= 0f && Velocity.sqrMagnitude < _runVelocity.sqrMagnitude)
            {
                _runVelocity = Velocity;
            }

            // Turn toward where the stick points: fast first, soft at the end (not a constant speed).
            var facing = _dashLeft > 0f ? _dashDirection : direction;
            if (facing.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing),
                    1f - Mathf.Exp(-_turnSharpness * deltaTime));
            }
        }

        /// <summary>
        /// Owner machine only: puts the player at <paramref name="position"/> right away (no smoothing on the
        /// other machines), standing still. Used to come back after a fall.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (!IsOwner)
            {
                return;
            }
            // A CharacterController keeps its own position: turn it off while the transform is moved.
            _controller.enabled = false;
            if (TryGetComponent<NetworkTransform>(out var networkTransform) && networkTransform.CanCommitToTransform)
            {
                networkTransform.Teleport(position, rotation, transform.localScale);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }
            _controller.enabled = true;
            StopMoving();
        }

        private void StopMoving()
        {
            _runVelocity = Vector3.zero;
            _dashLeft = 0f;
            _fallSpeed = 0f;
            Velocity = Vector3.zero;
        }

        private bool TryGetActions(out InputAction moveAction, out InputAction dashAction)
        {
            if (_slot != _player.LocalSlot)
            {
                _slot = _player.LocalSlot;
                _moveAction = _slot != null ? _slot.Input.actions.FindAction("Move") : null;
                _dashAction = _slot != null ? _slot.Input.actions.FindAction("Dash") : null;
            }
            moveAction = _moveAction;
            dashAction = _dashAction;
            return moveAction != null;
        }
    }
}
