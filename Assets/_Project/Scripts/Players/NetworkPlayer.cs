using OverSmithed.Networking;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Networked avatar of one roster entry. Spawned by the server and owned by the machine of the player.
    /// On the owner machine, it is linked to the <see cref="LocalPlayerSlot"/> with the same local index.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        private static readonly Color[] PlayerColors =
        {
            new(0.90f, 0.30f, 0.25f),
            new(0.25f, 0.50f, 0.95f),
            new(0.35f, 0.80f, 0.35f),
            new(0.95f, 0.80f, 0.20f)
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Renderer _body;
        [SerializeField] private TextMesh _label;

        private readonly NetworkVariable<byte> _playerNumber = new();
        private readonly NetworkVariable<byte> _localIndex = new();
        private RosterEntry _pendingEntry;
        private MaterialPropertyBlock _propertyBlock;

        public byte PlayerNumber => _playerNumber.Value;
        public byte LocalIndex => _localIndex.Value;

        /// <summary>The local input slot driving this player. Only set on the owner machine.</summary>
        public LocalPlayerSlot LocalSlot { get; private set; }

        /// <summary>Server only, before spawning: the roster entry this avatar represents.</summary>
        public void Setup(RosterEntry entry)
        {
            _pendingEntry = entry;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // Written here rather than before Spawn(): the values are sent with the spawn message.
                _playerNumber.Value = _pendingEntry.PlayerNumber;
                _localIndex.Value = _pendingEntry.LocalIndex;
            }

            _playerNumber.OnValueChanged += HandleValueChanged;
            _localIndex.OnValueChanged += HandleValueChanged;
            Refresh();
        }

        public override void OnNetworkDespawn()
        {
            _playerNumber.OnValueChanged -= HandleValueChanged;
            _localIndex.OnValueChanged -= HandleValueChanged;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                _label.transform.rotation = cam.transform.rotation;
            }
        }

        private void HandleValueChanged(byte previous, byte current)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (IsOwner && LocalPlayersManager.Instance != null)
            {
                LocalSlot = LocalPlayersManager.Instance.FindSlot(LocalIndex);
            }

            name = $"NetworkPlayer {PlayerNumber} (client {OwnerClientId}, local {LocalIndex})";

            var color = PlayerNumber >= 1 && PlayerNumber <= PlayerColors.Length
                ? PlayerColors[PlayerNumber - 1]
                : Color.gray;
            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.SetColor(BaseColorId, color);
            _body.SetPropertyBlock(_propertyBlock);

            var ownerTag = IsOwner ? "\n<color=#FFD966>★ à toi</color>" : string.Empty;
            _label.text = $"Joueur {PlayerNumber}\n<size=24>client {OwnerClientId} · local {LocalIndex}</size>{ownerTag}";
        }
    }
}
