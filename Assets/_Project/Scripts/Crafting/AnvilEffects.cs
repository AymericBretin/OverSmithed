using OverSmithed.Counters;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// Sparks on each hammer beat. The beat follows replicated work progress, so pausing preserves its phase and
    /// every machine uses the same rhythm as PlayerHammerAnimation.
    /// </summary>
    public class AnvilEffects : HandWorkEffects
    {
        [SerializeField] private ParticleSystem _sparks;
        [SerializeField, Min(0.05f)] private float _hitInterval = 0.6f;
        [Tooltip("Contact point in each swing, matching the two contacts in Player_HammerLoop.")]
        [SerializeField, Range(0f, 1f)] private float _hitPhase = 0.56f;
        [SerializeField, Min(1)] private int _sparksPerHit = 22;
        [Tooltip("Height above ItemPoint: just above the hot ingot, so the burst is not hidden inside its mesh.")]
        [SerializeField, Min(0f)] private float _impactHeight = 0.18f;
        [SerializeField] private Light _hitLight;
        [SerializeField, Min(0f)] private float _flashIntensity = 2f;

        private int _lastHit = -1;
        private float _flash;

        public override HandTool Tool => HandTool.Hammer;

        // The clip has two swings. Both the pose and the effects use elapsed WORK time, not local wall time.
        public override float AnimationPhase => Mathf.Repeat(_station.WorkSeconds / (2f * Mathf.Max(0.05f, _hitInterval)), 1f);

        protected override void OnEnable()
        {
            base.OnEnable();
            _lastHit = -1;
            if (_station != null)
            {
                _station.ItemChanged += HandleItemChanged;
                _station.WorkCompleted += HandleWorkCompleted;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _flash = 0f;
            if (_hitLight != null) _hitLight.intensity = 0f;
            if (_station != null)
            {
                _station.ItemChanged -= HandleItemChanged;
                _station.WorkCompleted -= HandleWorkCompleted;
            }
            if (_sparks != null)
            {
                _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void HandleItemChanged(Counter counter)
        {
            _lastHit = -1;
        }

        private void HandleWorkCompleted(Station station)
        {
            EmitHit();
            _lastHit = -1;
        }

        private void Update()
        {
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime / 0.12f);
            if (_hitLight != null) _hitLight.intensity = _flashIntensity * _flash * _flash;
            if (_station == null || !_station.IsSpawned || !_station.IsWorking || Time.deltaTime <= 0f)
            {
                return;
            }
            var recipe = _station.CurrentRecipe;
            if (recipe == null)
            {
                return;
            }
            var hit = Mathf.FloorToInt(_station.WorkSeconds / Mathf.Max(0.05f, _hitInterval) - _hitPhase);
            if (hit != _lastHit)
            {
                _lastHit = hit;
                // A late observer shows the current hit, never a backlog of old sparks.
                EmitHit();
            }
        }

        private void EmitHit()
        {
            if (_sparks == null)
            {
                return;
            }
            _sparks.transform.position = _station.ItemPoint.position + Vector3.up * _impactHeight;
            _sparks.Emit(_sparksPerHit);
            _flash = 1f;
        }
    }
}
