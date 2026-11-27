using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// The icon above a station (World Space canvas turned to the camera, where the progress bar is):
    /// a green check that pops for a moment when the work is done, and on a station that can overheat (furnace) a red
    /// "!" that blinks before the ready item burns: slowly at first, then faster and faster, and it grows and shakes
    /// in the last moment. The warning hides the check.
    /// Looks only, local to each machine: it reads <see cref="Station.WorkCompleted"/> (an RPC sent to everyone) and
    /// <see cref="StationOverheat.Phase"/> (synced by the host), so no extra network data.
    /// </summary>
    public class StationStatusIcon : MonoBehaviour
    {
        [SerializeField] private Station _station;

        [Header("Done")]
        [Tooltip("The green check badge (what grows and shrinks).")]
        [SerializeField] private RectTransform _doneBadge;
        [Tooltip("Seconds after the end of the work before the check pops (the bar shrinks away first).")]
        [SerializeField, Min(0f)] private float _doneDelay = 0.25f;
        [Tooltip("Seconds the check stays (pop in and out included).")]
        [SerializeField, Min(0.3f)] private float _doneTime = 1.5f;

        [Header("Warning (stations that overheat)")]
        [Tooltip("The red \"!\" badge (what blinks). Leave empty on stations that never overheat.")]
        [SerializeField] private RectTransform _warningBadge;
        [Tooltip("Seconds of one blink at the start of the warning (shown, then hidden).")]
        [SerializeField, Min(0.03f)] private float _blinkSlow = 1f;
        [Tooltip("Seconds of one blink at the very end of the warning.")]
        [SerializeField, Min(0.03f)] private float _blinkFast = 0.07f;
        [Tooltip("How the blinking speeds up over the warning: x = warning 0..1, y = 0 slow .. 1 fast. A curve that " +
                 "stays low then rises keeps it calm for a long time and frantic only at the end.")]
        [SerializeField] private AnimationCurve _blinkSpeedUp = new(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.6f, 0.2f, 0.6f, 0.6f), new Keyframe(1f, 1f, 3f, 0f));
        [Tooltip("Last seconds of the warning when the icon grows and shakes.")]
        [SerializeField, Min(0f)] private float _alarmTime = 1.5f;
        [Tooltip("How much bigger the icon gets at the very end (0.3 = +30 %).")]
        [SerializeField, Min(0f)] private float _alarmGrow = 0.3f;
        [Tooltip("Shake angle (degrees) at the very end.")]
        [SerializeField, Min(0f)] private float _alarmShake = 12f;

        private const float PopTime = 0.15f;

        private float _doneTimer;     // > 0 while the check is shown (counts down)
        private float _blink;         // blink cycles done
        private float _warningShown;  // 0 hidden .. 1 shown

        private void Awake()
        {
            SetScale(_doneBadge, 0f);
            SetScale(_warningBadge, 0f);
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
            _doneTimer = 0f;
            _warningShown = 0f;
            SetScale(_doneBadge, 0f);
            SetScale(_warningBadge, 0f);
        }

        private void HandleWorkCompleted(Station station)
        {
            _doneTimer = _doneDelay + _doneTime;
        }

        private void LateUpdate()
        {
            if (_station == null)
            {
                return;
            }
            var deltaTime = Time.deltaTime;
            var overheat = _station.IsSpawned ? _station.Overheat : null;
            var warning = overheat != null && overheat.IsWarning;
            UpdateWarning(overheat, warning, deltaTime);

            // The check: hidden by the warning, or as soon as the item is taken.
            if (warning || !_station.IsSpawned || _station.Item == null)
            {
                _doneTimer = 0f;
            }
            _doneTimer = Mathf.Max(0f, _doneTimer - deltaTime);
            var shown = _doneTime - _doneTimer + _doneDelay;    // seconds since the check started to pop
            var doneScale = 0f;
            if (_doneTimer > 0f && shown >= _doneDelay)
            {
                var inTime = shown - _doneDelay;
                doneScale = _doneTimer < PopTime
                    ? _doneTimer / PopTime                       // shrinks away at the end
                    : BackEase(Mathf.Clamp01(inTime / PopTime)); // pops in with a small overshoot
            }
            SetScale(_doneBadge, doneScale);
        }

        private void UpdateWarning(StationOverheat overheat, bool warning, float deltaTime)
        {
            if (_warningBadge == null)
            {
                return;
            }
            var on = false;
            var alarm = 0f;
            var period = _blinkSlow;
            if (warning)
            {
                var progress = overheat.WarningProgress;
                period = Mathf.Lerp(_blinkSlow, _blinkFast, Mathf.Clamp01(_blinkSpeedUp.Evaluate(progress)));
                _blink += deltaTime / period;
                on = Mathf.Repeat(_blink, 1f) < 0.6f;
                if (_alarmTime > 0f && overheat.WarningTime > 0f)
                {
                    alarm = Mathf.InverseLerp(1f - _alarmTime / overheat.WarningTime, 1f, progress);
                }
            }
            else
            {
                _blink = 0f;
            }
            // Pop in and out fast enough for the fastest blinks.
            var popTime = Mathf.Min(0.06f, period * 0.2f);
            _warningShown = Mathf.MoveTowards(_warningShown, on ? 1f : 0f, deltaTime / popTime);
            var t = _warningShown;
            var pop = t <= 0f ? 0f : (1f + 0.15f * Mathf.Sin(t * Mathf.PI)) * Mathf.Min(1f, t * 1.5f);
            SetScale(_warningBadge, pop * (1f + _alarmGrow * alarm));
            var shake = Mathf.Sin(Time.time * 50f) * _alarmShake * alarm;
            _warningBadge.localRotation = Quaternion.Euler(0f, 0f, shake);
        }

        // 0 → 1 with a small overshoot (back ease out).
        private static float BackEase(float t)
        {
            const float c = 1.7f;
            var u = t - 1f;
            return 1f + (c + 1f) * u * u * u + c * u * u;
        }

        private static void SetScale(RectTransform target, float scale)
        {
            if (target != null)
            {
                target.localScale = Vector3.one * Mathf.Max(0f, scale);
            }
        }
    }
}
