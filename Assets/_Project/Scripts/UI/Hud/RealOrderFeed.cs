using System.Collections.Generic;
using OverSmithed.Crafting;
using OverSmithed.Levels;
using OverSmithed.Orders;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>Displays the replicated orders after all network changes for the frame have been applied.</summary>
    public class RealOrderFeed : MonoBehaviour
    {
        [SerializeField] private OrderCardsHud _hud;
        [SerializeField] private RecipeDatabase _recipeDatabase;

        private readonly Dictionary<int, (Order Order, OrderCard Card)> _cards = new();
        private readonly HashSet<int> _active = new();
        private readonly List<int> _removed = new();
        private OrderManager _manager;

        private void OnDisable()
        {
            Clear();
            _manager = null;
        }

        private void Clear()
        {
            _cards.Clear();
            if (_hud != null) _hud.Clear();
        }

        private void LateUpdate()
        {
            var manager = OrderManager.Current;
            if (_manager != manager)
            {
                Clear();
                _manager = manager;
            }
            if (manager == null || !manager.IsSpawned || _hud == null) return;
            if (ShiftTimer.Current != null && ShiftTimer.Current.State == ShiftState.Countdown)
            {
                Clear();
                return;
            }

            _active.Clear();
            for (var i = 0; i < manager.Count; i++) _active.Add(manager[i].Id);
            _removed.Clear();
            foreach (var pair in _cards)
            {
                if (!_active.Contains(pair.Key))
                {
                    if (manager.WasCompleted(pair.Value.Order))
                    {
                        _hud.Remove(pair.Value.Card, delivered: true);
                    }
                    else if (manager.WasLost(pair.Value.Order))
                    {
                        pair.Value.Card.Patience = 0f;
                        _hud.Remove(pair.Value.Card, delivered: false);
                    }
                    else
                    {
                        // Reset or manual removal: no success or failure to show.
                        _hud.Discard(pair.Value.Card);
                    }
                    _removed.Add(pair.Key);
                }
            }
            foreach (var id in _removed) _cards.Remove(id);

            for (var i = 0; i < manager.Count; i++)
            {
                var order = manager[i];
                // A restart may reuse ids even with no countdown.
                if (_cards.TryGetValue(order.Id, out var entry) && !entry.Order.Equals(order))
                {
                    _hud.Discard(entry.Card);
                    _cards.Remove(order.Id);
                }
                if (!_cards.TryGetValue(order.Id, out entry))
                {
                    var item = manager.GetItem(order);
                    if (item == null) continue;
                    entry = (order, _hud.Add(OrderCardData.FromRecipe(item, _recipeDatabase)));
                    _cards.Add(order.Id, entry);
                }
                entry.Card.Patience = manager.GetPatienceNormalized(order);
            }
        }
    }
}
