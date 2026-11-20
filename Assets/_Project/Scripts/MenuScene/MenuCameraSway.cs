using UnityEngine;

namespace OverSmithed.MenuScene
{
    /// <summary>
    /// Very slow sway of the main menu camera (a few cm, a fraction of a degree) so the 3D scene feels alive.
    /// Decor only: local. The pose set in the scene is the center of the movement.
    /// </summary>
    public class MenuCameraSway : MonoBehaviour
    {
        [Tooltip("Max move (m) on each axis.")]
        [SerializeField] private Vector3 _moveAmount = new(0.08f, 0.04f, 0.05f);
        [Tooltip("Max turn (degrees): pitch, yaw.")]
        [SerializeField] private Vector2 _turnAmount = new(0.3f, 0.5f);
        [Tooltip("Time (s) of one full sway; each axis uses a slightly different time so it never repeats exactly.")]
        [SerializeField, Min(1f)] private float _period = 14f;

        private Vector3 _basePosition;
        private Quaternion _baseRotation;

        private void Awake()
        {
            _basePosition = transform.localPosition;
            _baseRotation = transform.localRotation;
        }

        private void LateUpdate()
        {
            var a = Time.unscaledTime * 2f * Mathf.PI / _period;
            var move = new Vector3(
                Mathf.Sin(a) * _moveAmount.x,
                Mathf.Sin(a * 1.37f + 1.1f) * _moveAmount.y,
                Mathf.Sin(a * 0.71f + 2.3f) * _moveAmount.z);
            transform.localPosition = _basePosition + _baseRotation * move;
            transform.localRotation = _baseRotation * Quaternion.Euler(
                Mathf.Sin(a * 0.83f + 0.7f) * _turnAmount.x,
                Mathf.Sin(a * 1.13f + 1.9f) * _turnAmount.y,
                0f);
        }
    }
}
