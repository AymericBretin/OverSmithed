using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.Players
{
    /// <summary>
    /// Owner-authoritative movement: the machine that owns the player moves it locally, right away, and
    /// the NetworkTransform (authority: Owner) replicates the result to every other machine.
    /// Whether moving is allowed at all is a rule decided by the host (<see cref="NetworkPlayer.CanMove"/>).
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(CharacterController))]
    public class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float _speed = 5f;
        [SerializeField] private float _turnSpeed = 720f;
        [Tooltip("Players are kept inside a square of this half size, centered on the origin.")]
        [SerializeField] private float _arenaHalfSize = 9.5f;

        private NetworkPlayer _player;
        private CharacterController _controller;
        private LocalPlayerSlot _slot;
        private InputAction _moveAction;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !_player.CanMove || !TryGetMoveAction(out var moveAction))
            {
                return;
            }

            // The camera looks along +Z without yaw, so stick/keys map directly onto the ground plane.
            var input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            var direction = new Vector3(input.x, 0f, input.y);

            // Clamp the requested motion rather than the position: a CharacterController must only move through Move().
            var position = transform.position;
            var target = position + direction * (_speed * Time.deltaTime);
            target.x = Mathf.Clamp(target.x, -_arenaHalfSize, _arenaHalfSize);
            target.z = Mathf.Clamp(target.z, -_arenaHalfSize, _arenaHalfSize);
            _controller.Move(target - position);

            if (direction.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), _turnSpeed * Time.deltaTime);
            }
        }

        private bool TryGetMoveAction(out InputAction moveAction)
        {
            if (_slot != _player.LocalSlot)
            {
                _slot = _player.LocalSlot;
                _moveAction = _slot != null ? _slot.Input.actions.FindAction("Move") : null;
            }
            moveAction = _moveAction;
            return moveAction != null;
        }
    }
}
