using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// Keeps a World Space canvas turned like the camera, so it is always readable (player tags,
    /// progress bars above stations...). Uses the main camera.
    /// </summary>
    [DisallowMultipleComponent]
    public class FaceCamera : MonoBehaviour
    {
        private Camera _camera;

        private void LateUpdate()
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }
            transform.rotation = _camera.transform.rotation;
        }
    }
}
