using OverSmithed.UI;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// The progress bar above a station (World Space canvas turned to the camera, kit <see cref="ProgressBar"/>).
    /// Hidden while nothing is being worked; pops in when the work starts, follows <see cref="Station.Progress"/>, and
    /// on completion gives a last pop at full and shrinks away. Local look only, every machine reads the station.
    /// </summary>
    public class StationProgressBar : MonoBehaviour
    {
        [SerializeField] private Station _station;
        [SerializeField] private ProgressBar _bar;
        [Tooltip("What grows and shrinks (the bar itself).")]
        [SerializeField] private RectTransform _scaled;
        [SerializeField, Min(0.01f)] private float _showTime = 0.18f;
        [SerializeField, Min(0.01f)] private float _hideTime = 0.12f;
        [SerializeField, Min(0.01f)] private float _doneTime = 0.35f;
        [SerializeField, Min(1f)] private float _donePop = 1.25f;

        private float _shown;      // 0 hidden .. 1 fully shown
        private float _doneTimer;  // > 0 while the "done" pop plays
        private float _value;      // shown value: slides up to the real progress (a strike adds a step)

        private void Awake()
        {
            if (_scaled != null)
            {
                _scaled.localScale = Vector3.zero;
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
        }

        private void HandleWorkCompleted(Station station)
        {
            _doneTimer = _doneTime;
        }

        private void LateUpdate()
        {
            if (_station == null || _bar == null || _scaled == null)
            {
                return;
            }
            var deltaTime = Time.unscaledDeltaTime;
            if (_doneTimer > 0f)
            {
                // Full bar: a quick swell, then it shrinks away.
                _doneTimer = Mathf.Max(0f, _doneTimer - deltaTime);
                var t = 1f - _doneTimer / _doneTime;
                _bar.Value = 1f;
                _value = 0f;
                var scale = t < 0.35f
                    ? Mathf.Lerp(1f, _donePop, t / 0.35f)
                    : Mathf.Lerp(_donePop, 0f, (t - 0.35f) / 0.65f);
                _scaled.localScale = Vector3.one * scale;
                _shown = 0f;
                return;
            }

            // Not spawned (scene unloading, network stopped): nothing to show.
            var wanted = _station.IsSpawned && _station.CurrentRecipe != null
                && (_station.IsWorking || _station.Progress > 0f);
            _shown = Mathf.MoveTowards(_shown, wanted ? 1f : 0f, deltaTime / (wanted ? _showTime : _hideTime));
            var progress = _station.IsSpawned ? _station.Progress : 0f;
            // Going up: slide there quickly (a strike at the anvil jumps one step). Going down (new item): at once.
            _value = progress < _value ? progress : Mathf.Lerp(_value, progress, 1f - Mathf.Exp(-deltaTime * 20f));
            _bar.Value = _value;
            // Back ease on the way in (small overshoot), straight on the way out.
            var shown = wanted ? 1f + 2.4f * Mathf.Pow(_shown - 1f, 3f) + 1.4f * Mathf.Pow(_shown - 1f, 2f) : _shown;
            _scaled.localScale = Vector3.one * Mathf.Max(0f, shown);
        }
    }
}
