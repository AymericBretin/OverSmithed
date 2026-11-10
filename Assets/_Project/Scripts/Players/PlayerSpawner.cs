using System.Collections.Generic;
using OverSmithed.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Players
{
    /// <summary>
    /// Server only. Keeps one <see cref="NetworkPlayer"/> per roster entry in the current scene,
    /// owned by the machine of that player. Avatars belong to the scene and are destroyed with it.
    /// Spawning starts once every client has finished loading the scene.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkPlayer _playerPrefab;
        [SerializeField] private Transform[] _spawnPoints;
        [Tooltip("Whether players can move in this scene (off in the lobby, on in game).")]
        [SerializeField] private bool _allowMovement;

        private readonly Dictionary<(ulong clientId, byte localIndex), NetworkPlayer> _players = new();
        private SessionRoster _roster;

        // Awake (not Start): the load event can complete in the same frame as the scene load on a lone host.
        private void Awake()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                enabled = false;
                return;
            }
            networkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
        }

        private void OnDestroy()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.SceneManager != null)
            {
                networkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            }
            if (_roster != null)
            {
                _roster.Changed -= Synchronize;
            }
        }

        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name || _roster != null)
            {
                return;
            }
            foreach (var clientId in clientsTimedOut)
            {
                Debug.LogWarning($"[PlayerSpawner] Client {clientId} did not load {sceneName} in time.");
            }

            _roster = SessionRoster.Instance;
            _roster.Changed += Synchronize;
            Synchronize();
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

            // Spawn avatars for new entries, and keep the skin of the others up to date.
            for (var i = 0; i < _roster.Count; i++)
            {
                var entry = _roster[i];
                var key = (entry.ClientId, entry.LocalIndex);
                if (!_players.TryGetValue(key, out var player))
                {
                    _players[key] = Spawn(entry);
                }
                else
                {
                    player.SetSkin(entry.SkinIndex);
                }
            }
        }

        private NetworkPlayer Spawn(RosterEntry entry)
        {
            var point = _spawnPoints[(entry.PlayerNumber - 1) % _spawnPoints.Length];
            var player = Instantiate(_playerPrefab, point.position, point.rotation);
            player.Setup(entry, _allowMovement);
            player.NetworkObject.SpawnWithOwnership(entry.ClientId, destroyWithScene: true);
            return player;
        }
    }
}
