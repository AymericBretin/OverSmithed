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
    /// during the shift: <see cref="_ordersAtStart"/> at "Go!", then more and more often (<see cref="_ramp"/>, CR-16)
    /// while there is room. Each order is an item picked in <see cref="_menu"/>, weighted by the difficulty.
    /// Network: one NetworkList written by the host only; NGO copies it to every machine in the same order (a late
    /// joiner gets the whole list). Every machine raises <see cref="OrderAdded"/> / <see cref="OrderRemoved"/> and logs
    /// the changes.
    /// </summary>
    public class OrderManager : NetworkBehaviour
    {
        [Header("Menu")]
        [Tooltip("Items the customers of this level can order, with how often at the start and at the end of the " +
                 "shift (the level definition will hold this list later). \"Fill weights from recipes\" below sets " +
                 "the weights from the work of each item: simple items first, long ones later.")]
        [SerializeField] private List<OrderMenuEntry> _menu = new();
        [SerializeField] private ItemDatabase _itemDatabase;

        [Header("Rhythm")]
        [Tooltip("Orders that come in at once when the shift starts (picked at difficulty 0).")]
        [SerializeField, Min(0)] private int _ordersAtStart = 2;
        [Tooltip("How the orders get faster and harder during the shift.")]
        [SerializeField] private OrderRamp _ramp = new();

        [Header("Delivery")]
        [SerializeField, Min(0)] private int _coinsPerOrder = 20;
        [Tooltip("Optional item for the Inspector delivery test; no crafting or physical item needed.")]
        [SerializeField] private ItemDefinition _debugDeliveryItem;

        private NetworkList<Order> _orders;
        // A shift's failure history: reliable events for connected clients and a count for late joiners.
        private NetworkList<Order> _failedOrders;
        private NetworkList<Order> _completedOrders;
        private readonly NetworkVariable<int> _teamCoins = new();
        private readonly NetworkVariable<double> _patienceClock = new();
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

        /// <summary>Every machine: an order expired. Historical failures are not replayed to late joiners.</summary>
        public event Action<Order> OrderLost;

        public event Action<Order> OrderCompleted;
        public event Action<int> CoinsChanged;
        public int TeamCoins => _teamCoins.Value;

        /// <summary>Every machine: the order was delivered (it left the list with coins).</summary>
        public bool WasCompleted(Order order) => _completedOrders.Contains(order);

        /// <summary>Every machine: distinguishes an expired order from a reset or a manual removal.</summary>
        public bool WasLost(Order order) => _failedOrders.Contains(order);

        public int FailedCount => _failedOrders.Count;

        /// <summary>Seconds remaining, from the replicated game clock. For active orders only.</summary>
        public float GetRemainingPatience(Order order) =>
            (float)Math.Max(0d, order.ExpiresAt - _patienceClock.Value);

        public float GetPatienceNormalized(Order order) => order.PatienceDuration > 0f
            ? Mathf.Clamp01(GetRemainingPatience(order) / order.PatienceDuration) : 0f;

        public int Count => _orders.Count;
        public Order this[int index] => _orders[index];
        /// <summary>The difficulty settings of this level (read only).</summary>
        public OrderRamp Ramp => _ramp;

        /// <summary>Open orders at most right now (grows with the difficulty).</summary>
        public int MaxOrders => _ramp.MaxOpenAt(Difficulty);

        /// <summary>
        /// Every machine: the difficulty now, 0 (start values) to 1 (end values), from the synced shift time.
        /// 0 outside the shift.
        /// </summary>
        public float Difficulty
        {
            get
            {
                var shift = ShiftTimer.Current;
                if (shift == null || shift.State != ShiftState.Playing || shift.Duration <= 0f)
                {
                    return 0f;
                }
                return _ramp.DifficultyAt(1f - shift.TimeLeft / shift.Duration);
            }
        }

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
            _failedOrders = new NetworkList<Order>();
            _completedOrders = new NetworkList<Order>();
        }

        public override void OnNetworkSpawn()
        {
            _orders.OnListChanged += HandleListChanged;
            _failedOrders.OnListChanged += HandleFailuresChanged;
            _completedOrders.OnListChanged += HandleCompletionsChanged;
            _teamCoins.OnValueChanged += HandleCoinsChanged;
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
            _failedOrders.OnListChanged -= HandleFailuresChanged;
            _completedOrders.OnListChanged -= HandleCompletionsChanged;
            _teamCoins.OnValueChanged -= HandleCoinsChanged;
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

        /// <summary>
        /// Host only: offers a finished item, completing the oldest live order for that item.
        /// The caller must validate ownership and consume the physical item only when this returns true.
        /// </summary>
        public bool TryDeliverItem(int itemId)
        {
            if (!IsServer || !IsSpawned || _shift == null || _shift.State != ShiftState.Playing
                || _itemDatabase == null || !_itemDatabase.TryGet(itemId, out _))
            {
                return false;
            }
            for (var i = 0; i < _orders.Count; i++)
            {
                var order = _orders[i];
                if (order.ItemId != itemId || GetRemainingPatience(order) <= 0f)
                {
                    continue;
                }
                _orders.RemoveAt(i);
                _teamCoins.Value = (int)Math.Min(int.MaxValue, (long)TeamCoins + Math.Max(0, _coinsPerOrder));
                _completedOrders.Add(order);
                return true;
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
                    _failedOrders.Clear();
                    _completedOrders.Clear();
                    _teamCoins.Value = 0;
                    _patienceClock.Value = 0d;
                    break;
                case ShiftState.Playing:
                    StartOrders();
                    break;
            }
        }

        private void StartOrders()
        {
            _orders.Clear();
            _failedOrders.Clear();
            _completedOrders.Clear();
            _teamCoins.Value = 0;
            _patienceClock.Value = 0d;
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
            _patienceClock.Value += Time.deltaTime;
            // Expire before generating, including when the list is full or the spawn timer isn't ready.
            for (var i = _orders.Count - 1; i >= 0; i--)
            {
                var order = _orders[i];
                if (_patienceClock.Value >= order.ExpiresAt)
                {
                    _orders.RemoveAt(i);
                    _failedOrders.Add(order);
                }
            }
            // The interval of the difficulty now: the wait shrinks during the shift.
            var interval = _ramp.IntervalAt(Difficulty);
            _timer += Time.deltaTime;
            if (_timer < interval)
            {
                return;
            }
            // Full: the timer waits at the end, so a new order comes as soon as a place is free.
            if (TryAddOrder())
            {
                _timer -= interval;
            }
            else
            {
                _timer = interval;
            }
        }

        private bool TryAddOrder()
        {
            var difficulty = Difficulty;
            if (_orders.Count >= _ramp.MaxOpenAt(difficulty))
            {
                return false;
            }
            var item = PickItem(difficulty);
            if (item == null)
            {
                return false;
            }
            var patience = _ramp.PatienceAt(difficulty);
            _orders.Add(new Order
            {
                Id = _nextId++,
                ItemId = item.Id,
                CreatedAt = NetworkManager.ServerTime.Time,
                PatienceDuration = patience,
                ExpiresAt = _patienceClock.Value + patience,
            });
            return true;
        }

        /// <summary>
        /// A random menu item, each one weighted by its weight at this difficulty. If every weight is 0 here, any
        /// orderable item (so a badly set menu still gives orders).
        /// </summary>
        private ItemDefinition PickItem(float difficulty)
        {
            var total = 0f;
            var count = 0;
            foreach (var entry in _menu)
            {
                if (IsOrderable(entry))
                {
                    total += entry.WeightAt(difficulty);
                    count++;
                }
            }
            if (count == 0)
            {
                Debug.LogWarning("[Orders] The menu has no orderable item.", this);
                return null;
            }
            if (total <= 0f)
            {
                var index = Random.Range(0, count);
                foreach (var entry in _menu)
                {
                    if (IsOrderable(entry) && index-- == 0)
                    {
                        return entry.Item;
                    }
                }
                return null;
            }
            var pick = Random.value * total;
            OrderMenuEntry last = null;
            foreach (var entry in _menu)
            {
                if (!IsOrderable(entry))
                {
                    continue;
                }
                var weight = entry.WeightAt(difficulty);
                if (weight <= 0f)
                {
                    continue;
                }
                last = entry;
                pick -= weight;
                if (pick < 0f)
                {
                    return entry.Item;
                }
            }
            // Rounding: the pick landed on the very end.
            return last?.Item;
        }

        private bool IsOrderable(OrderMenuEntry entry)
        {
            return entry != null && entry.Item != null && _itemDatabase != null && _itemDatabase.TryGet(entry.Item.Id, out _);
        }

        // ---------- Every machine ----------

        private void HandleCoinsChanged(int previous, int current) => CoinsChanged?.Invoke(current);

        private void HandleCompletionsChanged(NetworkListEvent<Order> change)
        {
            if (change.Type == NetworkListEvent<Order>.EventType.Add)
            {
                Debug.Log($"[Orders] Completed order #{change.Value.Id}: {Describe(change.Value)}");
                OrderCompleted?.Invoke(change.Value);
            }
        }

        private void HandleFailuresChanged(NetworkListEvent<Order> change)
        {
            if (change.Type == NetworkListEvent<Order>.EventType.Add)
            {
                Debug.Log($"[Orders] Lost order #{change.Value.Id}: {Describe(change.Value)} (patience expired, {FailedCount} failed)");
                OrderLost?.Invoke(change.Value);
            }
        }

        private void HandleListChanged(NetworkListEvent<Order> change)
        {
            switch (change.Type)
            {
                case NetworkListEvent<Order>.EventType.Add:
                    _known.Add(change.Value);
                    Debug.Log($"[Orders] New order #{change.Value.Id}: {Describe(change.Value)} ({_orders.Count}/{MaxOrders} open, " +
                              $"difficulty {Difficulty:F2}, patience {change.Value.PatienceDuration:F0} s)");
                    OrderAdded?.Invoke(change.Value);
                    break;
                case NetworkListEvent<Order>.EventType.Remove:
                case NetworkListEvent<Order>.EventType.RemoveAt:
                    _known.Remove(change.Value);
                    Debug.Log($"[Orders] Order #{change.Value.Id} ({Describe(change.Value)}) is gone ({_orders.Count}/{MaxOrders} open)");
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

        [ContextMenu("Debug/Deliver oldest requested item (no crafting)")]
        public void DebugDeliverOldest()
        {
            if (!IsServer || !IsSpawned || Count == 0)
            {
                Debug.Log("[Orders] Delivery test needs a host and an open order.", this);
                return;
            }
            Debug.Log($"[Orders] Delivery accepted: {TryDeliverItem(_orders[0].ItemId)}; team coins: {TeamCoins}", this);
        }

        [ContextMenu("Debug/Deliver selected test item")]
        public void DebugDeliverSelected()
        {
            var id = _debugDeliveryItem != null ? _debugDeliveryItem.Id : ItemDefinition.NoneId;
            Debug.Log($"[Orders] Test item {id}, accepted: {TryDeliverItem(id)}; team coins: {TeamCoins}", this);
        }

        [ContextMenu("Debug/Try invalid delivery")]
        public void DebugDeliverInvalid()
        {
            Debug.Log($"[Orders] Invalid delivery accepted: {TryDeliverItem(ItemDefinition.NoneId)}; team coins: {TeamCoins}", this);
        }

        [ContextMenu("Debug/Log orders and patience")]
        private void DebugLogPatience()
        {
            if (!IsSpawned)
            {
                return;
            }
            Debug.Log($"[Orders] {Count} open, {FailedCount} failed.");
            foreach (var order in _orders)
            {
                Debug.Log($"[Orders] #{order.Id}: {GetRemainingPatience(order):F1}s / {order.PatienceDuration:F1}s left");
            }
        }

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
            var startWeight = 0f;
            foreach (var entry in _menu)
            {
                if (entry == null || entry.Item == null)
                {
                    Debug.LogWarning($"[Orders] '{name}': empty entry in the menu.", this);
                }
                else if (_itemDatabase != null && !_itemDatabase.TryGet(entry.Item.Id, out _))
                {
                    Debug.LogWarning($"[Orders] '{name}': {entry.Item.name} is not in the item database.", this);
                }
                else
                {
                    startWeight += entry.WeightStart;
                }
            }
            if (_menu.Count > 0 && startWeight <= 0f)
            {
                Debug.LogWarning($"[Orders] '{name}': every start weight is 0, the first orders are picked at random.", this);
            }
        }
    }
}
