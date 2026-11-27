using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>The tool a player uses on a hand-work station (picks the animation and the prop in the hand).</summary>
    public enum HandTool : byte
    {
        Hammer = 0,
        Saw = 1,
    }

    /// <summary>
    /// The looks of a station worked by hand (anvil, workbench): which tool the worker uses and the rhythm of the
    /// strokes. The rhythm comes from the station's replicated work time, so every machine shows the same stroke
    /// at the same moment and a pause keeps the phase. <see cref="Players.PlayerHammerAnimation"/> reads it to pose
    /// the worker. Local only: no network data of its own.
    /// </summary>
    public abstract class HandWorkEffects : MonoBehaviour
    {
        private static readonly List<HandWorkEffects> Instances = new();

        /// <summary>Every enabled hand-work effect in the loaded scenes.</summary>
        public static IReadOnlyList<HandWorkEffects> All => Instances;

        [SerializeField] protected Station _station;

        public Station Station => _station;

        /// <summary>The tool the worker holds here.</summary>
        public abstract HandTool Tool { get; }

        /// <summary>0..1 through the animation loop of the worker, from the work time.</summary>
        public abstract float AnimationPhase { get; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instances.Clear();

        protected virtual void OnEnable() => Instances.Add(this);

        protected virtual void OnDisable() => Instances.Remove(this);
    }
}
