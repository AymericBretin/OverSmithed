using UnityEngine;

namespace OverSmithed.Core
{
    /// <summary>
    /// Marks the playable part of a level: the floor where players walk, the counters and the stations.
    /// <see cref="GameCamera"/> frames this box so it is always visible under the order cards.
    /// Put the object on the floor, at the center of the room. The box follows the world axes (rotation is ignored).
    /// </summary>
    public class PlayArea : MonoBehaviour
    {
        [Tooltip("Width (x), height (y) and depth (z) of the play area, in meters. Height = what must stay visible " +
                 "at the back edge: counters + an item on them is about 1.2 m. Use about 1.8 m if players can walk " +
                 "along the back edge (their heads must stay visible).")]
        [SerializeField] private Vector3 _size = new(16f, 1.2f, 8f);

        /// <summary>The box in world space. Its bottom is on the object's position.</summary>
        public Bounds Bounds => new(transform.position + Vector3.up * (_size.y * 0.5f), _size);

        private void OnDrawGizmos()
        {
            var bounds = Bounds;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
