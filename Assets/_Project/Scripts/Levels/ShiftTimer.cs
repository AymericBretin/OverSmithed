using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Levels
{
    /// <summary>The steps of a shift (one play of a level).</summary>
    public enum ShiftState : byte
    {
        /// <summary>The level is loading on the machines: nothing started yet.</summary>
        Waiting,
        /// <summary>3, 2, 1...: players can't move yet.</summary>
        Countdown,
        /// <summary>The timer runs: players work.</summary>
        Playing,
        /// <summary>Time is up: players can't move any more.</summary>
        Ended
    }

    /// <summary>
    /// The shift of a level: countdown, then playing while a timer runs down, then ended. One per level scene.
    /// The host runs it (starts the countdown once every machine has loaded the level, counts the time); every machine
    /// reads the same step and time left from two NetworkVariables. Time is counted with Time.deltaTime, so a local
    /// pause (Time.timeScale 0) also stops the timer.
    /// Players can only move and use their hands while the shift is <see cref="ShiftState.Playing"/>
    /// (<see cref="AllowsPlayerActions"/>, read by NetworkPlayer.CanAct).
    /// </summary>
    public class ShiftTimer : NetworkBehaviour
    {
        [Tooltip("Seconds of play (the timer at the start of the shift).")]
        [SerializeField, Min(1f)] private float _duration = 180f;
        [Tooltip("Seconds of the 3, 2, 1 countdown before the shift starts.")]
        [SerializeField, Min(0f)] private float _countdown = 3f;

        // Time left in the current step (countdown or playing). Written by the host every frame, sent at each network
        // tick (a few bytes). Declared BEFORE _state: NetworkVariables are applied in this order on the clients, so a
        // client that sees a new step already has its time.
        private readonly NetworkVariable<float> _timeLeft = new();

        private readonly NetworkVariable<ShiftState> _state = new(ShiftState.Waiting);

        private float _hostTimeLeft;
        private bool _started;
        private bool _startWhenScreenClear;
        private int _lastLoggedSecond = -1;

        /// <summary>The shift of the loaded level, null when the scene has none (lobby, world map).</summary>
        public static ShiftTimer Current { get; private set; }

        /// <summary>Raised (with the new shift) when <see cref="Current"/> changes.</summary>
        public static event Action<ShiftTimer> CurrentChanged;

        /// <summary>
        /// True when players may move and use their hands: no shift in this scene, or the shift is playing.
        /// </summary>
        public static bool AllowsPlayerActions => Current == null || Current.State == ShiftState.Playing;

        /// <summary>Raised on every machine when the step changes.</summary>
        public event Action<ShiftState> StateChanged;

        public ShiftState State => _state.Value;

        /// <summary>Seconds left in the current step: countdown (3 → 0) or playing (duration → 0). 0 when waiting or ended.</summary>
        public float TimeLeft => _timeLeft.Value;

        /// <summary>Seconds of play of this shift.</summary>
        public float Duration => _duration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            CurrentChanged = null;
        }

        private void Awake()
        {
            // Awake (not OnNetworkSpawn): on a lone host the load event can complete in the same frame as the load.
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsServer)
            {
                networkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
            }
        }

        public override void OnDestroy()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.SceneManager != null)
            {
                networkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            }
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += HandleStateChanged;
            _timeLeft.OnValueChanged += HandleTimeLeftChanged;
            Current = this;
            CurrentChanged?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateChanged;
            _timeLeft.OnValueChanged -= HandleTimeLeftChanged;
            if (Current == this)
            {
                Current = null;
                CurrentChanged?.Invoke(null);
            }
        }

        // Host: starts once every machine has the level (a slow machine must not miss the start).
        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name || _started || !IsSpawned)
            {
                return;
            }
            NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            // The countdown starts once the host's loading screen is gone, so "3" is not hidden under it.
            _startWhenScreenClear = true;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }
            if (_startWhenScreenClear && !UI.LoadingScreen.IsVisible)
            {
                _startWhenScreenClear = false;
                StartShift();
            }
            var state = _state.Value;
            if (state != ShiftState.Countdown && state != ShiftState.Playing)
            {
                return;
            }
            _hostTimeLeft = Mathf.Max(0f, _hostTimeLeft - Time.deltaTime);
            _timeLeft.Value = _hostTimeLeft;
            if (_hostTimeLeft > 0f)
            {
                return;
            }
            if (state == ShiftState.Countdown)
            {
                SetStep(ShiftState.Playing, _duration);
            }
            else
            {
                SetStep(ShiftState.Ended, 0f);
            }
        }

        /// <summary>Host only: (re)starts the shift from the countdown.</summary>
        public void StartShift()
        {
            if (!IsServer)
            {
                return;
            }
            _started = true;
            if (_countdown > 0f)
            {
                SetStep(ShiftState.Countdown, _countdown);
            }
            else
            {
                SetStep(ShiftState.Playing, _duration);
            }
        }

        /// <summary>Host only: ends the shift now (time up).</summary>
        public void EndShift()
        {
            if (IsServer && _state.Value != ShiftState.Ended)
            {
                SetStep(ShiftState.Ended, 0f);
            }
        }

        // Host: both values change in the same frame, so they leave in the same network message.
        private void SetStep(ShiftState state, float timeLeft)
        {
            _hostTimeLeft = timeLeft;
            _timeLeft.Value = timeLeft;
            _state.Value = state;
        }

        // Every machine.
        private void HandleStateChanged(ShiftState previous, ShiftState current)
        {
            _lastLoggedSecond = -1;
            switch (current)
            {
                case ShiftState.Countdown:
                    Debug.Log("[Shift] Get ready!");
                    break;
                case ShiftState.Playing:
                    Debug.Log($"[Shift] Go! {FormatTime(_duration)} on the clock.");
                    break;
                case ShiftState.Ended:
                    Debug.Log("[Shift] Time's up! The shift is over.");
                    break;
            }
            StateChanged?.Invoke(current);
        }

        // Every machine: a log for now (the HUD timer is UI-8). Countdown: 3, 2, 1. Playing: every 30 s, then every second
        // in the last 10 s.
        private void HandleTimeLeftChanged(float previous, float current)
        {
            // Only while counting down: a new step (or a restart) makes the time jump, up or down.
            var second = Mathf.CeilToInt(current);
            if (current >= previous || previous - current > 1f || second == _lastLoggedSecond || second <= 0)
            {
                return;
            }
            var state = _state.Value;
            var log = (state == ShiftState.Countdown && second <= Mathf.CeilToInt(_countdown)) ||
                      (state == ShiftState.Playing && (second <= 10 || second % 30 == 0) && second < Mathf.CeilToInt(_duration));
            if (!log)
            {
                return;
            }
            _lastLoggedSecond = second;
            Debug.Log(state == ShiftState.Countdown ? $"[Shift] {second}..." : $"[Shift] {FormatTime(second)} left");
        }

        /// <summary>"3:00", "0:09".</summary>
        public static string FormatTime(float seconds)
        {
            var total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        [ContextMenu("Restart Shift (host, Play Mode)")]
        private void RestartFromMenu()
        {
            StartShift();
        }

        [ContextMenu("End Shift Now (host, Play Mode)")]
        private void EndFromMenu()
        {
            EndShift();
        }
    }
}
