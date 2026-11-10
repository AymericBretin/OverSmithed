using OverSmithed.Networking;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Networked avatar of one roster entry. Spawned by the server and owned by the machine of the player.
    /// On the owner machine, it is linked to the <see cref="LocalPlayerSlot"/> with the same local index.
    /// Its look is a skin from the <see cref="SkinLibrary"/>: only the skin index travels on the network, and
    /// every machine creates the same visual locally.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private SkinLibrary _skins;
        [Tooltip("The skin's visual is created under this transform.")]
        [SerializeField] private Transform _visualRoot;

        private readonly NetworkVariable<byte> _playerNumber = new();
        private readonly NetworkVariable<byte> _localIndex = new();
        private readonly NetworkVariable<byte> _skinIndex = new();

        // Game rule decided by the host: whether players may move in the current scene.
        private readonly NetworkVariable<bool> _canMove = new();

        // Written by the owner machine: only it knows whether its device is plugged in.
        private readonly NetworkVariable<bool> _inputConnected = new(true,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private RosterEntry _pendingEntry;
        private bool _pendingCanMove;
        private GameObject _visual;
        private int _visualSkinIndex = -1;

        public byte PlayerNumber => _playerNumber.Value;
        public byte LocalIndex => _localIndex.Value;
        public byte SkinIndex => _skinIndex.Value;
        public bool CanMove => _canMove.Value;

        /// <summary>False while the device of this player is unplugged (known on every machine).</summary>
        public bool IsInputConnected => _inputConnected.Value;

        /// <summary>The local input slot driving this player. Only set on the owner machine.</summary>
        public LocalPlayerSlot LocalSlot { get; private set; }

        /// <summary>Animator of the current skin visual (null while there is no visual). Exists on every machine.</summary>
        public Animator VisualAnimator { get; private set; }

        /// <summary>Server only, before spawning: the roster entry this avatar represents.</summary>
        public void Setup(RosterEntry entry, bool canMove)
        {
            _pendingEntry = entry;
            _pendingCanMove = canMove;
        }

        /// <summary>Server only: changes the skin of a spawned avatar (for example after a choice in the lobby).</summary>
        public void SetSkin(byte skinIndex)
        {
            if (IsServer && IsSpawned)
            {
                _skinIndex.Value = skinIndex;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // Written here rather than before Spawn(): the values are sent with the spawn message.
                _playerNumber.Value = _pendingEntry.PlayerNumber;
                _localIndex.Value = _pendingEntry.LocalIndex;
                _skinIndex.Value = _pendingEntry.SkinIndex;
                _canMove.Value = _pendingCanMove;
            }

            _playerNumber.OnValueChanged += HandleValueChanged;
            _localIndex.OnValueChanged += HandleValueChanged;
            _skinIndex.OnValueChanged += HandleValueChanged;
            Refresh();
        }

        public override void OnNetworkDespawn()
        {
            _playerNumber.OnValueChanged -= HandleValueChanged;
            _localIndex.OnValueChanged -= HandleValueChanged;
            _skinIndex.OnValueChanged -= HandleValueChanged;
            BindSlot(null);
        }

        private void HandleValueChanged(byte previous, byte current)
        {
            Refresh();
        }

        private void BindSlot(LocalPlayerSlot slot)
        {
            if (LocalSlot == slot)
            {
                return;
            }
            if (LocalSlot != null)
            {
                LocalSlot.DeviceLost -= HandleSlotDeviceChanged;
                LocalSlot.DeviceRegained -= HandleSlotDeviceChanged;
            }
            LocalSlot = slot;
            if (LocalSlot != null)
            {
                LocalSlot.DeviceLost += HandleSlotDeviceChanged;
                LocalSlot.DeviceRegained += HandleSlotDeviceChanged;
                HandleSlotDeviceChanged(LocalSlot);
            }
        }

        // Owner only: publish the device state of the local slot to every machine.
        private void HandleSlotDeviceChanged(LocalPlayerSlot slot)
        {
            if (IsSpawned && IsOwner)
            {
                _inputConnected.Value = slot.IsDeviceConnected;
            }
        }

        private void Refresh()
        {
            if (IsOwner && LocalPlayersManager.Instance != null)
            {
                BindSlot(LocalPlayersManager.Instance.FindSlot(LocalIndex));
            }

            var skin = _skins != null ? _skins.Get(SkinIndex) : null;
            name = $"NetworkPlayer {PlayerNumber} ({(skin != null ? skin.DisplayName : "no skin")}, client {OwnerClientId}, local {LocalIndex})";
            ShowSkin(skin);
        }

        private void ShowSkin(SkinDefinition skin)
        {
            if (_visual != null && _visualSkinIndex == SkinIndex)
            {
                return;
            }
            if (_visual != null)
            {
                Destroy(_visual);
                _visual = null;
                VisualAnimator = null;
            }
            _visualSkinIndex = SkinIndex;
            if (skin == null || skin.Prefab == null)
            {
                Debug.LogWarning($"[NetworkPlayer] No skin at index {SkinIndex}.", this);
                return;
            }
            _visual = Instantiate(skin.Prefab, _visualRoot != null ? _visualRoot : transform, false);
            VisualAnimator = _visual.GetComponentInChildren<Animator>();
        }
    }
}
