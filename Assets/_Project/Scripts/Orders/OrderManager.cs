using System;
using System.Collections.Generic;
using OverSmithed.Items;
using OverSmithed.Levels;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OverSmithed.Orders
{
    /// <summary>
    /// The customer orders of the level (in-scene NetworkObject <c>Orders</c> in a level scene). The host creates them
    /// during the shift: <see cref="_ordersAtStart"/> at "Go!", then one every <see cref="_interval"/> seconds while
    /// fewer than <see cref="_maxOrders"/> are open. Each order is an item picked at random in <see cref="_menu"/>.
    /// Network: one NetworkList written by the host only; NGO copies it to every machine in the same order (a late
    /// joiner gets the whole list). Every machine raises <see cref="OrderAdded"/> / <see cref="OrderRemoved"/> and logs
    /// the changes.
    /// </summary>
    public class OrderManager : NetworkBehaviour
    {
        [Header("Menu")]
        [Tooltip("Items the customers of this level can order (the level definition will hold this list later).")]
        [SerializeField] private List<ItemDefinition> _menu = new();
        [SerializeField] private ItemDatabase _itemDatabase;

        [Header("Rhythm")]
        [Tooltip("Orders that come in at once when the shift starts.")]
        [SerializeField, Min(0)] private int _ordersAtStart = 2;
        [Tooltip("Seconds between two new orders (game time: a local pause stops it).")]
        [SerializeField, Min(0.5f)] private float _interval = 20f;
        [Tooltip("Open orders at most. When full, the next order comes as soon as one is gone.")]
        [SerializeField, Min(1)] private int _maxOrders = 4;

        private NetworkList<Order> _orders;
        // Every machine: a copy of the list, so a removed order is still known when the list is cleared.
        private readonly List<Order> _known = new();

        // Host.
        private ShiftTimer _shift;
        private float _timer;
        private int _nextId = 1;

        /// <summary>The orders of the loaded level, null when the scene has none.</summary>
        public static OrderManager Current { get; private set; }

        public static event Action<OrderManager> CurrentChanged;

        /// <summary>Every machine: a new order came in.</summary>
        public event Action<Order> OrderAdded;

        /// <summary>Every machine: an order left the list (delivered, lost, or the shift restarted).</summary>
        public event Action<Order> OrderRemoved;

        public int Count => _orders.Count;
        public Order this[int index] => _orders[index];
        public int MaxOrders => _maxOrders;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            CurrentChanged = null;
        }

        private void Awake()
        {
            // NetworkList must be created before the object is spawned.
            _orders = new NetworkList<Order>();
        }

        public override void OnNetworkSpawn()
        {
            _orders.OnListChanged += HandleListChanged;
            _known.Clear();
            foreach (var order in _orders)
            {
                _known.Add(order);
            }
            Current = this;
            CurrentChanged?.Invoke(this);
            if (IsServer)
            {
                ShiftTimer.CurrentChanged += HandleShiftChanged;
                HandleShiftChanged(ShiftTimer.Current);
            }
        }

        public override void OnNetworkDespawn()
        {
            _orders.OnListChanged -= HandleListChanged;
            ShiftTimer.CurrentChanged -= HandleShiftChanged;
            WatchShift(null);
            if (Current == this)
            {
                Current = null;
                CurrentChanged?.Invoke(null);
            }
        }

        /// <summary>Every machine: the item an order asks for (null if the id is unknown).</summary>
        public ItemDefinition GetItem(Order order)
        {
            return _itemDatabase != null && _itemDatabase.TryGet(order.ItemId, out var item) ? item : null;
        }

        /// <summary>Host only: takes an order off the list (delivered or lost). Returns false if it is not there.</summary>
        public bool RemoveOrder(int orderId)
        {
            if (!IsServer || !IsSpawned)
            {
                Debug.LogWarning("[Orders] Only the host can remove orders.", this);
                return false;
            }
            for (var i = 0; i < _orders.Count; i++)
            {
                if (_orders[i].Id == orderId)
                {
                    _orders.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        // ---------- Host: following the shift ----------

        private void HandleShiftChanged(ShiftTimer shift)
        {
            WatchShift(shift);
            if (shift != null && shift.State == ShiftState.Playing && _orders.Count == 0)
            {
                StartOrders();
            }
        }

        private void WatchShift(ShiftTimer shift)
        {
            if (_shift != null)
            {
                _shift.StateChanged -= HandleShiftStateChanged;
            }
            _shift = shift;
            if (_shift != null)
            {
                _shift.StateChanged += HandleShiftStateChanged;
            }
        }

        private void HandleShiftStateChanged(ShiftState state)
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            switch (state)
            {
                // A (re)started shift begins with no orders.
                case ShiftState.Countdown:
                    _orders.Clear();
                    break;
                case ShiftState.Playing:
                    StartOrders();
                    break;
            }
        }

        private void StartOrders()
        {
            _orders.Clear();
            _nextId = 1;
            _timer = 0f;
            for (var i = 0; i < _ordersAtStart; i++)
            {
                TryAddOrder();
            }
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || _shift == null || _shift.State != ShiftState.Playing)
            {
                return;
            }
            _timer += Time.deltaTime;
            if (_timer < _interval)
            {
                return;
            }
            // Full: the timer waits at the end, so a new order comes as soon as a place is free.
            if (TryAddOrder())
            {
                _timer -= _interval;
            }
            else
            {
                _timer = _interval;
            }
        }

        private bool TryAddOrder()
        {
            if (_orders.Count >= _maxOrders)
            {
                return false;
            }
            var item = PickItem();
            if (item == null)
            {
                return false;
            }
            _orders.Add(new Order
            {
                Id = _nextId++,
                ItemId = item.Id,
                CreatedAt = NetworkManager.ServerTime.Time,
            });
            return true;
        }

        private ItemDefinition PickItem()
        {
            var count = 0;
            foreach (var item in _menu)
            {
                count += IsOrderable(item) ? 1 : 0;
            }
            if (count == 0)
            {
                Debug.LogWarning("[Orders] The menu has no orderable item.", this);
                return null;
            }
            var pick = Random.Range(0, count);
            foreach (var item in _menu)
            {
                if (IsOrderable(item) && pick-- == 0)
                {
                    return item;
                }
            }
            return null;
        }

        private bool IsOrderable(ItemDefinition item)
        {
            return item != null && _itemDatabase != null && _itemDatabase.TryGet(item.Id, out _);
        }

        // ---------- Every machine ----------

        private void HandleListChanged(NetworkListEvent<Order> change)
        {
            switch (change.Type)
            {
                case NetworkListEvent<Order>.EventType.Add:
                    _known.Add(change.Value);
                    Debug.Log($"[Orders] New order #{change.Value.Id}: {Describe(change.Value)} ({_orders.Count}/{_maxOrders} open)");
                    OrderAdded?.Invoke(change.Value);
                    break;
                case NetworkListEvent<Order>.EventType.Remove:
                case NetworkListEvent<Order>.EventType.RemoveAt:
                    _known.Remove(change.Value);
                    Debug.Log($"[Orders] Order #{change.Value.Id} ({Describe(change.Value)}) is gone ({_orders.Count}/{_maxOrders} open)");
                    OrderRemoved?.Invoke(change.Value);
                    break;
                case NetworkListEvent<Order>.EventType.Clear:
                    var removed = _known.ToArray();
                    _known.Clear();
                    foreach (var order in removed)
                    {
                        OrderRemoved?.Invoke(order);
                    }
                    if (removed.Length > 0)
                    {
                        Debug.Log($"[Orders] Orders cleared ({removed.Length} removed)");
                    }
                    break;
            }
        }

        private string Describe(Order order)
        {
            var item = GetItem(order);
            return item != null ? item.DisplayName : $"item {order.ItemId}";
        }

        // ---------- Debug (Inspector context menu, host, Play Mode) ----------

        [ContextMenu("Debug/Add order now")]
        private void DebugAddOrder()
        {
            if (IsServer && IsSpawned && !TryAddOrder())
            {
                Debug.Log("[Orders] No order added (list full or empty menu).", this);
            }
        }

        [ContextMenu("Debug/Remove oldest order")]
        private void DebugRemoveOldest()
        {
            if (IsServer && IsSpawned && _orders.Count > 0)
            {
                RemoveOrder(_orders[0].Id);
            }
        }

        private void OnValidate()
        {
            if (_menu.Count == 0)
            {
                Debug.LogWarning($"[Orders] '{name}': the menu is empty, no order will come.", this);
            }
            foreach (var item in _menu)
            {
                if (item == null)
                {
                    Debug.LogWarning($"[Orders] '{name}': empty entry in the menu.", this);
                }
                else if (_itemDatabase != null && !_itemDatabase.TryGet(item.Id, out _))
                {
                    Debug.LogWarning($"[Orders] '{name}': {item.name} is not in the item database.", this);
                }
            }
        }
    }
}
