using OverSmithed.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Items
{
    /// <summary>
    /// An item that exists on the network. One prefab is used for every kind of item: only the item id
    /// travels on the network, and every machine creates the visual of that <see cref="ItemDefinition"/> locally.
    /// The host owns items: only the host creates, changes and moves them (NetworkTransform authority: Server).
    /// </summary>
    // A click in the Scene view selects the item root (the networked transform), not the visual's mesh.
    [SelectionBase]
    public class NetworkItem : NetworkBehaviour
    {
        [Tooltip("Must be the same database on every machine: it turns the network id back into an item.")]
        [SerializeField] private ItemDatabase _database;
        [Tooltip("The item's visual is created under this transform.")]
        [SerializeField] private Transform _visualRoot;

        private readonly NetworkVariable<int> _itemId = new(ItemDefinition.NoneId);

        private ItemDefinition _pendingDefinition;
        private GameObject _visual;
        private int _visualItemId = ItemDefinition.NoneId;

        /// <summary>What this item is, on every machine. Null before spawn or if the id is unknown.</summary>
        public ItemDefinition Definition => _database != null ? _database.Get(_itemId.Value) : null;

        /// <summary>
        /// Host only: creates a new item of this kind at this place, on every machine.
        /// The item belongs to the current scene and is destroyed with it. Returns null if not allowed.
        /// </summary>
        public static NetworkItem Create(NetworkItem prefab, ItemDefinition definition, Vector3 position,
            Quaternion rotation)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                Debug.LogWarning("[NetworkItem] Only the host can create items.");
                return null;
            }
            if (prefab == null || definition == null)
            {
                Debug.LogWarning("[NetworkItem] Create needs a prefab and an item definition.");
                return null;
            }

            var item = Instantiate(prefab, position, rotation);
            item._pendingDefinition = definition;
            item.NetworkObject.Spawn(destroyWithScene: true);
            return item;
        }

        /// <summary>Host only: turns this item into another kind of item (for example a hot ingot into a blade).</summary>
        public void SetDefinition(ItemDefinition definition)
        {
            if (!IsServer || !IsSpawned)
            {
                Debug.LogWarning("[NetworkItem] Only the host can change a spawned item.", this);
                return;
            }
            if (IsKnown(definition))
            {
                _itemId.Value = definition.Id;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && IsKnown(_pendingDefinition))
            {
                // Written here rather than before Spawn(): the value is sent with the spawn message.
                _itemId.Value = _pendingDefinition.Id;
            }
            _pendingDefinition = null;

            _itemId.OnValueChanged += HandleItemIdChanged;
            Refresh();
        }

        public override void OnNetworkDespawn()
        {
            _itemId.OnValueChanged -= HandleItemIdChanged;
        }

        private void HandleItemIdChanged(int previous, int current)
        {
            Refresh();
        }

        // Clients can only rebuild items that are in the database: anything else would be lost on the way.
        private bool IsKnown(ItemDefinition definition)
        {
            if (definition == null)
            {
                Debug.LogWarning("[NetworkItem] No item definition given.", this);
                return false;
            }
            if (_database == null || _database.Get(definition.Id) != definition)
            {
                Debug.LogWarning($"[NetworkItem] '{definition.name}' (id {definition.Id}) is not in the item database.",
                    this);
                return false;
            }
            return true;
        }

        private void Refresh()
        {
            var definition = Definition;
            name = $"NetworkItem {(definition != null ? definition.DisplayName : "?")} (id {_itemId.Value})";
            ShowVisual(definition);
        }

        private void ShowVisual(ItemDefinition definition)
        {
            if (_visual != null && _visualItemId == _itemId.Value)
            {
                return;
            }
            if (_visual != null)
            {
                Destroy(_visual);
                _visual = null;
            }
            _visualItemId = _itemId.Value;
            if (definition == null)
            {
                Debug.LogWarning($"[NetworkItem] Unknown item id {_itemId.Value}.", this);
                return;
            }
            if (definition.Prefab == null)
            {
                Debug.LogWarning($"[NetworkItem] '{definition.name}' has no prefab.", this);
                return;
            }
            _visual = Instantiate(definition.Prefab, _visualRoot != null ? _visualRoot : transform, false);

            // A highlighted item that changes kind must also highlight its new visual.
            if (TryGetComponent<Interactable>(out var interactable))
            {
                interactable.RefreshHighlight();
            }
        }
    }
}
