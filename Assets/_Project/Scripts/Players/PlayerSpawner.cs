using System.Collections.Generic;
using OverSmithed.Networking;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Server only. Keeps one <see cref="NetworkPlayer"/> per roster entry in the current scene,
    /// owned by the machine of that player. Avatars belong to the scene and are destroyed with it.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkPlayer _playerPrefab;
        [SerializeField] private Transform[] _spawnPoints;

        private readonly Dictionary<(ulong clientId, byte localIndex), NetworkPlayer> _players = new();
        private SessionRoster _roster;

        private void Start()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                enabled = false;
                return;
            }

            _roster = SessionRoster.Instance;
            _roster.Changed += Synchronize;
            Synchronize();
        }

        private void OnDestroy()
        {
            if (_roster != null)
            {
                _roster.Changed -= Synchronize;
            }
        }

        private void Synchronize()
        {
            // Despawn avatars whose entry left the roster.
            var stale = new List<(ulong, byte)>();
            foreach (var pair in _players)
            {
                if (pair.Value == null || !_roster.TryGetEntry(pair.Key.clientId, pair.Key.localIndex, out _))
                {
                    stale.Add(pair.Key);
                }
            }
            foreach (var key in stale)
            {
                var player = _players[key];
                // Already gone if NGO despawned it with its disconnected owner.
                if (player != null && player.IsSpawned)
                {
                    player.NetworkObject.Despawn();
                }
                _players.Remove(key);
            }

            // Spawn avatars for new entries.
            for (var i = 0; i < _roster.Count; i++)
            {
                var entry = _roster[i];
                var key = (entry.ClientId, entry.LocalIndex);
                if (!_players.ContainsKey(key))
                {
                    _players[key] = Spawn(entry);
                }
            }
        }

        private NetworkPlayer Spawn(RosterEntry entry)
        {
            var point = _spawnPoints[(entry.PlayerNumber - 1) % _spawnPoints.Length];
            var player = Instantiate(_playerPrefab, point.position, point.rotation);
            player.Setup(entry);
            player.NetworkObject.SpawnWithOwnership(entry.ClientId, destroyWithScene: true);
            return player;
        }
    }
}
