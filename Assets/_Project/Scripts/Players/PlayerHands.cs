using OverSmithed.Counters;
using OverSmithed.Interaction;
using OverSmithed.Items;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.Players
{
    /// <summary>
    /// What a player carries (one item at most), and the Interact button: pick up / put down.
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

        [Tooltip("Where the bottom of the carried item is, from the player's feet (x right, y up, z forward).")]
        [SerializeField] private Vector3 _holdOffset = new(0f, 0.85f, 0.55f);
        [Tooltip("The host refuses a request if the target is further than this (m, on the ground) from the player. " +
                 "Bigger than the aiming reach: the player may have moved a bit while the request travels.")]
        [SerializeField] private float _maxReach = 2f;

        private readonly NetworkVariable<NetworkObjectReference> _heldItem = new(NoItem);

        private NetworkPlayer _player;
        private PlayerInteractor _interactor;
        private LocalPlayerSlot _slot;
        private InputAction _interactAction;
        private NetworkItem _watchedItem;

        /// <summary>The carried item, or null. Valid on every machine.</summary>
        public NetworkItem HeldItem => _heldItem.Value.TryGet(out var networkObject)
            ? networkObject.GetComponent<NetworkItem>() : null;

        public bool IsHolding => HeldItem != null;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _interactor = GetComponent<PlayerInteractor>();
        }

        public override void OnNetworkDespawn()
        {
            // Host: a player who leaves while carrying puts the item on the floor, so it is not lost with them.
            var item = HeldItem;
            if (IsServer && item != null && item.IsSpawned && !NetworkManager.ShutdownInProgress)
            {
                Release(item);
                item.transform.position = FloorUnder(transform.position);
            }
            Watch(null);
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !_player.CanMove || !TryGetInteractAction(out var interact)
                || !interact.WasPressedThisFrame())
            {
                return;
            }
            var target = _interactor.Current;
            // Only networked things can be sent to the host (counters, items). Crates and stations come later.
            if (target != null && target.TryGetComponent<NetworkObject>(out var networkObject) && networkObject.IsSpawned)
            {
                InteractRpc(networkObject);
            }
        }

        // Sent by the owner machine, run on the host.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InteractRpc(NetworkObjectReference target)
        {
            // The target may be gone by the time the request arrives (despawned): nothing to do.
            if (target.TryGet(out var networkObject))
            {
                TryInteract(networkObject);
            }
        }

        /// <summary>
        /// Host only: what Interact does with <paramref name="target"/>. Empty hands: take the item on a counter,
        /// or a free item. Full hands: put the item on an empty counter. Returns false if nothing happened.
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

            // A free item (on the floor later, GP-9). An item on a counter or in someone's hands is not free:
            // the host's own Interactable knows it (attached to a counter, or disabled while carried).
            if (held == null && target.TryGetComponent<NetworkItem>(out var freeItem)
                && freeItem.TryGetComponent<Interactable>(out var interactable)
                && interactable.isActiveAndEnabled && interactable.CanBeTargeted)
            {
                return Hold(freeItem);
            }
            return false;
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
            if (!item.NetworkObject.TrySetParent(NetworkObject, worldPositionStays: false))
            {
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

        private static Vector3 FloorUnder(Vector3 position)
        {
            return Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out var hit, 5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point
                : position;
        }

        private bool TryGetInteractAction(out InputAction interactAction)
        {
            if (_slot != _player.LocalSlot)
            {
                _slot = _player.LocalSlot;
                _interactAction = _slot != null ? _slot.Input.actions.FindAction("Interact") : null;
            }
            interactAction = _interactAction;
            return interactAction != null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.TransformPoint(_holdOffset) + Vector3.up * 0.2f, Vector3.one * 0.4f);
        }
    }
}
