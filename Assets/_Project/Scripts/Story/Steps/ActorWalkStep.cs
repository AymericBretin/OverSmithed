using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>
    /// An actor walks (or runs) from where it stands through <see cref="_points"/>, then turns to face
    /// <see cref="_faceAtEnd"/> (optional). The Move blend tree follows the speed. The time it takes comes from the path
    /// length and <see cref="_speed"/>.
    /// </summary>
    public class ActorWalkStep : CutsceneStep
    {
        [SerializeField] private CutsceneActor _actor;
        [Tooltip("Points to walk through, in order (only their position on the ground counts).")]
        [SerializeField] private Transform[] _points = new Transform[0];
        [Tooltip("Meters per second (walk ~1.6, run ~4).")]
        [SerializeField, Min(0.1f)] private float _speed = 1.6f;
        [Tooltip("Move blend value while moving: 0.4 = walk, 1 = run.")]
        [SerializeField, Range(0f, 1f)] private float _animSpeed = 0.4f;
        [Tooltip("At the end, turns toward this (optional).")]
        [SerializeField] private Transform _faceAtEnd;
        [SerializeField, Min(0f)] private float _turnTime = 0.4f;
        [Tooltip("At the end, hides the actor (it went out of the door).")]
        [SerializeField] private bool _hideAtEnd;
        [Tooltip("On: the actor follows the height of the points (walks down a slope). Off: stays at its start height.")]
        [SerializeField] private bool _usePointHeights;

        public override CutsceneActor FocusActor => _actor;

        // Not kept by the editor's script reload: a path left from an edit-mode test would give a wrong Length.
        [System.NonSerialized] private Vector3[] _path;
        [System.NonSerialized] private float _pathLength;
        private Quaternion _endRotation;
        private Quaternion _arrivalRotation;

        public override float Length => Mathf.Max(0.01f, PathLengthFromPoints() / _speed + (_faceAtEnd != null ? _turnTime : 0f));

        // Before Begin, the length is measured from the first point (the start place is not known yet).
        private float PathLengthFromPoints()
        {
            if (_path != null && _path.Length >= 2)
            {
                return _pathLength;
            }
            var length = 0f;
            for (var i = 1; i < _points.Length; i++)
            {
                if (_points[i] != null && _points[i - 1] != null)
                {
                    length += Flat(_points[i].position - _points[i - 1].position).magnitude;
                }
            }
            return length;
        }

        protected override void Begin(bool skipping)
        {
            if (_actor == null || !_actor.IsPresent)
            {
                return;
            }
            var count = 1;
            foreach (var point in _points)
            {
                count += point != null ? 1 : 0;
            }
            _path = new Vector3[count];
            _path[0] = _actor.transform.position;
            var index = 1;
            foreach (var point in _points)
            {
                if (point != null)
                {
                    _path[index++] = new Vector3(point.position.x, _usePointHeights ? point.position.y : _path[0].y,
                        point.position.z);
                }
            }
            _pathLength = 0f;
            for (var i = 1; i < _path.Length; i++)
            {
                _pathLength += (_path[i] - _path[i - 1]).magnitude;
            }
            var last = _path.Length >= 2 ? Flat(_path[^1] - _path[^2]) : Vector3.zero;
            _arrivalRotation = last.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(last) : _actor.transform.rotation;
            _endRotation = _arrivalRotation;
            if (_faceAtEnd != null)
            {
                var toTarget = Flat(_faceAtEnd.position - _path[^1]);
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    _endRotation = Quaternion.LookRotation(toTarget);
                }
            }
            _actor.SetVisible(true);
            _actor.Play("Move");
        }

        protected override void Evaluate(float t, float deltaTime)
        {
            if (_path == null || _actor == null || !_actor.IsPresent)
            {
                return;
            }
            var time = t * Length;
            var walkTime = _pathLength / _speed;
            var actor = _actor.transform;
            if (time < walkTime && deltaTime > 0f)
            {
                var position = PointAt(time * _speed, out var direction);
                actor.position = position;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    actor.rotation = Quaternion.Slerp(actor.rotation, Quaternion.LookRotation(direction),
                        1f - Mathf.Exp(-12f * deltaTime));
                }
                _actor.SetSpeed(_animSpeed, deltaTime);
                return;
            }
            actor.position = _path[^1];
            _actor.SetSpeed(0f, deltaTime);
            var turn = _turnTime > 0f && _faceAtEnd != null ? Smooth((time - walkTime) / _turnTime) : 1f;
            actor.rotation = deltaTime > 0f
                ? Quaternion.Slerp(_arrivalRotation, _endRotation, turn)
                : _endRotation;
        }

        protected override void Finish()
        {
            if (_path == null || _actor == null || !_actor.IsPresent)
            {
                return;
            }
            _actor.transform.SetPositionAndRotation(_path[^1], _endRotation);
            _actor.SetSpeed(0f, 0f);
            if (_hideAtEnd)
            {
                _actor.SetVisible(false);
            }
        }

        private Vector3 PointAt(float distance, out Vector3 direction)
        {
            for (var i = 1; i < _path.Length; i++)
            {
                var segment = _path[i] - _path[i - 1];
                var length = segment.magnitude;
                if (distance <= length || i == _path.Length - 1)
                {
                    direction = Flat(segment);
                    return length > 0f ? _path[i - 1] + segment * Mathf.Clamp01(distance / length) : _path[i];
                }
                distance -= length;
            }
            direction = Vector3.zero;
            return _path[^1];
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            var previous = _actor != null ? _actor.transform.position : (Vector3?)null;
            foreach (var point in _points)
            {
                if (point == null)
                {
                    continue;
                }
                if (previous.HasValue)
                {
                    Gizmos.DrawLine(previous.Value + Vector3.up * 0.05f, point.position + Vector3.up * 0.05f);
                }
                Gizmos.DrawWireSphere(point.position, 0.12f);
                previous = point.position;
            }
        }
#endif
    }
}
