using OverSmithed.Crafting;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.Players
{
    /// <summary>
    /// Working a station by hand with Use. HOLD stations (grindstone, CR-19): the owner machine reads Use and the
    /// station in front of the player, and tells the host only when that changes (start / stop), not every frame. The
    /// host keeps the list of workers on each station and checks the rules itself every frame (in reach, empty hands,
    /// able to act): the station works while one of them is valid and pauses otherwise.
    /// TAP stations (anvil, workbench): each press of Use in front of one sends one strike to the host, which checks
    /// the same rules before counting it (<see cref="Station.TryStrike"/>).
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
            return station == _hostStation && CanStrikeAt(station);
        }

        /// <summary>Host: true if this player may strike <paramref name="station"/> now (in reach, empty hands...).</summary>
        public bool CanStrikeAt(Station station)
        {
            return isActiveAndEnabled && IsSpawned && station != null && station.IsSpawned
                   && station.isActiveAndEnabled && _player.CanAct
                   && _player.IsInputConnected && !_hands.IsHolding
                   && _hands.IsInReach(station.NetworkObject);
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            // Tap stations: one strike per press.
            var tapped = TappedStation();
            if (tapped != null)
            {
                StrikeRpc(new NetworkObjectReference(tapped.NetworkObject));
            }
            var wanted = WantedStation();
            if (wanted == _sentStation)
            {
                return;
            }
            _sentStation = wanted;
            WorkRpc(wanted != null ? new NetworkObjectReference(wanted.NetworkObject) : new NetworkObjectReference((NetworkObject)null));
        }

        // Owner: the HOLD station in front of the player while Use is held with empty hands, else null.
        private Station WantedStation()
        {
            if (!_player.CanAct || !_player.IsInputConnected || _hands.IsHolding
                || !TryGetUseAction(out var use) || !use.IsPressed())
            {
                return null;
            }
            var station = TargetStation();
            return station != null && !station.UsesTaps ? station : null;
        }

        // Owner: the TAP station in front of the player when Use was just pressed with empty hands, else null.
        private Station TappedStation()
        {
            if (!_player.CanAct || !_player.IsInputConnected || _hands.IsHolding
                || !TryGetUseAction(out var use) || !use.WasPressedThisFrame())
            {
                return null;
            }
            var station = TargetStation();
            return station != null && station.UsesTaps ? station : null;
        }

        private Station TargetStation()
        {
            var target = _interactor.Current;
            return target != null && target.TryGetComponent<Station>(out var station) && station.NeedsHandWork
                   && station.IsSpawned ? station : null;
        }

        // Sent by the owner machine on each press of Use at a tap station, run on the host (which checks the rules).
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void StrikeRpc(NetworkObjectReference target)
        {
            if (target.TryGet(out var networkObject, NetworkManager)
                && networkObject.TryGetComponent<Station>(out var station))
            {
                station.TryStrike(this);
            }
        }

        /// <summary>Host only (tests): one strike on this tap station, as if Use were pressed.</summary>
        public bool HostStrike(Station station)
        {
            return IsServer && station != null && station.TryStrike(this);
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
