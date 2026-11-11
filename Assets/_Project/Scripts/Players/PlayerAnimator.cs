using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Drives the idle / walk / run animations of the skin visual.
    /// Runs on every machine and sends nothing on the network: the NetworkTransform already replicates the
    /// position of each player, so each machine measures how fast an avatar moves on its own screen and
    /// feeds that speed to the Animator. Remote players animate exactly like they move.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerMovement))]
    public class PlayerAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        [Tooltip("Time (seconds) for the Speed parameter to catch up: smooths the change between idle and run.")]
        [SerializeField] private float _dampTime = 0.1f;
        [Tooltip("A move faster than this many times the top speed is a teleport (spawn, respawn): ignored.")]
        [SerializeField] private float _teleportFactor = 3f;

        private NetworkPlayer _player;
        private PlayerMovement _movement;
        private Vector3 _lastPosition;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _movement = GetComponent<PlayerMovement>();
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
        }

        // LateUpdate: after PlayerMovement (owner) and the NetworkTransform interpolation (others) moved the avatar.
        private void LateUpdate()
        {
            var position = transform.position;
            var delta = position - _lastPosition;
            _lastPosition = position;
            delta.y = 0f;

            var deltaTime = Time.deltaTime;
            var animator = _player.VisualAnimator;
            if (animator == null || deltaTime <= 0f || _movement.MaxSpeed <= 0f)
            {
                return;
            }

            // 0 = standing still, 1 = top speed.
            var speed = delta.magnitude / deltaTime / _movement.MaxSpeed;
            if (speed > _teleportFactor)
            {
                return;
            }
            animator.SetFloat(SpeedId, Mathf.Clamp01(speed), _dampTime, deltaTime);
        }
    }
}
