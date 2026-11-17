using UnityEngine;

namespace OverSmithed.Core
{
    /// <summary>
    /// Optional scene setting: a player or an item below this height has fallen off the map.
    /// Without one in the scene, <see cref="DefaultHeight"/> is used. Put one in a level only to change the height
    /// (for example a level built high above the ground).
    /// </summary>
    public class FallLimit : MonoBehaviour
    {
        /// <summary>Height (m) used when the scene has no FallLimit. The floor of the levels is at 0.</summary>
        public const float DefaultHeight = -6f;

        [Tooltip("Below this height (m, world), a player or an item has fallen off the map.")]
        [SerializeField] private float _height = DefaultHeight;

        private static FallLimit _current;

        /// <summary>The fall height of the current scene.</summary>
        public static float Height => _current != null ? _current._height : DefaultHeight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
        }

        private void OnEnable()
        {
            _current = this;
        }

        private void OnDisable()
        {
            if (_current == this)
            {
                _current = null;
            }
        }

        private void OnDrawGizmos()
        {
            // A red square at the fall height, under this object.
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            var center = new Vector3(transform.position.x, _height, transform.position.z);
            Gizmos.DrawWireCube(center, new Vector3(30f, 0f, 20f));
        }
    }
}
