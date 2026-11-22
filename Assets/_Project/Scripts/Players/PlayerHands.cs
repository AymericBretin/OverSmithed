using OverSmithed.Counters;
using OverSmithed.Interaction;
using OverSmithed.Items;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.Players
{
    /// <summary>
    /// What a player carries (one item at most), and the Interact button: pick up / put down / drop on the floor
    /// (when nothing is in front). The Throw button throws the carried item forward.
    /// The owner machine only asks (one RPC per press, with what <see cref="PlayerInteractor"/> aims at);
    /// the host checks the request and moves the item. Requests are handled one by one on the host, so two
    /// players can't take the same item, and the item is never copied: the same networked item changes place.
    /// A carried item is a network child of the player: every machine sees it follow the player.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerInteractor))]
    public class PlayerHands : NetworkBehaviour
    {
        // NGO's "no object" reference (a default struct would point at id 0 instead).
        private static readonly NetworkObjectReference NoItem = new((NetworkObject)null);

        [Tooltip("Where the bottom of the carried item is, from the player's feet (x right, y up, z forward). " +
                 "Low and far enough forward that the characters' big heads don't hide the item from the camera.")]
        [SerializeField] private Vector3 _holdOffset = new(0f, 0.7f, 0.75f);
        [Tooltip("The host refuses a request if the target is further than this (m, on the ground) from the player. " +
                 "Bigger than the aiming reach: the player may have moved a bit while the request travels.")]
        [SerializeField] private float _maxReach = 2f;
        [Tooltip("Drop: part of the player's speed the item keeps (0 = it falls straight down, 1 = full momentum).")]
        [SerializeField] private float _dropMomentum = 0.6f;
        [Tooltip("Drop: extra speed (m/s) forward, so the item slides a little away from the player.")]
        [SerializeField] private float _dropForwardSpeed = 0.5f;
        [Tooltip("Drop: what the item must not start inside (walls, counters). Not players or items.")]
        [SerializeField] private LayerMask _dropBlockers = 1;
        [Header("Throw")]
        [Tooltip("Throw strength: speed (m/s) forward when the item leaves the hands.")]
        [SerializeField] private float _throwForwardSpeed = 9f;
        [Tooltip("Speed (m/s) upward when the item leaves the hands: a higher arc, a longer throw.")]
        [SerializeField] private float _throwUpSpeed = 2.5f;
        [Tooltip("Part of the player's speed the thrown item keeps (running throws go further).")]
        [SerializeField] private float _throwMomentum = 0.5f;
        [Header("Catch")]
        [Tooltip("A flying item is caught only while it is inside this angle in front of the player (degrees).")]
        [SerializeField] [Range(0f, 180f)] private float _catchAngle = 70f;

        private readonly NetworkVariable<NetworkObjectReference> _heldItem = new(NoItem);

        private NetworkPlayer _player;
        private PlayerInteractor _interactor;
        private PlayerMovement _movement;
        private LocalPlayerSlot _slot;
        private InputAction _interactAction;
        private InputAction _throwAction;
        private NetworkItem _watchedItem;

        /// <summary>Every machine: this player just threw its item (for the arm swing). Sent by the host.</summary>
        public event System.Action Thrown;

        /// <summary>The carried item, or null. Valid on every machine.</summary>
        // Resolve against this peer, including when an integration test runs host and client in one process.
        public NetworkItem HeldItem => _heldItem.Value.TryGet(out var networkObject, NetworkManager)
            ? networkObject.GetComponent<NetworkItem>() : null;

        public bool IsHolding => HeldItem != null;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _interactor = GetComponent<PlayerInteractor>();
            _movement = GetComponent<PlayerMovement>();
        }

        public override void OnNetworkDespawn()
        {
            // Host: a player who leaves while carrying drops the item, so it is not lost with them.
            var item = HeldItem;
            if (IsServer && item != null && item.IsSpawned && !NetworkManager.ShutdownInProgress)
            {
                // While this player despawns, NGO has already taken the item off it, keeping its local position
                // as a world position (the hold offset near the scene origin): put it back at the hold point.
                Release(item);
                item.transform.SetPositionAndRotation(transform.TransformPoint(_holdOffset), transform.rotation);
                item.StartPhysics(Vector3.zero);
            }
            Watch(null);
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !_player.CanAct || !TryGetActions(out var interact, out var throwAction))
            {
                return;
            }
            if (throwAction != null && throwAction.WasPressedThisFrame() && IsHolding)
            {
                ThrowRpc(_movement != null ? _movement.Velocity : Vector3.zero);
                return;
            }
            if (!interact.WasPressedThisFrame())
            {
                return;
            }
            var target = _interactor.Current;
            // Only networked things can be sent to the host (counters, crates, items). Stations come later.
            if (target != null && target.TryGetComponent<NetworkObject>(out var networkObject) && networkObject.IsSpawned)
            {
                InteractRpc(networkObject);
            }
            // Nothing in front and something in the hands: drop it on the floor.
            else if (target == null && IsHolding)
            {
                // The owner knows how fast it really moves (the host only sees it a bit late).
                DropRpc(_movement != null ? _movement.Velocity : Vector3.zero);
            }
        }

        // Sent by the owner machine, run on the host.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DropRpc(Vector3 playerVelocity)
        {
            // The host checks the rule too: a request sent just before the shift ended may arrive after it.
            if (!_player.CanAct)
            {
                return;
            }
            // Never trust a speed sent by a client blindly: at most the top speed.
            var maxSpeed = _movement != null ? _movement.MaxSpeed : 0f;
            TryDrop(Vector3.ClampMagnitude(new Vector3(playerVelocity.x, 0f, playerVelocity.z), maxSpeed));
        }

        /// <summary>
        /// Host only: the carried item is lost (the player fell off the map). It is destroyed, or goes back to its
        /// counter if it is a special item (see <see cref="NetworkItem.FellOff"/>).
        /// </summary>
        public void LoseHeldItem()
        {
            var item = IsServer && IsSpawned ? HeldItem : null;
            if (item == null)
            {
                return;
            }
            Release(item);
            item.FellOff();
        }

        /// <summary>
        /// Host only: the carried item falls on the floor in front of the player and slides a little (it keeps
        /// part of <paramref name="playerVelocity"/>). Returns false if the hands are empty.
        /// </summary>
        public bool TryDrop(Vector3 playerVelocity = default)
        {
            return ReleaseInFront(playerVelocity * _dropMomentum + transform.forward * _dropForwardSpeed, thrown: false);
        }

        // Sent by the owner machine, run on the host.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ThrowRpc(Vector3 playerVelocity)
        {
            if (!_player.CanAct)
            {
                return;
            }
            // Never trust a speed sent by a client blindly: at most the top speed.
            var maxSpeed = _movement != null ? _movement.MaxSpeed : 0f;
            TryThrow(Vector3.ClampMagnitude(new Vector3(playerVelocity.x, 0f, playerVelocity.z), maxSpeed));
        }

        /// <summary>
        /// Host only: the carried item flies forward on an arc (simulated by the host, synced to every machine),
        /// lands and can be picked up again. Returns false if the hands are empty.
        /// </summary>
        public bool TryThrow(Vector3 playerVelocity = default)
        {
            var velocity = transform.forward * _throwForwardSpeed + Vector3.up * _throwUpSpeed
                           + playerVelocity * _throwMomentum;
            if (!ReleaseInFront(velocity, thrown: true))
            {
                return false;
            }
            ThrownRpc();
            return true;
        }

        // Sent by the host to every machine (host included): a one-time event, only for looks (arm swing).
        [Rpc(SendTo.ClientsAndHost)]
        private void ThrownRpc()
        {
            Thrown?.Invoke();
        }

        // Host: the carried item leaves the hands in front of the player (never inside a wall) and starts moving.
        private bool ReleaseInFront(Vector3 velocity, bool thrown)
        {
            var item = IsServer && IsSpawned ? HeldItem : null;
            if (item == null)
            {
                return false;
            }

            // Facing a wall: the item starts against it, not inside it.
            var forward = transform.forward;
            var start = transform.position + Vector3.up * _holdOffset.y;
            var dropPosition = item.transform.position;
            if (Physics.SphereCast(start + Vector3.up * 0.15f, 0.15f, forward, out var hit, _holdOffset.z,
                    _dropBlockers, QueryTriggerInteraction.Ignore))
            {
                dropPosition = start + forward * Mathf.Max(0f, hit.distance - 0.1f);
            }

            var dropRotation = item.transform.rotation;
            Release(item);
            item.transform.SetPositionAndRotation(dropPosition, dropRotation);
            item.StartPhysics(velocity, thrown, thrown ? this : null);
            return true;
        }

        // Sent by the owner machine, run on the host.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InteractRpc(NetworkObjectReference target)
        {
            if (!_player.CanAct)
            {
                return;
            }
            // The target may be gone by the time the request arrives (despawned): nothing to do.
            if (target.TryGet(out var networkObject))
            {
                TryInteract(networkObject);
            }
        }

        /// <summary>
        /// Host only: what Interact does with <paramref name="target"/>. Empty hands: take the item on a counter,
        /// a new item from a crate, or a free item. Full hands: put the item on an empty counter.
        /// Returns false if nothing happened.
        /// </summary>
        public bool TryInteract(NetworkObject target)
        {
            if (!IsServer || !IsSpawned || target == null || !IsInReach(target))
            {
                return false;
            }
            var held = HeldItem;

            if (target.TryGetComponent<Counter>(out var counter))
            {
                if (held == null)
                {
                    var item = counter.Take();
                    if (item == null)
                    {
                        return false;
                    }
                    if (Hold(item))
                    {
                        return true;
                    }
                    counter.TryPlace(item);
                    return false;
                }
                if (!counter.IsEmpty)
                {
                    return false;
                }
                Release(held);
                return counter.TryPlace(held);
            }

            // A crate gives a new item to empty hands. Nothing can be put on a crate.
            if (target.TryGetComponent<Crate>(out var crate))
            {
                if (held != null)
                {
                    return false;
                }
                // Created in the crate, then held: every machine sees it fly from the crate to the hands.
                var newItem = crate.CreateItem();
                if (newItem == null)
                {
                    return false;
                }
                if (Hold(newItem))
                {
                    return true;
                }
                newItem.NetworkObject.Despawn();
                return false;
            }

            // A free item (on the floor). An item on a counter or in someone's hands is not free:
            // the host's own Interactable knows it (attached to a counter, or disabled while carried).
            if (held == null && target.TryGetComponent<NetworkItem>(out var freeItem)
                && freeItem.TryGetComponent<Interactable>(out var interactable)
                && interactable.isActiveAndEnabled && interactable.CanBeTargeted)
            {
                return Hold(freeItem);
            }
            return false;
        }

        /// <summary>
        /// Host only: catches a thrown item that touched this player. The collision itself proves the item is in
        /// reach; a player carrying something or unable to act cannot catch it.
        /// </summary>
        public bool TryCatch(NetworkItem item)
        {
            if (!IsServer || !IsSpawned || !_player.CanAct || IsHolding || item == null || !item.IsSpawned
                || !item.CanBeCaughtBy(this) || !item.HasPhysics || !IsFacing(item.transform.position, item.Velocity))
            {
                return false;
            }
            return Hold(item);
        }

        private bool IsFacing(Vector3 position, Vector3 incomingVelocity)
        {
            var toItem = position - transform.position;
            toItem.y = 0f;
            // At the player's centre the position gives no reliable facing direction. Use the incoming
            // trajectory instead: a throw passing through from behind must not become catchable here.
            if (toItem.sqrMagnitude < 0.0001f)
            {
                toItem = -incomingVelocity;
                toItem.y = 0f;
            }
            if (toItem.sqrMagnitude < 0.0001f)
            {
                return true;
            }
            var forward = transform.forward;
            forward.y = 0f;
            return Vector3.Angle(forward, toItem) <= _catchAngle;
        }

        private bool IsInReach(NetworkObject target)
        {
            var point = target.TryGetComponent<Interactable>(out var interactable)
                ? interactable.ClosestPoint(transform.position)
                : target.transform.position;
            var offset = point - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= _maxReach * _maxReach;
        }

        // Host: the item becomes a network child of the player, in front of them.
        private bool Hold(NetworkItem item)
        {
            // A free item stops sliding before it goes in the hands.
            var wasFree = item.HasPhysics;
            item.StopPhysics();
            if (!item.NetworkObject.TrySetParent(NetworkObject, worldPositionStays: false))
            {
                if (wasFree)
                {
                    item.StartPhysics(Vector3.zero);
                }
                return false;
            }
            item.transform.SetLocalPositionAndRotation(_holdOffset, Quaternion.identity);
            Watch(item);
            _heldItem.Value = item.NetworkObject;
            return true;
        }

        // Host: the item leaves the hands, where it is (the caller then places it).
        private void Release(NetworkItem item)
        {
            item.NetworkObject.TryRemoveParent(worldPositionStays: true);
            Watch(null);
            _heldItem.Value = NoItem;
        }

        // Host: a carried item that is destroyed (trash later) empties the hands. Without this the reference
        // would stay, and network ids are reused for new objects later.
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
                Watch(null);
                _heldItem.Value = NoItem;
            }
        }

        private bool TryGetActions(out InputAction interactAction, out InputAction throwAction)
        {
            if (_slot != _player.LocalSlot)
            {
                _slot = _player.LocalSlot;
                _interactAction = _slot != null ? _slot.Input.actions.FindAction("Interact") : null;
                _throwAction = _slot != null ? _slot.Input.actions.FindAction("Throw") : null;
            }
            interactAction = _interactAction;
            throwAction = _throwAction;
            return interactAction != null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.TransformPoint(_holdOffset) + Vector3.up * 0.2f, Vector3.one * 0.4f);
        }
    }
}
