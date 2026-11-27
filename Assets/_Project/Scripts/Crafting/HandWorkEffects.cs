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
    /// blows. TAP stations: each strike (<see cref="Station.Struck"/>, seen on every machine) plays one blow; when the
    /// player taps faster than a blow, the pose speeds up to catch up. HOLD stations: the rhythm comes from the
    /// station's replicated work time. Either way every machine shows the same blow at the same moment.
    /// <see cref="Players.PlayerHammerAnimation"/> reads <see cref="AnimationPhase"/> to pose the worker; subclasses
    /// make their effect at the contact of each blow (<see cref="OnContact"/>). Local only: no network data of its own.
    /// </summary>
    public abstract class HandWorkEffects : MonoBehaviour
    {
        private static readonly List<HandWorkEffects> Instances = new();

        /// <summary>Every enabled hand-work effect in the loaded scenes.</summary>
        public static IReadOnlyList<HandWorkEffects> All => Instances;

        [SerializeField] protected Station _station;
        [Tooltip("Tap stations: seconds of one blow after a strike (short, so the hit comes right after the press).")]
        [SerializeField, Min(0.05f)] private float _tapBlowTime = 0.25f;
        [Tooltip("Tap stations: when the player taps faster, the pose catches up within this time (seconds).")]
        [SerializeField, Min(0.02f)] private float _catchUpTime = 0.1f;

        private float _blows;       // tap: blows already animated (1.0 = one blow done)
        private float _target;      // tap: blows asked by the strikes seen
        private int _lastContact = int.MinValue;

        public Station Station => _station;

        /// <summary>The tool the worker holds here.</summary>
        public abstract HandTool Tool { get; }

        /// <summary>Hold stations: seconds of one blow (the animation loop has two blows).</summary>
        protected abstract float HoldBlowTime { get; }

        /// <summary>Moment of a blow (0..1) when the tool hits, matching the animation.</summary>
        protected abstract float ContactPhase { get; }

        /// <summary>0..1 through the animation loop of the worker (two blows per loop).</summary>
        public float AnimationPhase => Mathf.Repeat(Blows * 0.5f, 1f);

        private float Blows => _station.UsesTaps ? _blows : _station.WorkSeconds / HoldBlowTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instances.Clear();

        protected virtual void OnEnable()
        {
            Instances.Add(this);
            _lastContact = int.MinValue;
            if (_station != null)
            {
                _station.Struck += HandleStruck;
            }
        }

        protected virtual void OnDisable()
        {
            Instances.Remove(this);
            if (_station != null)
            {
                _station.Struck -= HandleStruck;
            }
        }

        private void HandleStruck(Station station, int count)
        {
            // Never more than two blows behind: a late or very fast series shows the last blows, not a backlog.
            _target = Mathf.Max(_target, _blows) + count;
            _blows = Mathf.Max(_blows, _target - 2f);
        }

        /// <summary>The tool hits the item (sparks, sawdust...).</summary>
        protected abstract void OnContact();

        protected virtual void Update()
        {
            if (_station == null || !_station.IsSpawned || Time.deltaTime <= 0f)
            {
                return;
            }
            if (_station.UsesTaps)
            {
                if (_blows < _target)
                {
                    var speed = Mathf.Max(1f / _tapBlowTime, (_target - _blows) / _catchUpTime);
                    _blows = Mathf.Min(_target, _blows + speed * Time.deltaTime);
                }
            }
            else if (!_station.IsWorking || _station.CurrentRecipe == null)
            {
                _lastContact = int.MinValue;
                return;
            }

            var contact = Mathf.FloorToInt(Blows - ContactPhase);
            if (_lastContact == int.MinValue)
            {
                // First frame: where we are now, no hit from the past.
                _lastContact = contact;
                return;
            }
            if (contact != _lastContact)
            {
                _lastContact = contact;
                OnContact();
            }
        }
    }
}
