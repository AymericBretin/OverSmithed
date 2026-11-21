using System;
using System.Collections;
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
    /// Every machine knows which level spot it stands on (<see cref="CurrentNode"/>, from the position it sees). On
    /// the host, Interact on a built level (<see cref="TryEnterLevel"/>) tells everyone (<see cref="LevelLaunching"/>:
    /// jump, burst, fade), walks it to the center of the circle, then loads the level for everyone (FLOW-5).
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

        [Header("Entering a level (host)")]
        [Tooltip("Seconds between Interact and the level load (time for the jump, the burst and the fade).")]
        [SerializeField, Min(0f)] private float _launchDelay = 1.1f;
        [Tooltip("Speed (m/s) at which the walker goes to the center of the circle after Interact.")]
        [SerializeField, Min(0.1f)] private float _centerSpeed = 3f;

        private readonly NetworkVariable<byte> _skinIndex = new();
        private CharacterController _controller;
        private Vector3 _velocity;
        private float _fallSpeed;
        private GameObject _visual;
        private Animator _animator;
        private int _visualSkin = -1;
        private Vector3 _lastPosition;
        private LevelNode _launchNode;

        /// <summary>The walker of the loaded world map (null when there is none).</summary>
        public static MapWalker Current { get; private set; }

        /// <summary>
        /// Every machine: a level is starting from this spot (jump, burst, fade). Raised once per launch, before the
        /// host loads the level. Null = the start failed and was cancelled (undo the fade).
        /// </summary>
        public static event Action<LevelNode> LevelLaunching;

        /// <summary>Every machine: the walker arrived on a spot or left it (null = no spot).</summary>
        public event Action<LevelNode> NodeChanged;

        /// <summary>Every machine: the level spot the walker stands on (from the position this machine sees), or null.</summary>
        public LevelNode CurrentNode { get; private set; }

        /// <summary>Every machine: a level is starting (the walker no longer moves).</summary>
        public bool IsLaunching => _launchNode != null;

        /// <summary>Host only: horizontal speed of the last move.</summary>
        public Vector3 Velocity => _velocity;

        /// <summary>Top walking speed (m/s).</summary>
        public float MaxSpeed => _speed;

        /// <summary>Scale of the skin visual root (the visual motion keeps it).</summary>
        public float VisualScale => _visualScale;

        /// <summary>The skins (followers use the same ones).</summary>
        public SkinLibrary Skins => _skins;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            LevelLaunching = null;
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
            if (!IsSpawned || !IsServer)
            {
                return;
            }
            if (_launchNode != null)
            {
                MoveToCenter(_launchNode, Time.deltaTime);
                return;
            }
            // The pause menu of this machine is open (or was just closed by this press): the players don't walk.
            var input = GamePause.BlocksPlayerInput ? Vector2.zero : ReadHostInput();
            Move(input, Time.deltaTime);
        }

        private void LateUpdate()
        {
            RefreshCurrentNode();
            Animate(Time.deltaTime);
        }

        private void RefreshCurrentNode()
        {
            var node = LevelNode.FindAt(transform.position);
            if (node == CurrentNode)
            {
                return;
            }
            CurrentNode = node;
            NodeChanged?.Invoke(node);
        }

        // ---------- Entering a level ----------

        /// <summary>
        /// Host only: starts the level of the spot the walker stands on, for everyone. False (nothing happens) away
        /// from a spot, on a level that is not built yet, or when a level is already starting.
        /// </summary>
        public bool TryEnterLevel()
        {
            if (!IsSpawned || !IsServer || _launchNode != null)
            {
                return false;
            }
            var node = LevelNode.FindAt(transform.position);
            if (node == null || !node.HasLevel)
            {
                return false;
            }
            // Only the level number travels: every machine has the same map, so it finds the same spot.
            LevelLaunchingRpc(node.LevelNumber);
            StartCoroutine(LoadLevelAfterDelay(node));
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LevelLaunchingRpc(int levelNumber)
        {
            var node = LevelNode.FindByNumber(levelNumber);
            if (node == null)
            {
                Debug.LogWarning($"[MapWalker] No level spot number {levelNumber} on this map.", this);
                return;
            }
            _launchNode = node;
            LevelLaunching?.Invoke(node);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LevelLaunchCancelledRpc()
        {
            _launchNode = null;
            LevelLaunching?.Invoke(null);
        }

        private IEnumerator LoadLevelAfterDelay(LevelNode node)
        {
            _velocity = Vector3.zero;
            // Scaled time: a local pause opened meanwhile also holds the load (a scene must not load with time stopped).
            yield return new WaitForSeconds(_launchDelay);
            if (!SceneFlow.StartLevel(node.SceneName))
            {
                Debug.LogWarning($"[MapWalker] Could not start {node.SceneName}.", this);
                LevelLaunchCancelledRpc();
            }
        }

        // While the level starts: walk to the center of the circle and turn toward the camera.
        private void MoveToCenter(LevelNode node, float deltaTime)
        {
            if (!_controller.enabled || deltaTime <= 0f)
            {
                return;
            }
            var offset = node.transform.position - transform.position;
            offset.y = 0f;
            var step = Vector3.ClampMagnitude(offset, _centerSpeed * deltaTime);
            _controller.Move(step + Vector3.down * 2f * deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, 180f, 0f),
                1f - Mathf.Exp(-_turnSharpness * deltaTime));
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
