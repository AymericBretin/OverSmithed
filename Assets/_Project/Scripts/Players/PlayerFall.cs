using OverSmithed.Core;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// Falling off the map. The host watches every player (their position comes from their own machine): a player
    /// below <see cref="FallLimit.Height"/> is "out": hidden with a poof, the carried item is lost, and they can't
    /// act. After <see cref="_respawnDelay"/> the host asks the owner machine to put the player back at its spawn
    /// point; once the host sees them there, they are back (poof, then they blink for a moment).
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerFall : NetworkBehaviour
    {
        [Tooltip("Time (seconds) between falling out and coming back.")]
        [SerializeField] private float _respawnDelay = 3f;
        [Tooltip("Time (seconds) the player blinks after coming back.")]
        [SerializeField] private float _blinkTime = 1.5f;
        [Tooltip("Time (seconds) of one blink (shown, then hidden).")]
        [SerializeField] private float _blinkPeriod = 0.16f;
        [Tooltip("The player's visual: hidden while out, blinks after coming back.")]
        [SerializeField] private Transform _visualRoot;
        [Tooltip("Small cloud played where the player disappears and where they come back.")]
        [SerializeField] private ParticleSystem _poofPrefab;

        private readonly NetworkVariable<bool> _isOut = new();

        // Host only.
        private Pose _spawnPose;
        private float _respawnTime;
        private bool _returning;
        private float _resendTime;

        // Every machine.
        private float _blinkStart = float.MinValue;
        private bool _shown = true;

        /// <summary>
        /// True on every machine while the player fell off the map and waits to come back (hidden, can't act).
        /// </summary>
        public bool IsOut => _isOut.Value;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // The player is spawned on its spawn point: it comes back there.
                _spawnPose = new Pose(transform.position, transform.rotation);
            }
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            var limit = FallLimit.Height;
            if (!_isOut.Value)
            {
                if (transform.position.y < limit)
                {
                    FallOut();
                }
                return;
            }
            if (!_returning)
            {
                if (Time.time >= _respawnTime)
                {
                    _returning = true;
                    SendBack();
                }
                return;
            }
            // The owner moved the player back: the host sees it once the new position arrives.
            if (transform.position.y > limit + 1f)
            {
                _returning = false;
                _isOut.Value = false;
                CameBackRpc(transform.position);
            }
            else if (Time.time >= _resendTime)
            {
                // Lost or late answer: ask again.
                SendBack();
            }
        }

        // Host.
        private void FallOut()
        {
            _isOut.Value = true;
            _returning = false;
            _respawnTime = Time.time + _respawnDelay;
            if (TryGetComponent<PlayerHands>(out var hands))
            {
                hands.LoseHeldItem();
            }
            FellOutRpc(transform.position);
        }

        // Host.
        private void SendBack()
        {
            _resendTime = Time.time + 1f;
            MoveBackRpc(_spawnPose.position, _spawnPose.rotation);
        }

        // Run on the owner machine: movement belongs to it.
        [Rpc(SendTo.Owner)]
        private void MoveBackRpc(Vector3 position, Quaternion rotation)
        {
            if (TryGetComponent<PlayerMovement>(out var movement))
            {
                movement.Teleport(position, rotation);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FellOutRpc(Vector3 position)
        {
            Poof(position);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CameBackRpc(Vector3 position)
        {
            Poof(position);
            _blinkStart = Time.time;
        }

        private void Poof(Vector3 position)
        {
            if (_poofPrefab != null)
            {
                Instantiate(_poofPrefab, position + Vector3.up * 0.8f, Quaternion.identity);
            }
        }

        // Every machine: hidden while out, blinking just after coming back.
        private void LateUpdate()
        {
            var sinceBack = Time.time - _blinkStart;
            var blinking = sinceBack < _blinkTime && Mathf.Repeat(sinceBack, _blinkPeriod) > _blinkPeriod * 0.5f;
            var show = !(IsSpawned && _isOut.Value) && !blinking;
            if (show == _shown)
            {
                return;
            }
            _shown = show;
            if (_visualRoot == null)
            {
                return;
            }
            // Asked again each time: the skin visual under the root may have been replaced.
            foreach (var renderer in _visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = show;
            }
        }
    }
}
