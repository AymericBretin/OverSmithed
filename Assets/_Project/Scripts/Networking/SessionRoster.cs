using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Networking
{
    /// <summary>
    /// Source of truth for who plays in the session. Spawned once by the host and kept alive across
    /// networked scene loads. Only the server writes to it; clients send join/leave requests through RPCs.
    /// </summary>
    public class SessionRoster : NetworkBehaviour
    {
        public static SessionRoster Instance { get; private set; }

        /// <summary>Raised on every machine when the roster is spawned (instance) or despawned (null).</summary>
        public static event Action<SessionRoster> InstanceChanged;

        /// <summary>Raised whenever the list of players or the lock state changes.</summary>
        public event Action Changed;

        /// <summary>Raised on the requesting machine only, when the host refuses one of its local players.</summary>
        public event Action<byte, string> JoinRejected;

        private NetworkList<RosterEntry> _entries;
        private readonly NetworkVariable<bool> _isLocked = new();

        public int Count => _entries.Count;
        public RosterEntry this[int index] => _entries[index];

        /// <summary>True once the host launched the game: nobody can join anymore.</summary>
        public bool IsLocked => _isLocked.Value;

        // Domain reload is disabled on entering Play Mode: statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            InstanceChanged = null;
        }

        private void Awake()
        {
            // NetworkList must be created before the object is spawned.
            _entries = new NetworkList<RosterEntry>();
        }

        public override void OnNetworkSpawn()
        {
            _entries.OnListChanged += HandleListChanged;
            _isLocked.OnValueChanged += HandleLockChanged;
            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            }

            Instance = this;
            InstanceChanged?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            _entries.OnListChanged -= HandleListChanged;
            _isLocked.OnValueChanged -= HandleLockChanged;
            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (Instance == this)
            {
                Instance = null;
                InstanceChanged?.Invoke(null);
            }
        }

        public bool TryGetEntry(ulong clientId, byte localIndex, out RosterEntry entry)
        {
            var index = IndexOf(clientId, localIndex);
            entry = index >= 0 ? _entries[index] : default;
            return index >= 0;
        }

        // ---------- Requests (any machine, including the host) ----------

        public void RequestJoin(byte localIndex, string deviceLabel)
        {
            RequestJoinRpc(localIndex, new FixedString32Bytes(deviceLabel));
        }

        public void RequestLeave(byte localIndex)
        {
            RequestLeaveRpc(localIndex);
        }

        // ---------- Server only ----------

        public void SetLocked(bool locked)
        {
            if (IsServer)
            {
                _isLocked.Value = locked;
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestJoinRpc(byte localIndex, FixedString32Bytes deviceLabel, RpcParams rpcParams = default)
        {
            var clientId = rpcParams.Receive.SenderClientId;
            if (IndexOf(clientId, localIndex) >= 0)
            {
                return;
            }

            string refusal = null;
            if (_isLocked.Value)
            {
                refusal = "La partie a déjà commencé.";
            }
            else if (_entries.Count >= NetworkConstants.MaxPlayers)
            {
                refusal = $"La partie est pleine ({NetworkConstants.MaxPlayers} joueurs).";
            }

            if (refusal != null)
            {
                JoinRejectedRpc(localIndex, refusal, RpcTarget.Single(clientId, RpcTargetUse.Temp));
                return;
            }

            _entries.Add(new RosterEntry
            {
                ClientId = clientId,
                LocalIndex = localIndex,
                PlayerNumber = FirstFreePlayerNumber(),
                DeviceLabel = deviceLabel
            });
        }

        [Rpc(SendTo.Server)]
        private void RequestLeaveRpc(byte localIndex, RpcParams rpcParams = default)
        {
            var index = IndexOf(rpcParams.Receive.SenderClientId, localIndex);
            if (index >= 0)
            {
                _entries.RemoveAt(index);
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void JoinRejectedRpc(byte localIndex, FixedString64Bytes reason, RpcParams rpcParams)
        {
            JoinRejected?.Invoke(localIndex, reason.ToString());
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].ClientId == clientId)
                {
                    _entries.RemoveAt(i);
                }
            }
        }

        private byte FirstFreePlayerNumber()
        {
            for (byte number = 1; number <= NetworkConstants.MaxPlayers; number++)
            {
                var used = false;
                foreach (var entry in _entries)
                {
                    used |= entry.PlayerNumber == number;
                }
                if (!used)
                {
                    return number;
                }
            }
            return 0;
        }

        private int IndexOf(ulong clientId, byte localIndex)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Matches(clientId, localIndex))
                {
                    return i;
                }
            }
            return -1;
        }

        private void HandleListChanged(NetworkListEvent<RosterEntry> change)
        {
            Changed?.Invoke();
        }

        private void HandleLockChanged(bool previous, bool current)
        {
            Changed?.Invoke();
        }
    }
}
