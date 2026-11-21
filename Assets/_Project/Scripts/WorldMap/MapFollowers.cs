using System.Collections.Generic;
using OverSmithed.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// The other players walk in single file behind the world map walker (Mario-like), for looks only. Every machine
    /// builds the line itself from data it already has: the roster (who plays, with which skin) and the walker's
    /// position (NetworkTransform). Nothing goes on the network. The walker's path is recorded as a trail of points; the
    /// follower n stands on it n × <see cref="_spacing"/> behind the walker, so it only goes where the walker went
    /// (never into walls or water). The walker = the leader (first player of the host machine): every other roster
    /// entry is a follower, in join order.
    /// </summary>
    [DefaultExecutionOrder(-100)] // before the followers' own LateUpdate (they measure how far they moved)
    [RequireComponent(typeof(MapWalker))]
    public class MapFollowers : MonoBehaviour
    {
        [SerializeField] private MapFollower _followerPrefab;
        [Tooltip("Distance (m) along the path between two characters of the line.")]
        [SerializeField, Min(0.3f)] private float _spacing = 1.1f;
        [Tooltip("A trail point is recorded each time the walker moved this far (m).")]
        [SerializeField, Min(0.02f)] private float _pointStep = 0.1f;
        [Tooltip("When a level starts, each follower jumps this many seconds after the one in front.")]
        [SerializeField, Min(0f)] private float _jumpStagger = 0.12f;
        [Tooltip("The walker moved more than this (m) in one frame: a teleport, the line is placed again.")]
        [SerializeField] private float _teleportDistance = 3f;
        [Tooltip("At the start the line stands behind the walker; places blocked by these layers are avoided.")]
        [SerializeField] private LayerMask _blockers = ~0;
        [Tooltip("Size of a character for that check (m).")]
        [SerializeField] private float _checkRadius = 0.3f;

        private MapWalker _walker;
        private SessionRoster _roster;
        // Trail of the walker, newest point first.
        private readonly List<Vector3> _trail = new();
        private readonly List<MapFollower> _followers = new();
        private readonly List<int> _skins = new();
        private bool _placed;

        private void Awake()
        {
            _walker = GetComponent<MapWalker>();
        }

        private void OnEnable()
        {
            SessionRoster.InstanceChanged += HandleRosterInstanceChanged;
            HandleRosterInstanceChanged(SessionRoster.Instance);
        }

        private void OnDisable()
        {
            SessionRoster.InstanceChanged -= HandleRosterInstanceChanged;
            HandleRosterInstanceChanged(null);
            foreach (var follower in _followers)
            {
                if (follower != null)
                {
                    Destroy(follower.gameObject);
                }
            }
            _followers.Clear();
        }

        private void HandleRosterInstanceChanged(SessionRoster roster)
        {
            if (_roster != null)
            {
                _roster.Changed -= RefreshFollowers;
            }
            _roster = roster;
            if (_roster != null)
            {
                _roster.Changed += RefreshFollowers;
            }
            RefreshFollowers();
        }

        // ---------- Who follows ----------

        // Every roster entry except the leader (the first entry of the host machine), in roster order.
        private void RefreshFollowers()
        {
            _skins.Clear();
            if (_roster != null)
            {
                var leaderFound = false;
                for (var i = 0; i < _roster.Count; i++)
                {
                    var entry = _roster[i];
                    if (!leaderFound && entry.ClientId == NetworkManager.ServerClientId)
                    {
                        leaderFound = true;
                        continue;
                    }
                    _skins.Add(entry.SkinIndex);
                }
            }

            while (_followers.Count > _skins.Count)
            {
                var last = _followers[^1];
                _followers.RemoveAt(_followers.Count - 1);
                if (last != null)
                {
                    Destroy(last.gameObject);
                }
            }
            var added = false;
            while (_followers.Count < _skins.Count && _followerPrefab != null)
            {
                // Scene root, not under the walker: the walker moves, the followers move on their own.
                var follower = Instantiate(_followerPrefab, transform.position, transform.rotation);
                // In the map's scene (while it loads, the active scene can still be the lobby).
                SceneManager.MoveGameObjectToScene(follower.gameObject, gameObject.scene);
                follower.name = $"MapFollower {_followers.Count + 1}";
                _followers.Add(follower);
                added = true;
            }
            for (var i = 0; i < _followers.Count; i++)
            {
                _followers[i].Setup(_walker.Skins, _skins[i], _walker.MaxSpeed, _walker.VisualScale,
                    (i + 1) * _jumpStagger);
            }
            if (added)
            {
                _placed = false;
            }
        }

        // ---------- Line ----------

        private void LateUpdate()
        {
            _followers.RemoveAll(follower => follower == null);
            if (_followers.Count == 0 || !_walker.IsSpawned)
            {
                return;
            }
            var head = transform.position;
            if (!_placed || _trail.Count == 0 || (head - _trail[0]).sqrMagnitude > _teleportDistance * _teleportDistance)
            {
                PlaceLineBehind(head);
                return;
            }

            if ((head - _trail[0]).sqrMagnitude >= _pointStep * _pointStep)
            {
                _trail.Insert(0, head);
            }
            TrimTrail();

            var deltaTime = Time.deltaTime;
            var ahead = head;
            for (var i = 0; i < _followers.Count; i++)
            {
                var spot = PointOnTrail(head, (i + 1) * _spacing);
                _followers[i].MoveTo(spot, ahead, deltaTime);
                ahead = spot;
            }
        }

        // Start (or teleport): the line stands straight behind the walker. Behind = up the screen (+Z) when that place
        // is free, else another side, so nobody starts inside a house or a tree.
        private void PlaceLineBehind(Vector3 head)
        {
            _placed = true;
            var length = (_followers.Count + 1) * _spacing;
            var direction = PickFreeDirection(head, length);
            _trail.Clear();
            for (var distance = 0f; distance <= length + _pointStep; distance += _pointStep)
            {
                _trail.Add(head + direction * distance);
            }
            var facing = Quaternion.LookRotation(-direction);
            for (var i = 0; i < _followers.Count; i++)
            {
                _followers[i].Snap(head + direction * ((i + 1) * _spacing), facing);
            }
        }

        private Vector3 PickFreeDirection(Vector3 head, float length)
        {
            var directions = new[] { Vector3.forward, Vector3.left, Vector3.right, Vector3.back };
            foreach (var direction in directions)
            {
                if (IsFree(head, direction, length))
                {
                    return direction;
                }
            }
            return Vector3.forward;
        }

        private bool IsFree(Vector3 head, Vector3 direction, float length)
        {
            for (var i = 1; i <= _followers.Count; i++)
            {
                var spot = head + direction * (i * _spacing);
                var bottom = spot + Vector3.up * (_checkRadius + 0.05f);
                var top = spot + Vector3.up * 1.1f;
                if (Physics.CheckCapsule(bottom, top, _checkRadius, _blockers, QueryTriggerInteraction.Ignore))
                {
                    return false;
                }
            }
            // The straight line itself must not cross a wall either (the trail is walked as a path).
            return !Physics.SphereCast(head + Vector3.up * 0.6f, _checkRadius, direction, out _, length, _blockers,
                QueryTriggerInteraction.Ignore);
        }

        // The point at this distance along the trail (from the walker), else the oldest point.
        private Vector3 PointOnTrail(Vector3 head, float distance)
        {
            var previous = head;
            foreach (var point in _trail)
            {
                var segment = Vector3.Distance(previous, point);
                if (segment >= distance && segment > 0f)
                {
                    return Vector3.Lerp(previous, point, distance / segment);
                }
                distance -= segment;
                previous = point;
            }
            return previous;
        }

        // Keep only the part of the trail the last follower needs.
        private void TrimTrail()
        {
            var needed = (_followers.Count + 1) * _spacing + 1f;
            var total = 0f;
            for (var i = 1; i < _trail.Count; i++)
            {
                total += Vector3.Distance(_trail[i - 1], _trail[i]);
                if (total > needed)
                {
                    if (i + 1 < _trail.Count)
                    {
                        _trail.RemoveRange(i + 1, _trail.Count - (i + 1));
                    }
                    return;
                }
            }
        }
    }
}
