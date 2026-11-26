using OverSmithed.Effects;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// The furnace's look while it heats: glowing door slots, a flickering orange light, smoke from the pipe, embers
    /// from the door and a slow "breathing" of the stove; a burst of sparks + smoke + a bounce when the work is done.
    /// Looks only, local to each machine: it reads <see cref="Station.IsWorking"/> (already synced by the station) and
    /// <see cref="Station.WorkCompleted"/> (an RPC sent to everyone), so every machine shows the same thing at the
    /// same time without any extra network data.
    /// </summary>
    public class FurnaceEffects : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Station _station;

        [Header("Heat")]
        [Tooltip("Time (seconds) to heat up when the work starts.")]
        [SerializeField, Min(0.01f)] private float _heatUpTime = 0.4f;
        [Tooltip("Time (seconds) to cool down when the work stops.")]
        [SerializeField, Min(0.01f)] private float _coolDownTime = 0.8f;

        [Header("Door glow")]
        [Tooltip("The stove renderer. Its material has an emission mask that lights only the black door slots " +
                 "(Materials/Furnace_Stove); this script drives the emission color on its own copy of it.")]
        [SerializeField] private Renderer _stoveRenderer;
        [Tooltip("Color of the slots when hot (values above 1 make the bloom shine).")]
        [SerializeField, ColorUsage(false, true)] private Color _hotColor = new(2.2f, 0.6f, 0.12f);
        [Tooltip("How much the glow flickers (0.15 = +-15 %).")]
        [SerializeField, Range(0f, 1f)] private float _glowFlicker = 0.15f;

        [Header("Light")]
        [SerializeField] private LightFlicker _light;
        [Tooltip("Average light intensity when fully hot (a bit more as the work ends).")]
        [SerializeField, Min(0f)] private float _lightIntensity = 2.5f;

        [Header("Particles")]
        [SerializeField] private ParticleSystem _smoke;
        [Tooltip("Smoke puffs per second when fully hot.")]
        [SerializeField, Min(0f)] private float _smokeRate = 4f;
        [SerializeField] private ParticleSystem _embers;
        [Tooltip("Embers per second when fully hot.")]
        [SerializeField, Min(0f)] private float _emberRate = 3f;

        [Header("Breathing")]
        [Tooltip("What breathes (the stove model, not the root: the root bounce uses \"Model\").")]
        [SerializeField] private Transform _breathing;
        [Tooltip("How much it grows when it breathes in (0.03 = 3 % taller).")]
        [SerializeField, Min(0f)] private float _breathAmount = 0.03f;
        [Tooltip("Time (seconds) of one breath.")]
        [SerializeField, Min(0.05f)] private float _breathPeriod = 0.9f;

        [Header("Done")]
        [SerializeField] private SquashBounce _bounce;
        [SerializeField, Min(0)] private int _doneEmbers = 24;
        [SerializeField, Min(0)] private int _doneSmoke = 5;
        [Tooltip("Extra light intensity right when the work is done (fades in 0.4 s).")]
        [SerializeField, Min(0f)] private float _doneFlash = 3f;

        private Material _stoveMaterial;   // own copy (the highlight uses a property block on the same renderer)
        private Light _lightComponent;
        private Vector3 _breathingRest;
        private float _heat;       // 0 cold .. 1 fully hot
        private float _breathTime;
        private float _flash;      // 1 right after a completion, fades to 0
        private float _seed;

        private void Awake()
        {
            if (_stoveRenderer != null)
            {
                _stoveMaterial = _stoveRenderer.material;
            }
            _seed = Random.value * 100f;
            if (_light != null)
            {
                _lightComponent = _light.GetComponent<Light>();
            }
            if (_breathing != null)
            {
                _breathingRest = _breathing.localScale;
            }
            ApplyHeat(0f, 1f);
        }

        private void OnDestroy()
        {
            if (_stoveMaterial != null)
            {
                Destroy(_stoveMaterial);
            }
        }

        private void OnEnable()
        {
            if (_station != null)
            {
                _station.WorkCompleted += HandleWorkCompleted;
            }
        }

        private void OnDisable()
        {
            if (_station != null)
            {
                _station.WorkCompleted -= HandleWorkCompleted;
            }
            _heat = 0f;
            _flash = 0f;
            ApplyHeat(0f, 1f);
        }

        private void HandleWorkCompleted(Station station)
        {
            _flash = 1f;
            if (_bounce != null)
            {
                _bounce.Play();
            }
            if (_embers != null && _doneEmbers > 0)
            {
                _embers.Emit(_doneEmbers);
            }
            if (_smoke != null && _doneSmoke > 0)
            {
                _smoke.Emit(_doneSmoke);
            }
        }

        private void Update()
        {
            if (_station == null)
            {
                return;
            }
            var deltaTime = Time.deltaTime;
            var hot = _station.IsSpawned && _station.IsWorking;
            _heat = Mathf.MoveTowards(_heat, hot ? 1f : 0f, deltaTime / (hot ? _heatUpTime : _coolDownTime));
            _flash = Mathf.MoveTowards(_flash, 0f, deltaTime / 0.4f);
            var progress = _station.IsSpawned ? _station.Progress : 0f;
            ApplyHeat(deltaTime, progress);
        }

        private void ApplyHeat(float deltaTime, float progress)
        {
            // Smooth in and out (no hard start).
            var heat = _heat * _heat * (3f - 2f * _heat);

            // Door slots: black when cold, orange when hot, with a soft flicker.
            if (_stoveMaterial != null)
            {
                var flicker = 1f + (Mathf.PerlinNoise(Time.time * 6f + _seed, 0.5f) * 2f - 1f) * _glowFlicker;
                var glow = Mathf.Clamp01(heat + _flash * 0.5f);
                _stoveMaterial.SetColor(EmissionColorId, _hotColor * (flicker * glow));
            }

            // Light: brighter as the work ends, plus a flash on completion.
            if (_light != null)
            {
                _light.BaseIntensity = _lightIntensity * heat * (0.75f + 0.25f * progress) + _doneFlash * _flash;
                _lightComponent.enabled = _light.BaseIntensity > 0.001f;
            }

            SetRate(_smoke, _smokeRate * heat);
            SetRate(_embers, _emberRate * heat);

            // Breathing: taller and a bit thinner, then back. Faster as the work ends.
            if (_breathing != null)
            {
                if (heat > 0.001f)
                {
                    _breathTime += deltaTime * (1f + 0.5f * progress) / _breathPeriod;
                    var wave = (0.5f - 0.5f * Mathf.Cos(_breathTime * Mathf.PI * 2f)) * _breathAmount * heat;
                    _breathing.localScale = Vector3.Scale(_breathingRest,
                        new Vector3(1f - wave * 0.5f, 1f + wave, 1f - wave * 0.5f));
                }
                else
                {
                    _breathTime = 0f;
                    _breathing.localScale = _breathingRest;
                }
            }
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            if (system == null)
            {
                return;
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
