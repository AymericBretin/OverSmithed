using OverSmithed.Crafting;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.Players
{
    /// <summary>
    /// Holding Use on a station worked by hand (the grindstone, CR-19; the anvil, CR-9). The owner machine reads
    /// Use and the station in front of the player, and tells the host only when that changes (start / stop), not every
    /// frame. The host keeps the list of workers on each station and checks the rules itself every frame (in reach,
    /// empty hands, able to act): the station works while one of them is valid and pauses otherwise.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerHands), typeof(PlayerInteractor))]
    public class PlayerWork : NetworkBehaviour
    {
        private NetworkPlayer _player;
        private PlayerHands _hands;
        private PlayerInteractor _interactor;
        private LocalPlayerSlot _slot;
        private InputAction _useAction;

        // Owner: the station last sent to the host (null = not working).
        private Station _sentStation;
        // Host: the station this player works on.
        private Station _hostStation;

        private void Awake()
        {
            _player = GetComponent<NetworkPlayer>();
            _hands = GetComponent<PlayerHands>();
            _interactor = GetComponent<PlayerInteractor>();
        }

        public override void OnNetworkDespawn()
        {
            SetHostStation(null);
            _sentStation = null;
        }

        /// <summary>Host: true if this player may work on <paramref name="station"/> now.</summary>
        public bool CanWorkAt(Station station)
        {
            return isActiveAndEnabled && IsSpawned && station != null && station.IsSpawned
                   && station.isActiveAndEnabled && station == _hostStation && _player.CanAct
                   && _player.IsInputConnected && !_hands.IsHolding
                   && _hands.IsInReach(station.NetworkObject);
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            var wanted = WantedStation();
            if (wanted == _sentStation)
            {
                return;
            }
            _sentStation = wanted;
            WorkRpc(wanted != null ? new NetworkObjectReference(wanted.NetworkObject) : new NetworkObjectReference((NetworkObject)null));
        }

        // Owner: the hand-work station in front of the player while Use is held with empty hands, else null.
        private Station WantedStation()
        {
            if (!_player.CanAct || !_player.IsInputConnected || _hands.IsHolding
                || !TryGetUseAction(out var use) || !use.IsPressed())
            {
                return null;
            }
            var target = _interactor.Current;
            return target != null && target.TryGetComponent<Station>(out var station) && station.NeedsHandWork
                   && station.IsSpawned ? station : null;
        }

        // Sent by the owner machine when the worked station changes, run on the host.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void WorkRpc(NetworkObjectReference target)
        {
            var station = target.TryGet(out var networkObject, NetworkManager)
                ? networkObject.GetComponent<Station>() : null;
            SetHostStation(station);
        }

        private void SetHostStation(Station station)
        {
            if (!IsServer || station == _hostStation)
            {
                return;
            }
            if (_hostStation != null)
            {
                _hostStation.SetWorker(this, false);
            }
            _hostStation = station;
            if (_hostStation != null && !_hostStation.SetWorker(this, true))
            {
                _hostStation = null;
            }
        }

        /// <summary>Host only (tests, or a future auto-work): works on this station as if Use were held.</summary>
        public void HostWorkAt(Station station)
        {
            SetHostStation(station);
        }

        private bool TryGetUseAction(out InputAction use)
        {
            if (_slot != _player.LocalSlot)
            {
                _slot = _player.LocalSlot;
                _useAction = _slot != null ? _slot.Input.actions.FindAction("Use") : null;
            }
            use = _useAction;
            return use != null;
        }
    }
}
