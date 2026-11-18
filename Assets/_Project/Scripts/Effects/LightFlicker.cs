using UnityEngine;

namespace OverSmithed.Effects
{
    /// <summary>
    /// Makes a light flicker like a fire (intensity and a tiny position wobble, smooth noise). Looks only: local,
    /// nothing on the network. The intensity set in the Inspector is the average.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class LightFlicker : MonoBehaviour
    {
        [Tooltip("How much the intensity changes (0.2 = +-20 %).")]
        [SerializeField, Range(0f, 1f)] private float _amount = 0.2f;
        [Tooltip("How fast it flickers.")]
        [SerializeField, Min(0f)] private float _speed = 5f;
        [Tooltip("How far (m) the light wobbles, so shadows move a little.")]
        [SerializeField, Min(0f)] private float _wobble = 0.04f;

        private Light _light;
        private float _baseIntensity;
        private Vector3 _basePosition;
        private float _seed;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _baseIntensity = _light.intensity;
            _basePosition = transform.localPosition;
            _seed = Random.value * 100f;
        }

        private void Update()
        {
            var t = Time.time * _speed + _seed;
            // Two noises: a slow breathing and a fast crackle.
            var noise = Mathf.PerlinNoise(t, 0.3f) * 0.7f + Mathf.PerlinNoise(t * 3.1f, 7.7f) * 0.3f;
            _light.intensity = _baseIntensity * (1f + (noise * 2f - 1f) * _amount);
            transform.localPosition = _basePosition + new Vector3(
                Mathf.PerlinNoise(t * 0.7f, 3.3f) - 0.5f,
                Mathf.PerlinNoise(t * 0.7f, 5.1f) - 0.5f,
                Mathf.PerlinNoise(t * 0.7f, 9.4f) - 0.5f) * (2f * _wobble);
        }
    }
}
