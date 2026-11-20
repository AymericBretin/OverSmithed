using OverSmithed.Core;
using OverSmithed.Networking;
using OverSmithed.Players;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// The small character that walks on the world map (one per session, placed in the WorldMap scene).
    /// The host owns it: every joined player of the host machine moves it with Move (their sticks are added), the host
    /// moves it (CharacterController: walls, water and decor stop it) and the NetworkTransform (server authority) sends
    /// the position to the clients, who only watch. It looks like the leader (first player of the host): only the skin
    /// index travels on the network, every machine creates the same visual and animates it from the speed it sees.
    /// It starts on the spot of the last level played (else level 1).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class MapWalker : NetworkBehaviour
    {
        private const string MoveActionName = "Move";
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        [SerializeField] private SkinLibrary _skins;
        [Tooltip("The skin visual is created under this child.")]
        [SerializeField] private Transform _visualRoot;
        [Tooltip("Scale of the skin visual (the skins are ~2 m tall at scale 1 of this root).")]
        [SerializeField] private float _visualScale = 0.6f;

        [Header("Movement (host)")]
        [SerializeField] private float _speed = 5f;
        [SerializeField] private float _accelerationTime = 0.1f;
        [SerializeField] private float _stopTime = 0.12f;
        [SerializeField] private float _turnSharpness = 15f;
        [SerializeField] private float _gravity = 30f;

        [Header("Animation (every machine)")]
        [SerializeField] private float _animationDamp = 0.1f;

        private readonly NetworkVariable<byte> _skinIndex = new();
        private CharacterController _controller;
        private Vector3 _velocity;
        private float _fallSpeed;
        private GameObject _visual;
        private Animator _animator;
        private int _visualSkin = -1;
        private Vector3 _lastPosition;

        /// <summary>The walker of the loaded world map (null when there is none).</summary>
        public static MapWalker Current { get; private set; }

        /// <summary>Host only: horizontal speed of the last move.</summary>
        public Vector3 Velocity => _velocity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            Current = this;
            _skinIndex.OnValueChanged += HandleSkinChanged;
            // Only the host simulates; on clients the NetworkTransform moves it and the controller must not push it.
            _controller.enabled = IsServer;
            if (IsServer)
            {
                PlaceOnStartSpot();
                RefreshLeaderSkin();
                if (SessionRoster.Instance != null)
                {
                    SessionRoster.Instance.Changed += RefreshLeaderSkin;
                }
            }
            ShowSkin();
            _lastPosition = transform.position;
        }

        public override void OnNetworkDespawn()
        {
            _skinIndex.OnValueChanged -= HandleSkinChanged;
            if (SessionRoster.Instance != null)
            {
                SessionRoster.Instance.Changed -= RefreshLeaderSkin;
            }
            if (Current == this)
            {
                Current = null;
            }
        }

        private void Update()
        {
            if (IsSpawned && IsServer)
            {
                Move(ReadHostInput(), Time.deltaTime);
            }
        }

        private void LateUpdate()
        {
            Animate(Time.deltaTime);
        }

        // ---------- Host: start spot, leader skin, movement ----------

        private void PlaceOnStartSpot()
        {
            LevelNode start = null;
            foreach (var node in LevelNode.All)
            {
                if (!string.IsNullOrEmpty(SceneFlow.LastLevelScene) && node.SceneName == SceneFlow.LastLevelScene)
                {
                    start = node;
                    break;
                }
                if (node.LevelNumber == 1)
                {
                    start = node;
                }
            }
            if (start == null)
            {
                return;
            }
            var position = start.transform.position;
            var rotation = Quaternion.Euler(0f, 180f, 0f); // facing the camera
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
        }

        // The leader = the first player of the host machine (join order of the roster).
        private void RefreshLeaderSkin()
        {
            var roster = SessionRoster.Instance;
            if (roster == null || !IsServer)
            {
                return;
            }
            for (var i = 0; i < roster.Count; i++)
            {
                if (roster[i].ClientId == NetworkManager.LocalClientId)
                {
                    _skinIndex.Value = roster[i].SkinIndex;
                    return;
                }
            }
        }

        // Every joined player of this (host) machine can move the walker: the sticks are added.
        private static Vector2 ReadHostInput()
        {
            var players = LocalPlayersManager.Instance;
            if (players == null)
            {
                return Vector2.zero;
            }
            var sum = Vector2.zero;
            foreach (var slot in players.Slots)
            {
                if (slot == null || slot.State != LocalPlayerSlot.SlotState.Joined)
                {
                    continue;
                }
                var move = slot.Input.actions.FindAction(MoveActionName);
                if (move != null)
                {
                    sum += move.ReadValue<Vector2>();
                }
            }
            return Vector2.ClampMagnitude(sum, 1f);
        }

        private void Move(Vector2 input, float deltaTime)
        {
            if (!_controller.enabled || deltaTime <= 0f)
            {
                return;
            }
            // Fixed camera without yaw: the stick maps directly onto the ground.
            var direction = new Vector3(input.x, 0f, input.y);
            var wanted = direction * _speed;
            var speedingUp = wanted.sqrMagnitude >= _velocity.sqrMagnitude;
            var rate = _speed / Mathf.Max(0.001f, speedingUp ? _accelerationTime : _stopTime);
            _velocity = Vector3.MoveTowards(_velocity, wanted, rate * deltaTime);

            if (_controller.isGrounded && _fallSpeed < 0f)
            {
                _fallSpeed = -2f;
            }
            _fallSpeed = Mathf.Max(_fallSpeed - _gravity * deltaTime, -25f);
            _controller.Move((_velocity + Vector3.up * _fallSpeed) * deltaTime);
            var moved = _controller.velocity;
            moved.y = 0f;
            // Against a wall the speed drops too (no "pushing" for a moment after).
            if (moved.sqrMagnitude < _velocity.sqrMagnitude)
            {
                _velocity = moved;
            }

            if (direction.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction),
                    1f - Mathf.Exp(-_turnSharpness * deltaTime));
            }
        }

        // ---------- Every machine: visual and animation ----------

        private void HandleSkinChanged(byte previous, byte current)
        {
            ShowSkin();
        }

        private void ShowSkin()
        {
            if (_visual != null && _visualSkin == _skinIndex.Value)
            {
                return;
            }
            if (_visual != null)
            {
                Destroy(_visual);
            }
            _visualSkin = _skinIndex.Value;
            var skin = _skins != null ? _skins.Get(_skinIndex.Value) : null;
            if (skin == null || skin.Prefab == null)
            {
                Debug.LogWarning($"[MapWalker] No skin at index {_skinIndex.Value}.", this);
                return;
            }
            var root = _visualRoot != null ? _visualRoot : transform;
            root.localScale = Vector3.one * _visualScale;
            _visual = Instantiate(skin.Prefab, root, false);
            _animator = _visual.GetComponentInChildren<Animator>();
        }

        // Speed measured from the position: works the same on the host and on clients (no network data).
        private void Animate(float deltaTime)
        {
            var position = transform.position;
            var delta = position - _lastPosition;
            delta.y = 0f;
            _lastPosition = position;
            if (_animator == null || deltaTime <= 0f)
            {
                return;
            }
            var speed = delta.magnitude / deltaTime / Mathf.Max(0.01f, _speed);
            if (speed > 3f)
            {
                return; // teleport
            }
            _animator.SetFloat(SpeedId, Mathf.Clamp01(speed), _animationDamp, deltaTime);
        }
    }
}
