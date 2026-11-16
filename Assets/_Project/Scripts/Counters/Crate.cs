using OverSmithed.Effects;
using OverSmithed.Interaction;
using OverSmithed.Items;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Counters
{
    /// <summary>
    /// A crate of one material (metal ore, wood…): a player with empty hands takes a new one each time.
    /// It never runs out. Only the host creates items (<see cref="CreateItem"/>). The crate has no network state:
    /// its material is scene data, the same on every machine. It is a NetworkObject so a player's request can
    /// name it, and so the host can tell every machine "an item was taken from me" (bounce + the item flies out).
    /// Nothing can be put on a crate.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class Crate : NetworkBehaviour
    {
        [Tooltip("The material this crate gives.")]
        [SerializeField] private ItemDefinition _material;
        [Tooltip("Prefab used to create the items.")]
        [SerializeField] private NetworkItem _itemPrefab;
        [Tooltip("Where a new item comes out of the crate. Empty = the crate's pivot.")]
        [SerializeField] private Transform _takePoint;
        [Tooltip("Optional: squashes the crate when a player takes from it.")]
        [SerializeField] private SquashBounce _bounce;

        public ItemDefinition Material => _material;

        private Transform TakePoint => _takePoint != null ? _takePoint : transform;

        /// <summary>
        /// Host only: creates a new item of this crate's material, coming out of the crate (every machine sees
        /// it fly to where the caller then moves it, for example a player's hands).
        /// Returns null if not allowed or if the crate has no material.
        /// </summary>
        public NetworkItem CreateItem()
        {
            if (!IsServer || !IsSpawned)
            {
                Debug.LogWarning("[Crate] Only the host can take items from a crate.", this);
                return null;
            }
            if (_material == null)
            {
                Debug.LogWarning("[Crate] No material set on this crate.", this);
                return null;
            }
            var point = TakePoint;
            var item = NetworkItem.Create(_itemPrefab, _material, point.position, point.rotation);
            if (item != null)
            {
                TakenRpc(item.NetworkObject);
            }
            return item;
        }

        public override void OnNetworkSpawn()
        {
            // Checked when the level starts (not in OnValidate: the base Crate prefab has no material on purpose).
            if (IsServer && _material == null)
            {
                Debug.LogWarning($"[Crate] '{name}' has no material.", this);
            }
        }

        // Sent by the host, run on every machine (host included): a one-time event, only for looks.
        [Rpc(SendTo.ClientsAndHost)]
        private void TakenRpc(NetworkObjectReference item)
        {
            if (_bounce != null)
            {
                _bounce.Play();
            }
            // A client may create the item directly in the player's hands: this makes it fly out of the crate
            // there too. If the item is already gone, there is nothing to show.
            if (item.TryGet(out var networkObject) && networkObject.TryGetComponent<ItemVisualMotion>(out var motion))
            {
                motion.FlyFrom(TakePoint.position);
            }
        }
    }
}
