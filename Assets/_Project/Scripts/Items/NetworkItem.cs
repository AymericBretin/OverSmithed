using OverSmithed.Core;
using OverSmithed.Counters;
using OverSmithed.Interaction;
using OverSmithed.Players;
using Unity.Netcode;
using Unity.Netcode.Components;
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
        [Tooltip("Small cloud played where a special item comes back after a fall.")]
        [SerializeField] private ParticleSystem _poofPrefab;

        private readonly NetworkVariable<int> _itemId = new(ItemDefinition.NoneId);

        private ItemDefinition _pendingDefinition;
        private GameObject _visual;
        private int _visualItemId = ItemDefinition.NoneId;
        private bool _isThrown;
        private PlayerHands _thrower;

        /// <summary>The item leaves the network (destroyed, end of level). For example a counter holding it becomes empty.</summary>
        public event System.Action<NetworkItem> Despawned;

        /// <summary>What this item is, on every machine. Null before spawn or if the id is unknown.</summary>
        public ItemDefinition Definition => _database != null ? _database.Get(_itemId.Value) : null;

        /// <summary>
        /// The child that holds the visual, or null if the visual is created on the item itself. It is not
        /// networked: each machine may move it a little for looks (the carried item follows the hands).
        /// </summary>
        public Transform VisualRoot => _visualRoot != null && _visualRoot != transform ? _visualRoot : null;

        /// <summary>
        /// Host only: the counter this item started on (special items like the water bucket). If the item falls off
        /// the map it comes back there instead of being destroyed. Null for normal items.
        /// </summary>
        public Counter Home { get; set; }

        /// <summary>Host: the item is free and moved by physics (on the floor, sliding).</summary>
        public bool HasPhysics => _body != null && !_body.isKinematic;

        /// <summary>Host: current flight velocity, used to face an incoming item even at point-blank range.</summary>
        public Vector3 Velocity => HasPhysics ? _body.linearVelocity : Vector3.zero;

        /// <summary>Host: this free item came from a throw and can be caught by an empty-handed player.</summary>
        public bool IsThrown => _isThrown;

        /// <summary>
        /// Host only: the item becomes free: it falls and slides from where it is, starting at
        /// <paramref name="velocity"/>. Call it after the item left any hands or counter.
        /// </summary>
        public void StartPhysics(Vector3 velocity, bool thrown = false, PlayerHands thrower = null)
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
            _isThrown = thrown;
            _thrower = thrown ? thrower : null;
        }

        /// <summary>Host only: the item stops moving by itself (it goes in hands or on a counter).</summary>
        public void StopPhysics()
        {
            if (!IsServer)
            {
                return;
            }
            _isThrown = false;
            _thrower = null;
            if (_body == null || _body.isKinematic)
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

        // Host: a free item under the fall height fell off the map.
        private void Update()
        {
            if (IsServer && IsSpawned && transform.parent == null && transform.position.y < FallLimit.Height)
            {
                FellOff();
            }
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || !_isThrown || !HasPhysics || _collider == null)
            {
                return;
            }

            // Player/Item collisions are disabled in the project (carried items must not block movement).
            // Explicit queries still see players. Sweep the real item box over the next physics step so fast
            // throws cannot skip a receiver; stop at walls/counters before considering a player behind them.
            // Player-only barriers stop controllers but must not occlude catch or landing sweeps.
            var itemSweepLayers = ~LayerMask.GetMask(PlayerBarrier.LayerName);
            var center = _body.position + _body.rotation * Vector3.Scale(_collider.center, transform.lossyScale);
            var halfSize = Vector3.Scale(_collider.size * 0.5f, transform.lossyScale);
            foreach (var contact in Physics.OverlapBox(center, halfSize, _body.rotation, itemSweepLayers,
                         QueryTriggerInteraction.Ignore))
            {
                var hands = contact.GetComponentInParent<PlayerHands>();
                if (hands != null && CanBeCaughtBy(hands) && hands.TryCatch(this))
                {
                    return;
                }
            }

            var travel = _body.linearVelocity * Time.fixedDeltaTime
                         + Physics.gravity * (0.5f * Time.fixedDeltaTime * Time.fixedDeltaTime);
            if (travel.sqrMagnitude < 0.000001f)
            {
                return;
            }
            var hits = Physics.BoxCastAll(center, halfSize, travel.normalized, _body.rotation, travel.magnitude,
                itemSweepLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == _collider || hit.collider.GetComponentInParent<NetworkItem>() != null)
                {
                    continue;
                }
                var hands = hit.collider.GetComponentInParent<PlayerHands>();
                if (hands != null)
                {
                    if (CanBeCaughtBy(hands) && hands.TryCatch(this))
                    {
                        return;
                    }
                    continue;
                }
                var counter = hit.collider.GetComponentInParent<Counter>();
                if (counter != null && travel.y <= 0f && hit.normal.y > 0.5f)
                {
                    counter.TryPlace(this);
                    return;
                }
                // Ordinary physics resolves the first solid surface.
                return;
            }
        }

        /// <summary>Host: only an incoming throw can be caught, never the thrower's own outgoing item.</summary>
        public bool CanBeCaughtBy(PlayerHands hands)
        {
            if (!_isThrown || hands == null || hands == _thrower || _body == null)
            {
                return false;
            }
            // Player/item collisions are disabled. Without this check an item rejected from behind could
            // pass through the player, enter their facing cone, and be caught while flying away from them.
            var toPlayer = hands.transform.position - _body.position;
            toPlayer.y = 0f;
            var velocity = _body.linearVelocity;
            velocity.y = 0f;
            return velocity.sqrMagnitude < 0.0001f || Vector3.Dot(toPlayer, velocity) >= 0f;
        }

        // A contact further than this (m) is only "speculative": with ContinuousSpeculative collision detection the
        // physics reports surfaces the item MAY touch during the next step, before it really does. A throw that
        // starts 10 cm above the counter in front of the player got such a contact and was placed on that counter.
        private const float TouchDistance = 0.02f;

        // The host simulates free items, so it is also the only machine that decides where one lands. A dropped
        // item stays a normal free item: walking over it never picks it up.
        private void OnCollisionEnter(Collision collision) => HandleContact(collision);

        // A speculative contact (ignored in Enter) does not call Enter again when it becomes a real one: Stay does.
        private void OnCollisionStay(Collision collision) => HandleContact(collision);

        private void HandleContact(Collision collision)
        {
            if (!IsServer || !IsSpawned || !HasPhysics || !Touches(collision, topOnly: false))
            {
                return;
            }

            // A free item that lands on top of an empty counter stays there: a throw, but also an item that hit a
            // wall or another item first and then fell on a counter (it must not lie loose on top of it).
            // Contact normals are from the counter surface, unlike Rigidbody.velocity which may already have been
            // changed by the collision solver. Hitting a counter's side or underside still bounces.
            var counter = collision.collider.GetComponentInParent<Counter>();
            if (counter != null && counter.IsSpawned && counter.IsEmpty && Touches(collision, topOnly: true)
                && counter.TryPlace(this))
            {
                return;
            }

            if (!_isThrown)
            {
                return;
            }

            var hands = collision.collider.GetComponentInParent<PlayerHands>();
            if (hands != null && hands == _thrower)
            {
                return;
            }
            if (hands != null && hands.TryCatch(this))
            {
                return;
            }

            // It is no longer flying after hitting a wall, the ground, a full counter, or a player who cannot
            // catch it. It can still be picked up normally with Interact.
            _isThrown = false;
            _thrower = null;
        }

        // True if the item really touches the other collider (not a speculative contact). topOnly: only contacts on
        // an upward surface count (the top of a counter, not its side).
        private static bool Touches(Collision collision, bool topOnly)
        {
            for (var i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                if (contact.separation <= TouchDistance && (!topOnly || contact.normal.y > 0.5f))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Host only: the item fell off the map (alone, or in the hands of a falling player). A normal item is
        /// destroyed (crates give new ones). An item with a <see cref="Home"/> counter goes back on it, or on the
        /// nearest empty counter if that one is taken; destroyed only if no counter is free.
        /// </summary>
        public void FellOff()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            var counter = FindReturnCounter();
            if (counter == null)
            {
                NetworkObject.Despawn();
                return;
            }
            StopPhysics();
            // Teleport: the other machines jump straight there instead of sliding up from under the map.
            var point = counter.ItemPoint;
            if (TryGetComponent<NetworkTransform>(out var networkTransform))
            {
                networkTransform.Teleport(point.position, point.rotation, transform.localScale);
            }
            counter.TryPlace(this);
            CameBackRpc(point.position);
        }

        private Counter FindReturnCounter()
        {
            if (Home == null)
            {
                return null;
            }
            if (Home.IsSpawned && Home.IsEmpty)
            {
                return Home;
            }
            Counter best = null;
            var bestDistance = float.MaxValue;
            foreach (var counter in FindObjectsByType<Counter>())
            {
                var distance = (counter.transform.position - Home.transform.position).sqrMagnitude;
                if (counter.IsSpawned && counter.IsEmpty && distance < bestDistance)
                {
                    best = counter;
                    bestDistance = distance;
                }
            }
            return best;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CameBackRpc(Vector3 position)
        {
            // Appears on the counter with a poof (no flight from under the map).
            if (TryGetComponent<ItemVisualMotion>(out var motion))
            {
                motion.SkipNextFlight();
            }
            if (_poofPrefab != null)
            {
                Instantiate(_poofPrefab, position + Vector3.up * 0.3f, Quaternion.identity);
            }
        }

        public override void OnNetworkDespawn()
        {
            Home = null;
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
