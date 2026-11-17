using OverSmithed.Networking;
using OverSmithed.Players;
using UnityEngine;

namespace OverSmithed.Lobby
{
    /// <summary>
    /// One of the 4 places in the lobby room. While nobody has this player number, it shows a pale ring on the floor
    /// (the "press to join" text is a screen label: LobbySpotLabel); when a player takes it, the ring hides and the
    /// character pops in (again after each skin change).
    /// Runs on every machine from the roster (already synced): nothing new goes on the network.
    /// The PlayerSpawner puts player N on spot N (same transform), so the spot is also the spawn point.
    /// </summary>
    public class LobbySpot : MonoBehaviour
    {
        [Tooltip("Player number (1 to 4) standing on this spot.")]
        [SerializeField, Range(1, 4)] private int _playerNumber = 1;
        [Tooltip("Shown while the spot is free (pale circle + join hint).")]
        [SerializeField] private GameObject _freeVisual;
        [Tooltip("Time (seconds) of the pop when a character appears.")]
        [SerializeField] private float _popTime = 0.35f;

        private bool _occupied;
        private Transform _popTarget;
        private Vector3 _popScale;
        private float _popElapsed = -1f;

        public int PlayerNumber => _playerNumber;
        public bool IsOccupied => _occupied;

        private void OnEnable()
        {
            SessionRoster.InstanceChanged += Bind;
            Bind(SessionRoster.Instance);
        }

        private void OnDisable()
        {
            SessionRoster.InstanceChanged -= Bind;
            if (SessionRoster.Instance != null)
            {
                SessionRoster.Instance.Changed -= Refresh;
            }
            EndPop();
        }

        private void Bind(SessionRoster roster)
        {
            if (roster == null)
            {
                Refresh();
                return;
            }
            roster.Changed -= Refresh;
            roster.Changed += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            var roster = SessionRoster.Instance;
            var occupied = false;
            if (roster != null)
            {
                for (var i = 0; i < roster.Count; i++)
                {
                    if (roster[i].PlayerNumber == _playerNumber)
                    {
                        occupied = true;
                        break;
                    }
                }
            }
            if (_freeVisual != null)
            {
                _freeVisual.SetActive(!occupied);
            }
            _occupied = occupied;
        }

        private void LateUpdate()
        {
            // The avatar is spawned a bit after the roster changes (network spawn), and its skin visual is replaced when
            // the player picks another skin: pop each new skin visual once it is there.
            var skin = _occupied ? FindSkinVisual() : null;
            if (skin != null && skin != _popTarget)
            {
                EndPop();
                _popTarget = skin;
                _popScale = skin.localScale;
                _popElapsed = 0f;
            }
            if (_popTarget == null || _popElapsed < 0f || _popElapsed >= _popTime)
            {
                return;
            }
            _popElapsed += Time.deltaTime;
            var t = Mathf.Clamp01(_popElapsed / _popTime);
            // Ease out with a small overshoot ("back"): 0 → 1.1 → 1.
            const float overshoot = 1.7f;
            var u = t - 1f;
            var scale = 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
            _popTarget.localScale = _popScale * Mathf.Max(0f, scale);
            if (t >= 1f)
            {
                _popTarget.localScale = _popScale;
            }
        }

        private void EndPop()
        {
            if (_popTarget != null && _popElapsed >= 0f && _popElapsed < _popTime)
            {
                _popTarget.localScale = _popScale;
            }
            _popTarget = null;
            _popElapsed = -1f;
        }

        // The skin instance under the "Visual" child of the avatar with this player number.
        private Transform FindSkinVisual()
        {
            foreach (var player in NetworkPlayer.Spawned)
            {
                if (player == null || player.PlayerNumber != _playerNumber)
                {
                    continue;
                }
                var visual = player.transform.Find("Visual");
                return visual != null && visual.childCount > 0 ? visual.GetChild(0) : null;
            }
            return null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, 0.5f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.05f, transform.position + Vector3.up * 0.05f + transform.forward * 0.7f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.2f, $"Spot {_playerNumber}");
        }
#endif
    }
}
