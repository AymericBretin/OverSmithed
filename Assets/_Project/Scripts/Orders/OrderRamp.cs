using System;
using OverSmithed.Items;
using UnityEngine;

namespace OverSmithed.Orders
{
    /// <summary>
    /// The difficulty of a level's orders over its shift (CR-16). One curve gives the difficulty (0 = easy, 1 = hard)
    /// from the shift progress (0 = "Go!", 1 = end); each setting goes from its start value to its end value with it.
    /// Read by the host only: the clients just get the orders it creates.
    /// </summary>
    [Serializable]
    public class OrderRamp
    {
        [Tooltip("Difficulty (0 = start values, 1 = end values) along the shift (0 = \"Go!\", 1 = time's up).")]
        public AnimationCurve Difficulty = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Seconds between two new orders: x at difficulty 0, y at difficulty 1 (game time: a local pause stops it).")]
        public Vector2 Interval = new(28f, 14f);

        [Tooltip("Open orders at most: x at difficulty 0, y at difficulty 1. When full, the next order comes as soon as one is gone.")]
        public Vector2Int MaxOpen = new(3, 4);

        [Tooltip("Patience of a new order in seconds: x at difficulty 0, y at difficulty 1. An order keeps the patience it got.")]
        public Vector2 Patience = new(70f, 50f);

        /// <summary>Difficulty 0..1 at a shift progress 0..1.</summary>
        public float DifficultyAt(float progress)
        {
            return Difficulty != null && Difficulty.length > 0 ? Mathf.Clamp01(Difficulty.Evaluate(Mathf.Clamp01(progress))) : 0f;
        }

        public float IntervalAt(float difficulty) => Mathf.Max(0.5f, Mathf.Lerp(Interval.x, Interval.y, difficulty));

        public int MaxOpenAt(float difficulty) => Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(MaxOpen.x, MaxOpen.y, difficulty)));

        public float PatienceAt(float difficulty) => Mathf.Max(0.1f, Mathf.Lerp(Patience.x, Patience.y, difficulty));
    }

    /// <summary>
    /// An item of the level's menu and how often it is ordered: its weight goes from <see cref="WeightStart"/> to
    /// <see cref="WeightEnd"/> with the difficulty. Weight 0 = never ordered at that point (a hard item can start at 0).
    /// </summary>
    [Serializable]
    public class OrderMenuEntry
    {
        public ItemDefinition Item;
        [Tooltip("How often it is ordered at difficulty 0, compared with the other items.")]
        [Min(0f)] public float WeightStart = 1f;
        [Tooltip("How often it is ordered at difficulty 1, compared with the other items.")]
        [Min(0f)] public float WeightEnd = 1f;

        public float WeightAt(float difficulty) => Mathf.Max(0f, Mathf.Lerp(WeightStart, WeightEnd, difficulty));
    }
}
