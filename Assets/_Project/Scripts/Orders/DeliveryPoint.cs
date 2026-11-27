using System.Collections.Generic;
using OverSmithed.Counters;
using OverSmithed.Crafting;
using OverSmithed.Effects;
using OverSmithed.Interaction;
using OverSmithed.Items;
using OverSmithed.Levels;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Orders
{
    /// <summary>
    /// Where players give finished items (CR-12). A player brings a forge tray holding one finished item: if an open
    /// order wants it, the order is completed (<see cref="OrderManager.TryDeliverItem"/>: coins for the team), the tray
    /// leaves, and a few seconds later it comes back WORN on the return counter, where worn trays pile up. A worn tray
    /// must be polished before it can be used again (CR-19). Anything else is refused and stays in the hands.
    /// The host decides; every machine plays the coin burst and the bounce.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class DeliveryPoint : NetworkBehaviour
    {
        [SerializeField] private TrayConfig _trays;
        [Tooltip("Where delivered trays come back, worn (they pile up there).")]
        [SerializeField] private Counter _returnCounter;
        [Tooltip("Seconds of game time before a delivered tray comes back.")]
        [SerializeField, Min(0f)] private float _returnDelay = 8f;
        [Header("Looks")]
        [SerializeField] private SquashBounce _bounce;
        [Tooltip("3D coin burst played on the delivery point when an order is completed.")]
        [SerializeField] private ParticleSystem _coinBurst;
        [Tooltip("Where the burst starts (the top of the chest).")]
        [SerializeField] private Transform _effectPoint;
        [Tooltip("Small cloud where a worn tray comes back.")]
        [SerializeField] private ParticleSystem _returnPoof;

        // Host: game time left before each delivered tray comes back.
        private readonly List<float> _pendingReturns = new();

        public override void OnNetworkDespawn()
        {
            _pendingReturns.Clear();
        }

        /// <summary>
        /// Host: true if <paramref name="held"/> can be delivered now: a tray holding one finished item that an open
        /// order wants, during the shift. Asked before the player lets go of it.
        /// </summary>
        public bool CanTake(NetworkItem held)
        {
            if (!IsServer || !IsSpawned || held == null || !held.TryGetComponent<Tray>(out var tray))
            {
                return false;
            }
            var item = tray.SingleContent;
            var orders = OrderManager.Current;
            var shift = ShiftTimer.Current;
            if (item == null || orders == null || !orders.IsSpawned || (shift != null && shift.State != ShiftState.Playing))
            {
                return false;
            }
            for (var i = 0; i < orders.Count; i++)
            {
                var order = orders[i];
                if (order.ItemId == item.Id && orders.GetRemainingPatience(order) > 0f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Host: delivers the tray (released by the player): completes the oldest order for its item, removes the
        /// tray, and schedules a worn tray on the return counter. False (and nothing changes) if it can't be delivered.
        /// </summary>
        public bool TryDeliver(NetworkItem held)
        {
            if (!CanTake(held))
            {
                return false;
            }
            var item = held.GetComponent<Tray>().SingleContent;
            if (!OrderManager.Current.TryDeliverItem(item.Id))
            {
                return false;
            }
            held.NetworkObject.Despawn();
            _pendingReturns.Add(_returnDelay);
            DeliveredRpc();
            return true;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || _pendingReturns.Count == 0)
            {
                return;
            }
            // Game time: a local pause holds the trays too.
            for (var i = _pendingReturns.Count - 1; i >= 0; i--)
            {
                _pendingReturns[i] -= Time.deltaTime;
                if (_pendingReturns[i] <= 0f && TryReturnWornTray())
                {
                    _pendingReturns.RemoveAt(i);
                }
            }
        }

        // Host: a worn tray appears on the return counter, or on the worn pile already there. False if the counter
        // holds something else: it tries again next frame.
        private bool TryReturnWornTray()
        {
            if (_returnCounter == null || !_returnCounter.IsSpawned || _trays == null || _trays.TrayPrefab == null
                || _trays.WornTray == null)
            {
                return false;
            }
            var onCounter = _returnCounter.Item;
            var pile = onCounter != null ? onCounter.GetComponent<Tray>() : null;
            if (onCounter != null && (pile == null || !pile.IsWorn || !pile.IsEmpty))
            {
                return false;
            }
            var point = _returnCounter.ItemPoint;
            if (pile != null)
            {
                // One more worn tray on the pile already there.
                pile.SetCount(pile.Count + 1);
                ReturnedRpc(point.position);
                return true;
            }
            var worn = NetworkItem.Create(_trays.TrayPrefab, _trays.WornTray, point.position, point.rotation);
            if (worn == null)
            {
                return false;
            }
            // A tray must never be lost: after a fall it comes back here.
            worn.Home = _returnCounter;
            if (!_returnCounter.TryPlace(worn))
            {
                worn.NetworkObject.Despawn();
                return false;
            }
            ReturnedRpc(point.position);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DeliveredRpc()
        {
            if (_bounce != null)
            {
                _bounce.Play();
            }
            if (_coinBurst != null)
            {
                var point = _effectPoint != null ? _effectPoint.position : transform.position + Vector3.up * 0.5f;
                Instantiate(_coinBurst, point, Quaternion.identity);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ReturnedRpc(Vector3 position)
        {
            if (_returnPoof != null)
            {
                Instantiate(_returnPoof, position + Vector3.up * 0.2f, Quaternion.identity);
            }
        }
    }
}
