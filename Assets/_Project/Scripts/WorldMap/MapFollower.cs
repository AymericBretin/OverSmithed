using OverSmithed.Players;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// One follower of the world map walker (prefab Prefabs/WorldMap/MapFollower): a player who is not the leader,
    /// walking in single file behind it. Decoration only, created by <see cref="MapFollowers"/> on every machine (not a
    /// NetworkObject, no collider). It shows its player's skin and animates from the speed it moves at.
    /// </summary>
    [RequireComponent(typeof(MapWalkerMotion))]
    public class MapFollower : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        [Tooltip("The skin visual is created under this child.")]
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private float _animationDamp = 0.1f;
        [Tooltip("How fast it turns toward where it walks.")]
        [SerializeField] private float _turnSharpness = 15f;

        private GameObject _visual;
        private Animator _animator;
        private float _maxSpeed = 5f;
        private Vector3 _lastPosition;

        /// <summary>Skin index shown now (-1 = none).</summary>
        public int SkinIndex { get; private set; } = -1;

        public void Setup(SkinLibrary skins, int skinIndex, float maxSpeed, float visualScale, float jumpDelay)
        {
            _maxSpeed = maxSpeed;
            GetComponent<MapWalkerMotion>().Setup(maxSpeed, visualScale, jumpDelay);
            if (skinIndex == SkinIndex && _visual != null)
            {
                return;
            }
            if (_visual != null)
            {
                Destroy(_visual);
            }
            SkinIndex = skinIndex;
            var skin = skins != null ? skins.Get(skinIndex) : null;
            if (skin == null || skin.Prefab == null)
            {
                Debug.LogWarning($"[MapFollower] No skin at index {skinIndex}.", this);
                return;
            }
            var root = _visualRoot != null ? _visualRoot : transform;
            root.localScale = Vector3.one * visualScale;
            _visual = Instantiate(skin.Prefab, root, false);
            _animator = _visual.GetComponentInChildren<Animator>();
        }

        /// <summary>Puts it there at once (start of the map, after a teleport of the leader).</summary>
        public void Snap(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            _lastPosition = position;
        }

        /// <summary>Moves it to its spot in the line this frame; it turns toward <paramref name="lookAt"/>.</summary>
        public void MoveTo(Vector3 position, Vector3 lookAt, float deltaTime)
        {
            transform.position = position;
            var direction = lookAt - position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0025f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction),
                    1f - Mathf.Exp(-_turnSharpness * deltaTime));
            }
        }

        // After MapFollowers moved it (it runs in the leader's LateUpdate, this one in its own).
        private void LateUpdate()
        {
            var deltaTime = Time.deltaTime;
            var position = transform.position;
            var delta = position - _lastPosition;
            delta.y = 0f;
            _lastPosition = position;
            if (_animator == null || deltaTime <= 0f)
            {
                return;
            }
            var speed = delta.magnitude / deltaTime / Mathf.Max(0.01f, _maxSpeed);
            if (speed > 3f)
            {
                return; // snapped
            }
            _animator.SetFloat(SpeedId, Mathf.Clamp01(speed), _animationDamp, deltaTime);
        }
    }
}
