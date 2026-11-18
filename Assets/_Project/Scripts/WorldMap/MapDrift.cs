using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// Moves an object (a cloud) slowly with the wind; when it leaves the area it comes back on the other side.
    /// Decor only, local to each machine.
    /// </summary>
    public class MapDrift : MonoBehaviour
    {
        [Tooltip("World direction and speed (m/s).")]
        [SerializeField] private Vector3 _velocity = new(0.6f, 0f, 0.15f);
        [Tooltip("The object wraps around inside this box (world X and Z, centered on the map).")]
        [SerializeField] private Vector2 _areaHalfSize = new(45f, 32f);
        [SerializeField] private Vector2 _areaCenter = Vector2.zero;

        private void Update()
        {
            var position = transform.position + _velocity * Time.deltaTime;
            position.x = Wrap(position.x, _areaCenter.x, _areaHalfSize.x);
            position.z = Wrap(position.z, _areaCenter.y, _areaHalfSize.y);
            transform.position = position;
        }

        private static float Wrap(float value, float center, float halfSize)
        {
            if (value > center + halfSize)
            {
                return value - 2f * halfSize;
            }
            if (value < center - halfSize)
            {
                return value + 2f * halfSize;
            }
            return value;
        }
    }
}
