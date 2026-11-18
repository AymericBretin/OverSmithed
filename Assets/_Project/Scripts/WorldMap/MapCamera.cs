using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// World map camera: same tilted view as the levels (fixed pitch, no yaw), follows the <see cref="MapWalker"/>
    /// smoothly. The point it looks at stays inside <see cref="_lookArea"/> so the view never goes far over the sea.
    /// Local to each machine: every machine follows the walker it sees (no network data).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MapCamera : MonoBehaviour
    {
        [SerializeField, Range(20f, 89f)] private float _pitch = 56f;
        [SerializeField, Range(10f, 60f)] private float _fieldOfView = 30f;
        [Tooltip("Distance (m) from the camera to the point it looks at.")]
        [SerializeField, Min(5f)] private float _distance = 20f;
        [Tooltip("Height (m) of the point it looks at (a bit above the ground, around the walker's body).")]
        [SerializeField] private float _lookHeight = 0.5f;
        [SerializeField, Min(0f)] private float _smoothTime = 0.35f;
        [Tooltip("The looked-at point stays in this area (world X/Z: x, z, width, depth).")]
        [SerializeField] private Rect _lookArea = new(-24.5f, -15.5f, 47.5f, 28f);

        private Camera _camera;
        private Vector3 _velocity;
        private bool _snapped;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            var walker = MapWalker.Current;
            if (walker == null)
            {
                _snapped = false;
                return;
            }
            _camera.fieldOfView = _fieldOfView;
            var rotation = Quaternion.Euler(_pitch, 0f, 0f);
            var look = walker.transform.position;
            look.x = Mathf.Clamp(look.x, _lookArea.xMin, _lookArea.xMax);
            look.z = Mathf.Clamp(look.z, _lookArea.yMin, _lookArea.yMax);
            look.y = _lookHeight;
            var wanted = look - rotation * Vector3.forward * _distance;
            // First frame with a walker: jump there (no slide across the map when the scene opens).
            transform.position = _snapped
                ? Vector3.SmoothDamp(transform.position, wanted, ref _velocity, _smoothTime)
                : wanted;
            transform.rotation = rotation;
            _snapped = true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            var center = new Vector3(_lookArea.center.x, 0.1f, _lookArea.center.y);
            Gizmos.DrawWireCube(center, new Vector3(_lookArea.width, 0.01f, _lookArea.height));
        }
    }
}
