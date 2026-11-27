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
    /// running, breathing), through <see cref="ItemVisualMotion.HandsLift"/>: the item itself stays where the host
    /// put it. Taking or putting down an item adds a short "reach" of the arms (down, then back); throwing one
    /// a quick swing up (<see cref="PlayerHands.Thrown"/>).
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerMovement), typeof(PlayerHands))]
    public class PlayerAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private const string CarryLayerName = "Carry";
        private const string LeftHandPath = "Rig_Medium/root/hips/spine/chest/upperarm.l/lowerarm.l/wrist.l/hand.l";
        private const string RightHandPath = "Rig_Medium/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r/hand.r";
        private const string LeftUpperArmPath = "Rig_Medium/root/hips/spine/chest/upperarm.l";
        private const string RightUpperArmPath = "Rig_Medium/root/hips/spine/chest/upperarm.r";

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
        [Tooltip("How far (degrees) the arms reach down when taking or putting down an item.")]
        [SerializeField] private float _reachAngle = 35f;
        [Tooltip("Time (seconds) of the reach. When putting down, the arms stay up this long, then go down.")]
        [SerializeField] private float _reachTime = 0.25f;
        [Tooltip("How far (degrees) the arms swing up when throwing an item.")]
        [SerializeField] private float _throwAngle = 55f;
        [Tooltip("Time (seconds) of the throw swing. The arms stay forward this long, then go down.")]
        [SerializeField] private float _throwTime = 0.3f;

        private NetworkPlayer _player;
        private PlayerMovement _movement;
        private PlayerHands _hands;
        private PlayerHammerAnimation _hammerAnimation;
        private Vector3 _lastPosition;

        // The skin visual (and so its Animator) can change: the layer index and the hands are looked up again then.
        private Animator _carryAnimator;
        private int _carryLayer = -1;
        private Transform _leftHand;
        private Transform _rightHand;
        private Transform _leftUpperArm;
        private Transform _rightUpperArm;

        // The item this machine saw in the hands last frame: a change starts a reach.
        private NetworkItem _lastHeldItem;
        // The arm gesture playing now: a reach (down) when taking / putting down, a swing (up) when throwing.
        private float _gestureElapsed = float.MaxValue;
        private float _gestureTime;
        private float _gestureAngle;
        private bool _gestureEaseOut;
        private float _lastThrowTime = float.MinValue;

        // The carried item whose visual follows the hands.
        private ItemVisualMotion _followedMotion;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _movement = GetComponent<PlayerMovement>();
            _hands = GetComponent<PlayerHands>();
            _hammerAnimation = GetComponent<PlayerHammerAnimation>();
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
            _hands.Thrown += HandleThrown;
        }

        private void OnDisable()
        {
            _hands.Thrown -= HandleThrown;
            StopFollowing();
        }

        // Every machine hears it (host RPC): the arms swing up instead of the usual reach down.
        private void HandleThrown()
        {
            _lastThrowTime = Time.time;
            StartGesture(-_throwAngle, _throwTime, easeOut: true);
        }

        private void StartGesture(float angle, float time, bool easeOut)
        {
            _gestureAngle = angle;
            _gestureTime = time;
            _gestureEaseOut = easeOut;
            _gestureElapsed = 0f;
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
                _leftUpperArm = animator.transform.Find(LeftUpperArmPath);
                _rightUpperArm = animator.transform.Find(RightUpperArmPath);
            }
            if (_carryLayer < 0)
            {
                StopFollowing();
                return;
            }

            // The full-body work layer (hammer, saw) owns the arms. A pending put-down reach must not bend its strike.
            if (_hammerAnimation != null && _hammerAnimation.IsWorking)
            {
                animator.SetLayerWeight(_carryLayer, 0f);
                _gestureElapsed = float.MaxValue;
                _lastHeldItem = null;
                StopFollowing();
                return;
            }

            // IsSpawned first: the held item is a network reference, only valid while the player is spawned.
            var heldItem = _hands.IsSpawned ? _hands.HeldItem : null;
            if (heldItem != _lastHeldItem)
            {
                // Taken or put down (seen the same way on every machine): reach, unless the skin just changed.
                // A thrown item already has its swing (the throw message may come just before or after this).
                var justThrown = heldItem == null && Time.time - _lastThrowTime < _throwTime;
                if (newAnimator)
                {
                    _gestureElapsed = float.MaxValue;
                }
                else if (!justThrown)
                {
                    StartGesture(_reachAngle, _reachTime, easeOut: false);
                }
                _lastHeldItem = heldItem;
            }
            var gesturing = _gestureElapsed < _gestureTime;

            // The weight this frame's pose was made with (a new weight is used from the next frame).
            var shownWeight = animator.GetLayerWeight(_carryLayer);
            // After putting down or throwing, the arms stay up during the gesture, then go down.
            var target = heldItem != null || gesturing ? 1f : 0f;
            // A new skin visual starts directly in the right pose (no arms going up after a skin change).
            var weight = newAnimator || _carryBlendTime <= 0f
                ? target
                : Mathf.MoveTowards(shownWeight, target, deltaTime / _carryBlendTime);
            animator.SetLayerWeight(_carryLayer, weight);

            if (gesturing)
            {
                var t = Mathf.Clamp01(_gestureElapsed / _gestureTime);
                // The throw goes up fast and comes back slower.
                if (_gestureEaseOut)
                {
                    t = 1f - (1f - t) * (1f - t);
                }
                TurnArms(_gestureAngle * Mathf.Sin(Mathf.PI * t) * shownWeight);
                _gestureElapsed += deltaTime;
            }
            FollowHands(heldItem, shownWeight);
        }

        // Turns both upper arms around the shoulders, on top of the animation (degrees: + = down, - = up).
        // Scaled by the carry weight by the caller: it only makes sense with the arms forward.
        private void TurnArms(float angle)
        {
            if (_leftUpperArm == null || _rightUpperArm == null)
            {
                return;
            }
            var turn = Quaternion.AngleAxis(angle, transform.right);
            _leftUpperArm.rotation = turn * _leftUpperArm.rotation;
            _rightUpperArm.rotation = turn * _rightUpperArm.rotation;
        }

        // Moves the carried item's visual up and down with the hands (after the reach: the item dips with them).
        // The weight makes it start and stop softly while the arms go up.
        private void FollowHands(NetworkItem heldItem, float carryWeight)
        {
            var motion = heldItem != null ? heldItem.GetComponent<ItemVisualMotion>() : null;
            if (motion != _followedMotion)
            {
                StopFollowing();
                _followedMotion = motion;
            }
            if (_followedMotion == null || _leftHand == null || _rightHand == null)
            {
                return;
            }

            var hands = transform.InverseTransformPoint((_leftHand.position + _rightHand.position) * 0.5f);
            var lift = (hands.y - _handsRestHeight) * _itemFollow * carryWeight;
            _followedMotion.HandsLift = transform.TransformVector(0f, lift, 0f).y;
        }

        // The item left the hands (or this player is gone): its visual goes back to its normal height.
        private void StopFollowing()
        {
            if (_followedMotion != null)
            {
                _followedMotion.HandsLift = 0f;
            }
            _followedMotion = null;
        }
    }
}
