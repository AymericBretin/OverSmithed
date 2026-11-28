using OverSmithed.Effects;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// The fire of an overheating station (furnace): flames, smoke and a strong red light while it is on fire, with a
    /// burst + bounce when it catches fire and a puff of smoke when the fire is put out. (The warning icon before it
    /// burns is <see cref="StationStatusIcon"/>.)
    /// Looks only, local to each machine: it reads <see cref="StationOverheat.Phase"/> (synced by the host), so every
    /// machine sees the warning and the fire at the same time without extra network data.
    /// </summary>
    public class FurnaceFireEffects : MonoBehaviour
    {
        [SerializeField] private StationOverheat _overheat;

        [Header("Fire")]
        [Tooltip("Time (seconds) for the fire to grow when it starts.")]
        [SerializeField, Min(0.01f)] private float _fireUpTime = 0.3f;
        [Tooltip("Time (seconds) for the fire to die when it is put out.")]
        [SerializeField, Min(0.01f)] private float _fireDownTime = 0.5f;
        [Tooltip("Flame particles (lid, door...). Each one gets the rate below.")]
        [SerializeField] private ParticleSystem[] _flames = new ParticleSystem[0];
        [Tooltip("Flames per second while on fire (for each flame system).")]
        [SerializeField, Min(0f)] private float _flameRate = 18f;
        [Tooltip("Dark smoke particles (pipe, above the fire...). Each one gets the rate below.")]
        [SerializeField] private ParticleSystem[] _smoke = new ParticleSystem[0];
        [Tooltip("Dark smoke puffs per second while on fire (for each smoke system).")]
        [SerializeField, Min(0f)] private float _smokeRate = 7f;
        [SerializeField] private LightFlicker _light;
        [Tooltip("Average light intensity while on fire.")]
        [SerializeField, Min(0f)] private float _lightIntensity = 4f;

        [Header("Catch fire / put out")]
        [SerializeField] private SquashBounce _bounce;
        [SerializeField, Min(0)] private int _igniteFlames = 20;
        [SerializeField, Min(0)] private int _igniteSmoke = 6;
        [Tooltip("Dark smoke puffs when the fire is put out.")]
        [SerializeField, Min(0)] private int _putOutSmoke = 10;

        private Light _lightComponent;
        private float _fire;       // 0 no fire .. 1 full fire

        private void Awake()
        {
            if (_light != null)
            {
                _lightComponent = _light.GetComponent<Light>();
            }
            Apply(0f);
        }

        private void OnEnable()
        {
            if (_overheat != null)
            {
                _overheat.PhaseChanged += HandlePhaseChanged;
            }
        }

        private void OnDisable()
        {
            if (_overheat != null)
            {
                _overheat.PhaseChanged -= HandlePhaseChanged;
            }
            _fire = 0f;
            Apply(0f);
        }

        private void HandlePhaseChanged(StationOverheat overheat, OverheatPhase previous, OverheatPhase current)
        {
            if (current == OverheatPhase.OnFire)
            {
                if (_bounce != null)
                {
                    _bounce.Play();
                }
                Emit(_flames, _igniteFlames);
                Emit(_smoke, _igniteSmoke);
            }
            else if (previous == OverheatPhase.OnFire)
            {
                Emit(_smoke, _putOutSmoke);
            }
        }

        private void Update()
        {
            if (_overheat == null)
            {
                return;
            }
            var deltaTime = Time.deltaTime;
            var onFire = _overheat.IsOnFire;
            _fire = Mathf.MoveTowards(_fire, onFire ? 1f : 0f, deltaTime / (onFire ? _fireUpTime : _fireDownTime));
            Apply(deltaTime);
        }

        private void Apply(float deltaTime)
        {
            var fire = _fire * _fire * (3f - 2f * _fire);
            SetRate(_flames, _flameRate * fire);
            SetRate(_smoke, _smokeRate * fire);
            if (_light != null)
            {
                _light.BaseIntensity = _lightIntensity * fire;
                _lightComponent.enabled = _light.BaseIntensity > 0.001f;
            }
        }

        private static void Emit(ParticleSystem[] systems, int count)
        {
            foreach (var system in systems)
            {
                if (system != null && count > 0)
                {
                    system.Emit(count);
                }
            }
        }

        private static void SetRate(ParticleSystem[] systems, float rate)
        {
            foreach (var system in systems)
            {
                if (system == null)
                {
                    continue;
                }
                var emission = system.emission;
                emission.rateOverTime = rate;
                if (!system.isPlaying)
                {
                    system.Play();
                }
            }
        }
    }
}
