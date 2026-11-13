using System;
using System.Collections.Generic;
using OverSmithed.Interaction;
using OverSmithed.Items;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Counters
{
    /// <summary>
    /// A surface that holds one item, or nothing. The host decides what is on it: only the host calls
    /// <see cref="TryPlace"/> and <see cref="Take"/>, and every machine reads <see cref="Item"/>.
    /// Only a reference to the item travels on the network; the item's position is already synced by its
    /// NetworkTransform. On every machine the item is attached to the counter's <see cref="Interactable"/>:
    /// players aim at the counter, and the item lights up with it.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class Counter : NetworkBehaviour
    {
        [Tooltip("Where the bottom of the held item is placed (and how it is turned). Empty = the counter's pivot.")]
        [SerializeField] private Transform _itemPoint;
        [Tooltip("Optional: item already on the counter when the level starts (created by the host).")]
        [SerializeField] private ItemDefinition _startingItem;
        [Tooltip("Prefab used to create the starting item.")]
        [SerializeField] private NetworkItem _itemPrefab;

        // NGO's "no object" reference (a default struct would point at id 0 instead).
        private static readonly NetworkObjectReference NoItem = new((NetworkObject)null);

        private readonly NetworkVariable<NetworkObjectReference> _item = new(NoItem);

        private Interactable _interactable;
        private NetworkItem _attachedItem;
        private NetworkItem _watchedItem;
        private bool _needsResolve;
        private bool _startingItemSpawned;

        /// <summary>Every machine: the held item changed (it can be null). For example a station starts working.</summary>
        public event Action<Counter> ItemChanged;

        /// <summary>The item on this counter, or null. Valid on every machine.</summary>
        public NetworkItem Item => _item.Value.TryGet(out var networkObject) ? networkObject.GetComponent<NetworkItem>() : null;

        public bool IsEmpty => Item == null;

        private Transform ItemPoint => _itemPoint != null ? _itemPoint : transform;

        private void Awake()
        {
            _interactable = GetComponent<Interactable>();

            // Awake (not OnNetworkSpawn): the load event can complete in the same frame as the scene load on a
            // lone host. The starting item is created once every machine has loaded the scene.
            var networkManager = NetworkManager.Singleton;
            if (_startingItem != null && networkManager != null && networkManager.IsServer)
            {
                networkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
            }
        }

        public override void OnDestroy()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.SceneManager != null)
            {
                networkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            }
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            _item.OnValueChanged += HandleItemChanged;
            Resolve();
        }

        public override void OnNetworkDespawn()
        {
            _item.OnValueChanged -= HandleItemChanged;
            Watch(null);
            SetAttached(null);
        }

        /// <summary>
        /// Host only: puts <paramref name="item"/> on this counter (moved onto the item point).
        /// Returns false if the counter is not empty.
        /// </summary>
        public bool TryPlace(NetworkItem item)
        {
            if (!IsServer || !IsSpawned)
            {
                Debug.LogWarning("[Counter] Only the host can place items.", this);
                return false;
            }
            if (item == null || !item.IsSpawned || !IsEmpty)
            {
                return false;
            }

            var point = ItemPoint;
            item.transform.SetPositionAndRotation(point.position, point.rotation);
            Watch(item);
            _item.Value = item.NetworkObject;
            return true;
        }

        /// <summary>
        /// Host only: the item leaves this counter (it is not moved: the caller does it, for example a player
        /// picks it up). Returns the item, or null if the counter was empty.
        /// </summary>
        public NetworkItem Take()
        {
            if (!IsServer || !IsSpawned)
            {
                Debug.LogWarning("[Counter] Only the host can take items.", this);
                return null;
            }
            var item = Item;
            Clear();
            return item;
        }

        private void Clear()
        {
            Watch(null);
            _item.Value = NoItem;
        }

        // Host: a held item that is destroyed (trash, end of level) frees the counter. Without this the
        // reference would stay, and network ids are reused for new objects later.
        private void Watch(NetworkItem item)
        {
            if (_watchedItem != null)
            {
                _watchedItem.Despawned -= HandleItemDespawned;
            }
            _watchedItem = item;
            if (_watchedItem != null)
            {
                _watchedItem.Despawned += HandleItemDespawned;
            }
        }

        private void HandleItemDespawned(NetworkItem item)
        {
            if (IsServer && IsSpawned && item == _watchedItem)
            {
                Clear();
            }
        }

        private void HandleItemChanged(NetworkObjectReference previous, NetworkObjectReference current)
        {
            Resolve();
        }

        // On a client the counter's new value can arrive before the item itself: try again every frame.
        private void Update()
        {
            if (_needsResolve)
            {
                Resolve();
            }
        }

        private void Resolve()
        {
            var hasReference = _item.Value.TryGet(out var networkObject);
            var item = hasReference ? networkObject.GetComponent<NetworkItem>() : null;
            _needsResolve = !hasReference && !_item.Value.Equals(NoItem);
            SetAttached(item);
        }

        private void SetAttached(NetworkItem item)
        {
            if (item == _attachedItem)
            {
                return;
            }
            if (_attachedItem != null && _attachedItem.TryGetComponent<Interactable>(out var oldInteractable))
            {
                _interactable.Detach(oldInteractable);
            }
            _attachedItem = item;
            if (_attachedItem != null && _attachedItem.TryGetComponent<Interactable>(out var newInteractable))
            {
                _interactable.Attach(newInteractable);
            }
            ItemChanged?.Invoke(this);
        }

        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name || _startingItemSpawned || !IsSpawned)
            {
                return;
            }
            _startingItemSpawned = true;
            NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;

            var point = ItemPoint;
            var item = NetworkItem.Create(_itemPrefab, _startingItem, point.position, point.rotation);
            if (item != null)
            {
                TryPlace(item);
            }
        }

        private void OnDrawGizmos()
        {
            if (_startingItem == null)
            {
                return;
            }
            // Shows in the Scene view which counters start with an item.
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(ItemPoint.position + Vector3.up * 0.2f, Vector3.one * 0.4f);
        }
    }
}
