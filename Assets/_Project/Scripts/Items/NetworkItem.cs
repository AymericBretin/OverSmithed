using OverSmithed.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Items
{
    /// <summary>
    /// An item that exists on the network. One prefab is used for every kind of item: only the item id
    /// travels on the network, and every machine creates the visual of that <see cref="ItemDefinition"/> locally.
    /// The host owns items: only the host creates, changes and moves them (NetworkTransform authority: Server).
    /// Physics: an item in hands or on a counter is kinematic (it doesn't move by itself). A free item (dropped on
    /// the floor) falls and slides, simulated by the host only (<see cref="StartPhysics"/>); clients just follow its
    /// synced position. It always stays upright (no rolling). Its box collider is fitted to the visual.
    /// </summary>
    // A click in the Scene view selects the item root (the networked transform), not the visual's mesh.
    [SelectionBase]
    public class NetworkItem : NetworkBehaviour
    {
        [Tooltip("Must be the same database on every machine: it turns the network id back into an item.")]
        [SerializeField] private ItemDatabase _database;
        [Tooltip("The item's visual is created under this transform.")]
        [SerializeField] private Transform _visualRoot;
        [Tooltip("Physics body: kinematic unless the item is free (dropped on the floor), and only on the host.")]
        [SerializeField] private Rigidbody _body;
        [Tooltip("Resized to the visual's model each time the visual is created.")]
        [SerializeField] private BoxCollider _collider;

        private readonly NetworkVariable<int> _itemId = new(ItemDefinition.NoneId);

        private ItemDefinition _pendingDefinition;
        private GameObject _visual;
        private int _visualItemId = ItemDefinition.NoneId;

        /// <summary>The item leaves the network (destroyed, end of level). For example a counter holding it becomes empty.</summary>
        public event System.Action<NetworkItem> Despawned;

        /// <summary>What this item is, on every machine. Null before spawn or if the id is unknown.</summary>
        public ItemDefinition Definition => _database != null ? _database.Get(_itemId.Value) : null;

        /// <summary>
        /// The child that holds the visual, or null if the visual is created on the item itself. It is not
        /// networked: each machine may move it a little for looks (the carried item follows the hands).
        /// </summary>
        public Transform VisualRoot => _visualRoot != null && _visualRoot != transform ? _visualRoot : null;

        /// <summary>Host: the item is free and moved by physics (on the floor, sliding).</summary>
        public bool HasPhysics => _body != null && !_body.isKinematic;

        /// <summary>
        /// Host only: the item becomes free: it falls and slides from where it is, starting at
        /// <paramref name="velocity"/>. Call it after the item left any hands or counter.
        /// </summary>
        public void StartPhysics(Vector3 velocity)
        {
            if (!IsServer || _body == null)
            {
                return;
            }
            // The physics pose may be old (a carried item follows the player by its transform): start from here.
            _body.position = transform.position;
            _body.rotation = transform.rotation;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.isKinematic = false;
            _body.linearVelocity = velocity;
            _body.angularVelocity = Vector3.zero;
            _body.WakeUp();
        }

        /// <summary>Host only: the item stops moving by itself (it goes in hands or on a counter).</summary>
        public void StopPhysics()
        {
            if (!IsServer || _body == null || _body.isKinematic)
            {
                return;
            }
            // Velocities can only be reset while the body is not kinematic.
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.isKinematic = true;
            // Interpolation would pull a carried item back toward its last physics pose: off while not free.
            _body.interpolation = RigidbodyInterpolation.None;
        }

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
            // Every item starts kinematic, without physics interpolation (it would pull a carried item behind the
            // player). Clients never simulate items: they follow the host's positions (NetworkTransform smoothing).
            if (_body != null)
            {
                _body.isKinematic = true;
                _body.interpolation = RigidbodyInterpolation.None;
            }

            if (IsServer && IsKnown(_pendingDefinition))
            {
                // Written here rather than before Spawn(): the value is sent with the spawn message.
                _itemId.Value = _pendingDefinition.Id;
            }
            _pendingDefinition = null;

            _itemId.OnValueChanged += HandleItemIdChanged;
            Refresh();
            UpdateTargetable(transform.parent);

            // The item is at its real place now: its visual must not fly in from where it was instantiated.
            if (TryGetComponent<ItemVisualMotion>(out var motion))
            {
                motion.Snap();
            }
        }

        public override void OnNetworkDespawn()
        {
            _itemId.OnValueChanged -= HandleItemIdChanged;
            Despawned?.Invoke(this);
        }

        // Called by NGO on every machine when the item gets or loses a network parent (a player carries it).
        public override void OnNetworkObjectParentChanged(NetworkObject parentNetworkObject)
        {
            UpdateTargetable(parentNetworkObject != null ? parentNetworkObject.transform : null);
        }

        // A carried item is not a target: nobody can aim at (or take) the item in someone else's hands.
        private void UpdateTargetable(Transform parent)
        {
            if (TryGetComponent<Interactable>(out var interactable))
            {
                interactable.enabled = parent == null;
            }
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
            FitCollider();

            // A highlighted item that changes kind must also highlight its new visual.
            if (TryGetComponent<Interactable>(out var interactable))
            {
                interactable.RefreshHighlight();
            }
        }

        // The box collider wraps the model. Measured in the visual root's space (its effects, like the pop or the
        // hands lift, don't count): the visual root rests at the item's pivot with no rotation or scale.
        private void FitCollider()
        {
            if (_collider == null || _visual == null)
            {
                return;
            }
            var space = _visualRoot != null ? _visualRoot : transform;
            var toSpace = space.worldToLocalMatrix;
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var meshFilter in _visual.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = meshFilter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }
                var toItem = toSpace * meshFilter.transform.localToWorldMatrix;
                var meshBounds = mesh.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    point = toItem.MultiplyPoint3x4(point);
                    if (hasBounds)
                    {
                        bounds.Encapsulate(point);
                    }
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                }
            }
            if (hasBounds)
            {
                _collider.center = bounds.center;
                _collider.size = bounds.size;
            }
        }
    }
}
