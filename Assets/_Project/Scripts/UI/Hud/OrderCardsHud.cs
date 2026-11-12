using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// The row of order cards at the top of the screen, oldest on the left. New cards slide in from the right;
    /// when a card leaves, the next ones slide left to fill the gap. Cards are placed by code (no layout group)
    /// so they can move smoothly.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class OrderCardsHud : MonoBehaviour
    {
        [SerializeField] private OrderCard _cardPrefab;
        [SerializeField] private float _spacing = 12f;
        [Tooltip("Time to reach the target place (slide in, fill a gap).")]
        [SerializeField] private float _moveSmoothTime = 0.18f;

        private readonly List<OrderCard> _cards = new();

        /// <summary>Cards on screen, oldest first (leaving cards are not in this list).</summary>
        public IReadOnlyList<OrderCard> Cards => _cards;

        private RectTransform Rect => (RectTransform)transform;

        public OrderCard Add(OrderCardData data)
        {
            var card = Instantiate(_cardPrefab, Rect);
            card.Setup(data);
            card.Patience = 1f;
            // Starts just outside the right edge of the screen.
            card.X = Rect.rect.width + _spacing;
            card.Velocity = 0f;
            Place(card);
            _cards.Add(card);
            return card;
        }

        /// <summary>The card plays its exit animation; the others close the gap.</summary>
        public void Remove(OrderCard card, bool delivered)
        {
            if (card == null || !_cards.Remove(card))
            {
                return;
            }
            card.Leave(delivered);
        }

        private void Update()
        {
            var target = 0f;
            foreach (var card in _cards)
            {
                card.X = Mathf.SmoothDamp(card.X, target, ref card.Velocity, _moveSmoothTime);
                Place(card);
                target += card.Width + _spacing;
            }
        }

        private static void Place(OrderCard card)
        {
            card.Rect.anchoredPosition = new Vector2(card.X, 0f);
        }
    }
}
