using OverSmithed.Items;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Drives the animations of the skin visual: idle / walk / run, and the carry pose (arms forward).
    /// Runs on every machine and sends nothing on the network: the NetworkTransform already replicates the
    /// position of each player, so each machine measures how fast an avatar moves on its own screen and
    /// feeds that speed to the Animator. Remote players animate exactly like they move.
    /// The carry pose is the "Carry" layer of the controller (arms only, legs keep running): its weight goes
    /// to 1 while the player holds an item (<see cref="PlayerHands.HeldItem"/>, already known on every machine).
    /// The carried item's visual also follows the hands up and down (they move with the body when walking,
    /// running, breathing). Only the visual child moves, on each machine: the item itself stays where the host put it.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerMovement), typeof(PlayerHands))]
    public class PlayerAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private const string CarryLayerName = "Carry";
        private const string LeftHandPath = "Rig_Medium/root/hips/spine/chest/upperarm.l/lowerarm.l/wrist.l/hand.l";
        private const string RightHandPath = "Rig_Medium/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r/hand.r";

        [Tooltip("Time (seconds) for the Speed parameter to catch up: smooths the change between idle and run.")]
        [SerializeField] private float _dampTime = 0.1f;
        [Tooltip("A move faster than this many times the top speed is a teleport (spawn, respawn): ignored.")]
        [SerializeField] private float _teleportFactor = 3f;
        [Tooltip("Time (seconds) for the arms to go up into the carry pose, or back down.")]
        [SerializeField] private float _carryBlendTime = 0.15f;
        [Tooltip("How much the carried item follows the hands up and down: 0 = it stays still, 1 = exactly like the hands.")]
        [SerializeField, Range(0f, 1.5f)] private float _itemFollow = 1f;
        [Tooltip("Height (m, from the feet) of the middle of the hands in the carry pose when standing still " +
                 "(average over the idle animation). At this height the item is at its normal place. " +
                 "Measure it again if the carry pose changes.")]
        [SerializeField] private float _handsRestHeight = 0.8f;

        private NetworkPlayer _player;
        private PlayerMovement _movement;
        private PlayerHands _hands;
        private Vector3 _lastPosition;

        // The skin visual (and so its Animator) can change: the layer index and the hands are looked up again then.
        private Animator _carryAnimator;
        private int _carryLayer = -1;
        private Transform _leftHand;
        private Transform _rightHand;

        // The visual child of the carried item that this machine moves, and where it was before.
        private Transform _followedVisual;
        private Vector3 _followedVisualRest;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _movement = GetComponent<PlayerMovement>();
            _hands = GetComponent<PlayerHands>();
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
        }

        private void OnDisable()
        {
            StopFollowing();
        }

        // LateUpdate: after PlayerMovement (owner) and the NetworkTransform interpolation (others) moved the avatar,
        // and after the Animator posed the bones of this frame (the hands are where they are drawn).
        private void LateUpdate()
        {
            var position = transform.position;
            var delta = position - _lastPosition;
            _lastPosition = position;
            delta.y = 0f;

            var deltaTime = Time.deltaTime;
            var animator = _player.VisualAnimator;
            if (animator == null || deltaTime <= 0f)
            {
                return;
            }

            UpdateCarry(animator, deltaTime);

            if (_movement.MaxSpeed <= 0f)
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

        private void UpdateCarry(Animator animator, float deltaTime)
        {
            var newAnimator = animator != _carryAnimator;
            if (newAnimator)
            {
                _carryAnimator = animator;
                _carryLayer = animator.GetLayerIndex(CarryLayerName);
                _leftHand = animator.transform.Find(LeftHandPath);
                _rightHand = animator.transform.Find(RightHandPath);
            }
            if (_carryLayer < 0)
            {
                StopFollowing();
                return;
            }

            // IsSpawned first: the held item is a network reference, only valid while the player is spawned.
            var heldItem = _hands.IsSpawned ? _hands.HeldItem : null;
            var target = heldItem != null ? 1f : 0f;
            // A new skin visual starts directly in the right pose (no arms going up after a skin change).
            var weight = newAnimator || _carryBlendTime <= 0f
                ? target
                : Mathf.MoveTowards(animator.GetLayerWeight(_carryLayer), target, deltaTime / _carryBlendTime);
            animator.SetLayerWeight(_carryLayer, weight);

            FollowHands(heldItem, weight);
        }

        // Moves the carried item's visual up and down with the hands. The weight makes it start and stop softly
        // while the arms go up.
        private void FollowHands(NetworkItem heldItem, float carryWeight)
        {
            var visual = heldItem != null ? heldItem.VisualRoot : null;
            if (visual != _followedVisual)
            {
                StopFollowing();
                if (visual != null)
                {
                    _followedVisual = visual;
                    _followedVisualRest = visual.localPosition;
                }
            }
            if (_followedVisual == null || _leftHand == null || _rightHand == null)
            {
                return;
            }

            var hands = transform.InverseTransformPoint((_leftHand.position + _rightHand.position) * 0.5f);
            var lift = (hands.y - _handsRestHeight) * _itemFollow * carryWeight;
            var worldLift = transform.TransformVector(0f, lift, 0f);
            _followedVisual.localPosition = _followedVisualRest + _followedVisual.parent.InverseTransformVector(worldLift);
        }

        // The item left the hands (or this player is gone): its visual goes back to its normal place.
        private void StopFollowing()
        {
            if (_followedVisual != null)
            {
                _followedVisual.localPosition = _followedVisualRest;
            }
            _followedVisual = null;
        }
    }
}
