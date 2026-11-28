using UnityEngine;

namespace OverSmithed.Effects
{
    /// <summary>
    /// A trail of small dust puffs left behind a moving object (a dashing player, a thrown item). The particle system
    /// simulates in world space and emits by distance, so the puffs stay where they were dropped; this component only
    /// turns the emission on and off (<see cref="Emitting"/>), with a small burst when it starts. What decides when
    /// it is on lives in the owner script (<c>PlayerDashTrail</c>, <c>ItemThrowTrail</c>). Looks only, local.
    /// </summary>
    public class MotionTrail : MonoBehaviour
    {
        [SerializeField] private ParticleSystem _particles;
        [Tooltip("Puffs emitted at once when the trail starts.")]
        [SerializeField, Min(0)] private int _startBurst = 4;

        private bool _emitting;

        /// <summary>Turns the trail on (it follows the object) or off (the puffs already dropped fade out).</summary>
        public bool Emitting
        {
            get => _emitting;
            set
            {
                if (_particles == null || value == _emitting)
                {
                    return;
                }
                _emitting = value;
                var emission = _particles.emission;
                emission.enabled = value;
                if (value)
                {
                    if (!_particles.isPlaying)
                    {
                        _particles.Play();
                    }
                    if (_startBurst > 0)
                    {
                        _particles.Emit(_startBurst);
                    }
                }
            }
        }

        private void Awake()
        {
            if (_particles != null)
            {
                var emission = _particles.emission;
                emission.enabled = false;
            }
        }

        private void OnDisable()
        {
            Emitting = false;
        }
    }
}
