using OverSmithed.Effects;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Dust puffs left on the ground behind a dashing player. Every machine uses the dash it sees in the movement
    /// (<see cref="PlayerBodyMotion.IsDashing"/>), so remote players leave a trail too, without network data.
    /// </summary>
    [RequireComponent(typeof(PlayerBodyMotion))]
    public class PlayerDashTrail : MonoBehaviour
    {
        [SerializeField] private MotionTrail _trail;
        [Tooltip("Seconds a dash must last before the trail starts: one odd frame (a frame-time hiccup, network " +
                 "smoothing) is not a dash. A real dash lasts 0.16 s.")]
        [SerializeField, Min(0f)] private float _minDashTime = 0.03f;

        private PlayerBodyMotion _body;
        private float _dashTime;

        private void Awake()
        {
            _body = GetComponent<PlayerBodyMotion>();
        }

        private void LateUpdate()
        {
            if (_trail == null)
            {
                return;
            }
            var dashing = _body.isActiveAndEnabled && _body.IsDashing;
            _dashTime = dashing ? _dashTime + Time.deltaTime : 0f;
            _trail.Emitting = dashing && _dashTime >= _minDashTime;
        }
    }
}
