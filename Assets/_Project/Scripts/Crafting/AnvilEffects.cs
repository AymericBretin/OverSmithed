using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// Sparks on each hammer blow (each strike when tapping Use). The blows are timed by <see cref="HandWorkEffects"/>
    /// from what the station replicates, so every machine uses the same rhythm as PlayerHammerAnimation.
    /// </summary>
    public class AnvilEffects : HandWorkEffects
    {
        [SerializeField] private ParticleSystem _sparks;
        [Tooltip("Hold mode only: seconds of one blow.")]
        [SerializeField, Min(0.05f)] private float _hitInterval = 0.6f;
        [Tooltip("Contact point in each swing, matching the two contacts in Player_HammerLoop.")]
        [SerializeField, Range(0f, 1f)] private float _hitPhase = 0.56f;
        [SerializeField, Min(1)] private int _sparksPerHit = 22;
        [Tooltip("Height above ItemPoint: just above the hot ingot, so the burst is not hidden inside its mesh.")]
        [SerializeField, Min(0f)] private float _impactHeight = 0.18f;
        [SerializeField] private Light _hitLight;
        [SerializeField, Min(0f)] private float _flashIntensity = 2f;

        private float _flash;

        public override HandTool Tool => HandTool.Hammer;

        protected override float HoldBlowTime => Mathf.Max(0.05f, _hitInterval);

        protected override float ContactPhase => _hitPhase;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_station != null)
            {
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
                _station.WorkCompleted -= HandleWorkCompleted;
            }
            if (_sparks != null)
            {
                _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void HandleWorkCompleted(Station station)
        {
            EmitHit();
        }

        protected override void OnContact()
        {
            EmitHit();
        }

        protected override void Update()
        {
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime / 0.12f);
            if (_hitLight != null) _hitLight.intensity = _flashIntensity * _flash * _flash;
            base.Update();
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
