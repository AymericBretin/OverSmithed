using UnityEngine;

namespace OverSmithed.Effects
{
    /// <summary>
    /// A short squash and stretch of a model (a crate when a player takes from it, a counter when an item lands
    /// on it). Looks only: local to each machine, nothing on the network. The model should have its pivot on the
    /// floor so it squashes down onto it.
    /// </summary>
    public class SquashBounce : MonoBehaviour
    {
        [Tooltip("The model that squashes. Empty = the child named \"Model\".")]
        [SerializeField] private Transform _target;
        [Tooltip("How much it squashes (0.1 = 10 % lower at most, and a bit wider).")]
        [SerializeField] private float _amount = 0.1f;
        [Tooltip("Time (seconds) of the whole bounce.")]
        [SerializeField] private float _duration = 0.25f;

        private Vector3 _restScale;
        private float _elapsed = float.MaxValue;
        private float _delay;

        private void Awake()
        {
            if (_target == null)
            {
                _target = transform.Find("Model");
            }
            if (_target == null)
            {
                Debug.LogWarning("[SquashBounce] No model to squash.", this);
                enabled = false;
                return;
            }
            _restScale = _target.localScale;
        }

        private void OnDisable()
        {
            if (_target != null)
            {
                _target.localScale = _restScale;
            }
            _elapsed = float.MaxValue;
        }

        /// <summary>Starts the bounce, after <paramref name="delay"/> seconds (for example when a flying item lands).</summary>
        public void Play(float delay = 0f)
        {
            if (!isActiveAndEnabled || _duration <= 0f)
            {
                return;
            }
            _delay = delay;
            _elapsed = 0f;
        }

        private void LateUpdate()
        {
            if (_elapsed >= _duration)
            {
                return;
            }
            if (_delay > 0f)
            {
                _delay -= Time.deltaTime;
                return;
            }
            _elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(_elapsed / _duration);
            // Down first, then a smaller stretch up, fading out.
            var wave = Mathf.Sin(t * Mathf.PI * 2f) * (1f - t) * _amount;
            _target.localScale = Vector3.Scale(_restScale, new Vector3(1f + wave * 0.5f, 1f - wave, 1f + wave * 0.5f));
        }
    }
}
