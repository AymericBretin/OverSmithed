using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// Spins a part of a model (windmill blades, water wheel...) around a local axis that goes through
    /// <see cref="_localCenter"/> (the hub), so the part does not wobble when its pivot is not at the hub.
    /// Decor only, local to each machine.
    /// </summary>
    public class MapSpin : MonoBehaviour
    {
        [Tooltip("Spin axis, in this object's local space.")]
        [SerializeField] private Vector3 _localAxis = Vector3.forward;
        [Tooltip("The hub, in this object's local space.")]
        [SerializeField] private Vector3 _localCenter;
        [SerializeField] private float _degreesPerSecond = 40f;

        private void Update()
        {
            var center = transform.TransformPoint(_localCenter);
            var axis = transform.TransformDirection(_localAxis);
            transform.RotateAround(center, axis, _degreesPerSecond * Time.deltaTime);
        }
    }
}
