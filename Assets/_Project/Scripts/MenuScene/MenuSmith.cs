using UnityEngine;

namespace OverSmithed.MenuScene
{
    /// <summary>
    /// An apprentice working in the 3D scene behind the main menu. Starts its Animator state (with an offset so
    /// two smiths don't move together) and, when the hammer clip says "OnHammerHit" (animation event), throws a
    /// burst of sparks and flashes a light. Decor only: local, nothing on the network.
    /// Put it on the same object as the Animator (animation events are sent to that object).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class MenuSmith : MonoBehaviour
    {
        [Tooltip("Animator state played at start (MenuSmiths controller: Hammer, Stoke).")]
        [SerializeField] private string _startState = "Hammer";
        [Tooltip("Where the state starts (0..1 of the clip).")]
        [SerializeField, Range(0f, 1f)] private float _startOffset;

        [Header("Hammer hit (optional)")]
        [SerializeField] private ParticleSystem _sparks;
        [SerializeField, Min(0)] private int _sparkCount = 14;
        [Tooltip("Light that flashes on each hit.")]
        [SerializeField] private Light _hitLight;
        [SerializeField, Min(0f)] private float _flashIntensity = 4f;
        [SerializeField, Min(0.01f)] private float _flashTime = 0.18f;
        [Tooltip("Squashes on each hit (the hot ingot on the anvil).")]
        [SerializeField] private Transform _hitTarget;
        [SerializeField] private float _hitSquash = 0.25f;

        private float _flash;
        private Vector3 _targetScale;

        private void Start()
        {
            if (!string.IsNullOrEmpty(_startState))
            {
                GetComponent<Animator>().Play(_startState, 0, _startOffset);
            }
            if (_hitLight != null)
            {
                _hitLight.intensity = 0f;
            }
            if (_hitTarget != null)
            {
                _targetScale = _hitTarget.localScale;
            }
        }

        private void Update()
        {
            if (_flash <= 0f)
            {
                return;
            }
            _flash = Mathf.Max(0f, _flash - Time.deltaTime / _flashTime);
            if (_hitLight != null)
            {
                _hitLight.intensity = _flashIntensity * _flash * _flash;
            }
            if (_hitTarget != null)
            {
                var squash = _hitSquash * _flash;
                _hitTarget.localScale = Vector3.Scale(_targetScale, new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f));
            }
        }

        // Called by the animation event of Menu_HammerLoop.
        private void OnHammerHit()
        {
            if (_sparks != null)
            {
                _sparks.Emit(_sparkCount);
            }
            _flash = 1f;
        }
    }
}
