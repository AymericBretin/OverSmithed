using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Items
{
    /// <summary>
    /// Host only. Creates a list of items when the scene starts, once every client has loaded it
    /// (for example items already on the counters at the start of a level, or test items).
    /// </summary>
    public class StartingItemsSpawner : MonoBehaviour
    {
        [Serializable]
        private struct StartingItem
        {
            public ItemDefinition Item;
            [Tooltip("Where the item appears (its bottom is placed on this point).")]
            public Transform Point;
        }

        [SerializeField] private NetworkItem _itemPrefab;
        [SerializeField] private List<StartingItem> _items = new();

        private bool _spawned;

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
        }

        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name || _spawned)
            {
                return;
            }
            _spawned = true;

            foreach (var entry in _items)
            {
                if (entry.Item == null || entry.Point == null)
                {
                    Debug.LogWarning("[StartingItemsSpawner] An entry has no item or no point.", this);
                    continue;
                }
                NetworkItem.Create(_itemPrefab, entry.Item, entry.Point.position, entry.Point.rotation);
            }
        }
    }
}
