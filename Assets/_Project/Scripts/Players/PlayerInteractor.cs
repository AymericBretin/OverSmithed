using OverSmithed.Interaction;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Finds the closest <see cref="Interactable"/> in front of the player and highlights it.
    /// Runs only on the owner machine, for its own players: the target is a local choice, nothing is sent on
    /// the network. Two players on the same machine each have their own target.
    /// Later, GP-8 reads <see cref="Current"/> when Interact is pressed and asks the host to do the action.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerInteractor : MonoBehaviour
    {
        [Tooltip("Distance (m) from the player's center to the probe point in front of them.")]
        [SerializeField] private float _probeDistance = 0.6f;
        [Tooltip("Height (m) of the probe point above the player's feet.")]
        [SerializeField] private float _probeHeight = 1f;
        [Tooltip("A target is found if its closest point is within this distance (m, on the ground) of the probe.")]
        [SerializeField] private float _probeRadius = 0.55f;
        [Tooltip("A target is found if its closest point is within this height (m) of the probe.")]
        [SerializeField] private float _maxHeightGap = 1.2f;
        [Tooltip("Things further than this angle (degrees) from where the player faces are ignored.")]
        [SerializeField] private float _maxAngle = 70f;
        [Tooltip("The current target is kept unless another one is better by more than this: no flicker between two counters.")]
        [SerializeField] private float _switchMargin = 0.05f;

        private NetworkPlayer _player;

        /// <summary>What this player aims at right now, or null. Only set on the owner machine.</summary>
        public Interactable Current { get; private set; }

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
        }

        private void OnDisable()
        {
            SetCurrent(null);
        }

        // LateUpdate: after PlayerMovement moved and turned the player this frame.
        private void LateUpdate()
        {
            var canAim = _player.IsSpawned && _player.IsOwner && _player.CanMove;
            SetCurrent(canAim ? FindBest() : null);
        }

        private Interactable FindBest()
        {
            var probe = ProbePoint;
            var position = transform.position;
            var forward = Flat(transform.forward).normalized;

            Interactable best = null;
            var bestScore = float.MaxValue;
            var currentScore = float.MaxValue;
            var currentInReach = false;
            foreach (var interactable in Interactable.All)
            {
                if (!TryScore(interactable, probe, position, forward, out var score))
                {
                    continue;
                }
                if (interactable == Current)
                {
                    currentScore = score;
                    currentInReach = true;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = interactable;
                }
            }

            return currentInReach && currentScore <= bestScore + _switchMargin ? Current : best;
        }

        // Lower is better. False if the interactable is out of reach.
        private bool TryScore(Interactable interactable, Vector3 probe, Vector3 position, Vector3 forward,
            out float score)
        {
            score = 0f;
            // Held things (an item on a counter) light up with their holder: players aim at the holder.
            if (!interactable.CanBeTargeted)
            {
                return false;
            }
            var closest = interactable.ClosestPoint(probe);
            if (Mathf.Abs(closest.y - probe.y) > _maxHeightGap)
            {
                return false;
            }

            var distance = Flat(closest - probe).magnitude;
            if (distance > _probeRadius)
            {
                return false;
            }

            var toTarget = Flat(closest - position);
            if (toTarget.sqrMagnitude > 0.0001f && Vector3.Angle(forward, toTarget) > _maxAngle)
            {
                return false;
            }

            // Two counters side by side both touch the probe: the one whose center is nearer wins.
            score = distance + 0.1f * Flat(interactable.Center - probe).magnitude;
            return true;
        }

        private void SetCurrent(Interactable target)
        {
            if (target != Current)
            {
                // A destroyed target compares equal to null: nothing to remove.
                if (Current != null)
                {
                    Current.RemoveHighlight(this);
                }
                Current = target;
            }
            // Every frame, cheap and safe: a target disabled and enabled again in between lost its highlighters.
            if (Current != null)
            {
                Current.AddHighlight(this);
            }
        }

        private Vector3 ProbePoint =>
            transform.position + Flat(transform.forward).normalized * _probeDistance + Vector3.up * _probeHeight;

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private void OnDrawGizmosSelected()
        {
            var probe = ProbePoint;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(probe, _probeRadius);
            if (Current != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(probe, Current.ClosestPoint(probe));
            }
        }
    }
}
