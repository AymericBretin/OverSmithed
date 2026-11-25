using OverSmithed.Effects;
using UnityEngine;

namespace OverSmithed.Story
{
    /// <summary>A light fades to an intensity (the fire going out). Works with a flickering light too.</summary>
    public class LightFadeStep : CutsceneStep
    {
        [SerializeField] private Light _light;
        [SerializeField, Min(0f)] private float _intensity;
        [SerializeField, Min(0f)] private float _duration = 1f;

        private LightFlicker _flicker;
        private float _from;

        public override float Length => _duration;

        protected override void Begin(bool skipping)
        {
            if (_light == null)
            {
                return;
            }
            _flicker = _light.GetComponent<LightFlicker>();
            _from = _flicker != null ? _flicker.BaseIntensity : _light.intensity;
        }

        protected override void Evaluate(float t, float deltaTime)
        {
            if (_light == null)
            {
                return;
            }
            var value = Mathf.Lerp(_from, _intensity, deltaTime > 0f ? Smooth(t) : 1f);
            if (_flicker != null)
            {
                _flicker.BaseIntensity = value;
            }
            _light.intensity = value;
        }
    }
}
