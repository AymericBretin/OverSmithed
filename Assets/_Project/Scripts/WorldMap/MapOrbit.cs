using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// Turns its children slowly around this object (around the vertical axis), e.g. the floating islands around the
    /// Floating Islands level. Decor only: each machine turns it on its own, nothing goes on the network.
    /// </summary>
    public class MapOrbit : MonoBehaviour
    {
        [Tooltip("Degrees per second (360 / seconds per turn). Negative = the other way.")]
        [SerializeField] private float _degreesPerSecond = 3f;

        private void Update()
        {
            transform.Rotate(0f, _degreesPerSecond * Time.deltaTime, 0f, Space.World);
        }
    }
}
