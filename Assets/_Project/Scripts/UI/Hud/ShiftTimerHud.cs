using OverSmithed.Levels;
using TMPro;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// The shift timer at the bottom right of the screen ("02:47"). Reads <see cref="ShiftTimer.Current"/> on every
    /// machine (the host already shares the time left): no network data of its own. Shows the full duration during the
    /// countdown, turns red near the end and pulses every second in the last seconds. Hidden when the scene has no shift.
    /// </summary>
    public class ShiftTimerHud : MonoBehaviour
    {
        [SerializeField] private UiTheme _theme;
        [Tooltip("Shown only when the scene has a shift.")]
        [SerializeField] private GameObject _content;
        [SerializeField] private TMP_Text _timeText;
        [Tooltip("Grows on each pulse (its pivot should be its center).")]
        [SerializeField] private RectTransform _pulseTarget;
        [SerializeField] private UiColor _normalColor = UiColor.Gold;
        [SerializeField] private UiColor _warningColor = UiColor.Danger;
        [Tooltip("Seconds left when the time turns red.")]
        [SerializeField] private float _warningTime = 30f;
        [Tooltip("Seconds left when the time starts to pulse every second.")]
        [SerializeField] private float _pulseFrom = 10f;
        [SerializeField] private float _pulseScale = 1.3f;
        [SerializeField] private float _pulseDuration = 0.35f;

        private int _shownSecond = -1;
        private float _pulseLeft;

        private void OnDisable()
        {
            _shownSecond = -1;
            _pulseLeft = 0f;
            if (_pulseTarget != null)
            {
                _pulseTarget.localScale = Vector3.one;
            }
        }

        private void Update()
        {
            var shift = ShiftTimer.Current;
            if (_content.activeSelf != (shift != null))
            {
                _content.SetActive(shift != null);
                _shownSecond = -1;
            }
            if (shift == null)
            {
                return;
            }

            var state = shift.State;
            var seconds = state switch
            {
                ShiftState.Playing => shift.TimeLeft,
                ShiftState.Ended => 0f,
                _ => shift.Duration // Waiting, countdown: the whole shift is still ahead.
            };
            var whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
            if (whole != _shownSecond)
            {
                _shownSecond = whole;
                // Fixed-width digits: the text doesn't shake when the seconds change.
                _timeText.text = $"<mspace=0.56em>{whole / 60:00}</mspace>:<mspace=0.56em>{whole % 60:00}</mspace>";
                var warning = state != ShiftState.Waiting && state != ShiftState.Countdown && whole <= _warningTime;
                if (_theme != null)
                {
                    _timeText.color = _theme.GetColor(warning ? _warningColor : _normalColor);
                }
                if (state == ShiftState.Playing && whole > 0 && whole <= _pulseFrom)
                {
                    _pulseLeft = _pulseDuration;
                }
            }

            if (_pulseTarget != null)
            {
                _pulseLeft = Mathf.Max(0f, _pulseLeft - Time.deltaTime);
                var t = _pulseDuration > 0f ? _pulseLeft / _pulseDuration : 0f;
                _pulseTarget.localScale = Vector3.one * (1f + (_pulseScale - 1f) * t * t);
            }
        }
    }
}
