using OverSmithed.Players;
using UnityEngine;

namespace OverSmithed.Core
{
    /// <summary>
    /// Makes the fixed game camera a bit more alive: it slides a little toward the average position of all the
    /// players, then glides back when they spread out. It never rotates and never moves far, so the whole room
    /// stays readable. Runs on every machine from the synced player positions: nothing is sent on the network.
    /// </summary>
    public class GameCameraDrift : MonoBehaviour
    {
        [Tooltip("Part of the players' distance from the room center that the camera follows (0 = fixed camera).")]
        [SerializeField, Range(0f, 1f)] private float _follow = 0.15f;
        [Tooltip("Largest camera move from its start position, in meters (x = left/right, y = toward the back/front).")]
        [SerializeField] private Vector2 _maxOffset = new(0.8f, 0.5f);
        [Tooltip("About how long (seconds) the camera takes to reach its target: higher = softer.")]
        [SerializeField] private float _smoothTime = 0.8f;

        private Vector3 _startPosition;
        private Vector3 _roomCenter;
        private Vector3 _velocity;

        private void Awake()
        {
            _startPosition = transform.position;

            // The room center is the point on the ground (y = 0) the camera looks at when the scene starts.
            var view = new Ray(transform.position, transform.forward);
            _roomCenter = new Plane(Vector3.up, Vector3.zero).Raycast(view, out var distance)
                ? view.GetPoint(distance)
                : Vector3.zero;
        }

        // LateUpdate: after the players moved this frame (owner input or network interpolation).
        private void LateUpdate()
        {
            var target = _startPosition;
            var players = NetworkPlayer.Spawned;
            if (players.Count > 0)
            {
                var sum = Vector3.zero;
                foreach (var player in players)
                {
                    sum += player.transform.position;
                }
                var offset = (sum / players.Count - _roomCenter) * _follow;
                target.x += Mathf.Clamp(offset.x, -_maxOffset.x, _maxOffset.x);
                target.z += Mathf.Clamp(offset.z, -_maxOffset.y, _maxOffset.y);
            }
            transform.position = Vector3.SmoothDamp(transform.position, target, ref _velocity, _smoothTime);
        }
    }
}
