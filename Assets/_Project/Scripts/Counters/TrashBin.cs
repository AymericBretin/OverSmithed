using OverSmithed.Interaction;
using OverSmithed.Items;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Counters
{
    /// <summary>
    /// A networked trash bin. The host despawns the item put in it, so the same item disappears on every machine.
    /// </summary>
    [RequireComponent(typeof(NetworkObject), typeof(Interactable))]
    public class TrashBin : NetworkBehaviour
    {
        /// <summary>Host only: permanently removes <paramref name="item"/> from the network.</summary>
        public bool TryDiscard(NetworkItem item)
        {
            if (!IsServer || !IsSpawned)
            {
                Debug.LogWarning("[TrashBin] Only the host can discard items.", this);
                return false;
            }
            if (item == null || !item.IsSpawned)
            {
                return false;
            }

            item.NetworkObject.Despawn();
            return true;
        }
    }
}
