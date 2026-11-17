using OverSmithed.Levels;
using TMPro;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// Big words in the middle of the screen for the shift: "3", "2", "1" during the countdown, "Go!" when it starts,
    /// "Time's up!" when it ends. Each word pops in (big, then its normal size) and fades out. Reads
    /// <see cref="ShiftTimer.Current"/> on every machine: no network data of its own.
    /// </summary>
    public class ShiftAnnouncer : MonoBehaviour
    {
        [SerializeField] private UiTheme _theme;
        [SerializeField] private TMP_Text _text;
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Pops in (its pivot should be its center).")]
        [SerializeField] private RectTransform _scaleTarget;
        [SerializeField] private UiColor _countdownColor = UiColor.Gold;
        [SerializeField] private UiColor _goColor = UiColor.Success;
        [SerializeField] private UiColor _endColor = UiColor.Danger;
        [Tooltip("Size at the start of the pop (1 = normal size).")]
        [SerializeField] private float _popFrom = 1.8f;
        [SerializeField] private float _popTime = 0.25f;
        [Tooltip("Seconds \"Go!\" stays before fading.")]
        [SerializeField] private float _goTime = 0.8f;
        [Tooltip("Seconds \"Time's up!\" stays before fading.")]
        [SerializeField] private float _endTime = 3f;
        [SerializeField] private float _fadeTime = 0.3f;

        private ShiftTimer _shift;
        private ShiftState _lastState;
        private int _lastCountdownSecond = -1;
        private float _age;
        private float _hold;
        private bool _showing;

        private void OnEnable()
        {
            Hide();
        }

        private void Update()
        {
            var shift = ShiftTimer.Current;
            if (shift != _shift)
            {
                // New level (or no shift any more): start fresh.
                _shift = shift;
                _lastState = ShiftState.Waiting;
                _lastCountdownSecond = -1;
                Hide();
            }
            if (shift != null)
            {
                Watch(shift);
            }
            Animate();
        }

        private void Watch(ShiftTimer shift)
        {
            var state = shift.State;
            if (state == ShiftState.Countdown)
            {
                var second = Mathf.CeilToInt(shift.TimeLeft);
                if (second > 0 && second != _lastCountdownSecond)
                {
                    _lastCountdownSecond = second;
                    // Each number fades out just before the next one.
                    Show(second.ToString(), _countdownColor, 0.6f);
                }
            }
            else
            {
                _lastCountdownSecond = -1;
            }
            if (state == _lastState)
            {
                return;
            }
            _lastState = state;
            if (state == ShiftState.Playing)
            {
                Show("Go!", _goColor, _goTime);
            }
            else if (state == ShiftState.Ended)
            {
                Show("Time's up!", _endColor, _endTime);
            }
        }

        private void Show(string word, UiColor color, float hold)
        {
            _text.text = word;
            if (_theme != null)
            {
                _text.color = _theme.GetColor(color);
            }
            _age = 0f;
            _hold = hold;
            _showing = true;
            _text.gameObject.SetActive(true);
        }

        private void Hide()
        {
            _showing = false;
            if (_text != null)
            {
                _text.gameObject.SetActive(false);
            }
        }

        private void Animate()
        {
            if (!_showing)
            {
                return;
            }
            _age += Time.deltaTime;
            // Pop in: from big to the normal size, eased out.
            var popT = _popTime > 0f ? Mathf.Clamp01(_age / _popTime) : 1f;
            var ease = 1f - (1f - popT) * (1f - popT);
            _scaleTarget.localScale = Vector3.one * Mathf.Lerp(_popFrom, 1f, ease);
            // Fade out after the hold time.
            var fadeT = _fadeTime > 0f ? Mathf.Clamp01((_age - _hold) / _fadeTime) : (_age >= _hold ? 1f : 0f);
            _group.alpha = 1f - fadeT;
            if (fadeT >= 1f)
            {
                Hide();
            }
        }
    }
}
