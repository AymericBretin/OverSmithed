using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// TEMPORARY (UI-7): fills the order cards with fake orders so the HUD can be seen and tuned before the
    /// order system exists. Local to each machine, nothing goes on the network. UI-10 (#54) replaces it with
    /// the real orders. Right-click the component for "Add order", "Add one of each recipe", "Deliver oldest",
    /// "Fail oldest".
    /// </summary>
    public class FakeOrderFeed : MonoBehaviour
    {
        private class FakeOrder
        {
            public OrderCard Card;
            public float Duration;
            public float Elapsed;
            /// <summary>Time at which the fake players deliver it, or -1: they never do (the order is lost).</summary>
            public float DeliverAt;
        }

        [SerializeField] private OrderCardsHud _hud;
        [SerializeField] private List<OrderCardData> _recipes = new();
        [SerializeField] private bool _autoPlay = true;
        [SerializeField] private int _ordersAtStart = 2;
        [SerializeField] private int _maxOrders = 5;
        [Tooltip("Seconds between two new orders (random between x and y).")]
        [SerializeField] private Vector2 _spawnInterval = new(6f, 12f);
        [Tooltip("Patience of an order in seconds (random between x and y).")]
        [SerializeField] private Vector2 _patience = new(30f, 60f);
        [Tooltip("Chance that an order is delivered before its patience runs out.")]
        [SerializeField, Range(0f, 1f)] private float _deliverChance = 0.5f;

        private readonly List<FakeOrder> _orders = new();
        private float _nextSpawn;

        private void Start()
        {
            if (!_autoPlay)
            {
                return;
            }
            for (var i = 0; i < _ordersAtStart; i++)
            {
                AddOrder();
            }
            _nextSpawn = Time.time + Random.Range(_spawnInterval.x, _spawnInterval.y);
        }

        private void Update()
        {
            for (var i = _orders.Count - 1; i >= 0; i--)
            {
                var order = _orders[i];
                order.Elapsed += Time.deltaTime;
                order.Card.Patience = 1f - order.Elapsed / order.Duration;

                if (_autoPlay && order.DeliverAt >= 0f && order.Elapsed >= order.DeliverAt)
                {
                    Finish(i, delivered: true);
                }
                else if (order.Elapsed >= order.Duration)
                {
                    Finish(i, delivered: false);
                }
            }

            if (_autoPlay && Time.time >= _nextSpawn)
            {
                AddOrder();
                _nextSpawn = Time.time + Random.Range(_spawnInterval.x, _spawnInterval.y);
            }
        }

        [ContextMenu("Add order")]
        private void AddOrder()
        {
            if (_hud == null || _recipes.Count == 0 || _orders.Count >= _maxOrders)
            {
                return;
            }
            var duration = Random.Range(_patience.x, _patience.y);
            _orders.Add(new FakeOrder
            {
                Card = _hud.Add(_recipes[Random.Range(0, _recipes.Count)]),
                Duration = duration,
                DeliverAt = Random.value < _deliverChance ? duration * Random.Range(0.3f, 0.9f) : -1f,
            });
        }

        /// <summary>Shows one card of every recipe (ignores the max), to check how each one looks.</summary>
        [ContextMenu("Add one of each recipe")]
        private void AddEachRecipe()
        {
            if (_hud == null)
            {
                return;
            }
            foreach (var recipe in _recipes)
            {
                var duration = Random.Range(_patience.x, _patience.y);
                _orders.Add(new FakeOrder { Card = _hud.Add(recipe), Duration = duration, DeliverAt = -1f });
            }
        }

        [ContextMenu("Deliver oldest")]
        private void DeliverOldest()
        {
            if (_orders.Count > 0)
            {
                Finish(0, delivered: true);
            }
        }

        [ContextMenu("Fail oldest")]
        private void FailOldest()
        {
            if (_orders.Count > 0)
            {
                Finish(0, delivered: false);
            }
        }

        private void Finish(int index, bool delivered)
        {
            _hud.Remove(_orders[index].Card, delivered);
            _orders.RemoveAt(index);
        }
    }
}
