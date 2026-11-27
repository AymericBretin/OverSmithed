using OverSmithed.Effects;
using UnityEngine;

namespace OverSmithed.Items
{
    /// <summary>
    /// A thin trail of tiny dust puffs behind a thrown item, while it flies. Every machine reads
    /// <see cref="NetworkItem.IsFlying"/> (written by the host when the throw starts and when the item lands, is
    /// caught or placed), so the trail starts and stops at the same moment everywhere.
    /// </summary>
    [RequireComponent(typeof(NetworkItem))]
    public class ItemThrowTrail : MonoBehaviour
    {
        [SerializeField] private MotionTrail _trail;

        private NetworkItem _item;

        private void Awake()
        {
            _item = GetComponent<NetworkItem>();
        }

        private void LateUpdate()
        {
            if (_trail != null)
            {
                _trail.Emitting = _item.IsFlying;
            }
        }
    }
}
