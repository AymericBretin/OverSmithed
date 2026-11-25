using System;
using System.Collections;
using System.Collections.Generic;
using OverSmithed.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Story
{
    /// <summary>
    /// Plays the story intro cutscene (scene StoryIntro, right after the lobby on a new save) for everyone.
    /// The host decides: it starts shot 0 once every machine has the scene, goes to the next shot when one of its
    /// players presses Interact (<see cref="HostNext"/>) and skips everything with Pause (<see cref="HostSkip"/>).
    /// Only the shot number travels on the network (one int): every machine plays that shot's camera, steps and text
    /// itself (scene data). A machine that is behind jumps the shots it missed to their end first.
    /// At the end the host saves "intro seen" and takes everyone to the world map (the players start level 1 there).
    /// </summary>
    public class StoryIntroDirector : NetworkBehaviour
    {
        [SerializeField] private Camera _camera;
        [Tooltip("The shots, in order.")]
        [SerializeField] private List<CutsceneShot> _shots = new();
        [Tooltip("Lighting moods: the one of the current shot is on, the others off.")]
        [SerializeField] private List<CutsceneMood> _moods = new();
        [Tooltip("Seconds of the fade to black at the end, before the level loads.")]
        [SerializeField, Min(0f)] private float _endFadeTime = 1.2f;

        // -1 = waiting for every machine; 0.._shots.Count-1 = playing that shot; _shots.Count = the end.
        private readonly NetworkVariable<int> _shot = new(-1);

        private int _playing = -1;
        private bool _ending;
        private bool _loadingMap;

        /// <summary>The cutscene of the loaded scene, null when none.</summary>
        public static StoryIntroDirector Current { get; private set; }

        /// <summary>Every machine: a shot starts (show its text).</summary>
        public event Action<CutsceneShot> ShotStarted;

        /// <summary>Every machine: the cutscene ends (last shot done or skipped): fade to black.</summary>
        public event Action Ending;

        public CutsceneShot CurrentShot => _playing >= 0 && _playing < _shots.Count ? _shots[_playing] : null;
        public int ShotIndex => _playing;
        public int ShotCount => _shots.Count;
        public bool IsEnding => _ending;
        public float EndFadeTime => _endFadeTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
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

        private void Start()
        {
            // Before shot 0 (other machines still loading): first mood, camera on the first frame, screen black (UI).
            if (_playing < 0 && _shots.Count > 0)
            {
                ApplyMood(_shots[0].Mood);
                _shots[0].PoseCamera(_camera, 0f);
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
            Current = this;
            _shot.OnValueChanged += HandleShotChanged;
            if (_shot.Value >= 0)
            {
                GoTo(_shot.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            _shot.OnValueChanged -= HandleShotChanged;
            if (Current == this)
            {
                Current = null;
            }
        }

        // Host: starts once every machine has the scene (nobody misses the start).
        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != gameObject.scene.name || !IsSpawned || _shot.Value >= 0)
            {
                return;
            }
            NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            _shot.Value = _shots.Count > 0 ? 0 : _shots.Count;
        }

        /// <summary>Host only: next shot (after the last one: the end).</summary>
        public void HostNext()
        {
            if (IsServer && _shot.Value >= 0 && _shot.Value < _shots.Count)
            {
                _shot.Value++;
            }
        }

        /// <summary>Host only: skips the whole cutscene.</summary>
        public void HostSkip()
        {
            if (IsServer && _shot.Value >= 0 && _shot.Value < _shots.Count)
            {
                _shot.Value = _shots.Count;
            }
        }

        private void HandleShotChanged(int previous, int current)
        {
            GoTo(current);
        }

        // Every machine.
        private void GoTo(int index)
        {
            if (_ending || index <= _playing)
            {
                return;
            }
            // Shots not finished (or missed) jump to their end, in order: the next one starts from the right state.
            for (var i = Mathf.Max(0, _playing); i < Mathf.Min(index, _shots.Count); i++)
            {
                if (i != _playing)
                {
                    _shots[i].Begin();
                }
                _shots[i].Complete();
            }
            if (index >= _shots.Count)
            {
                _ending = true;
                Ending?.Invoke();
                if (IsServer)
                {
                    StartCoroutine(LoadMapAfterFade());
                }
                return;
            }
            _playing = index;
            var shot = _shots[index];
            ApplyMood(shot.Mood);
            shot.Begin();
            shot.PoseCamera(_camera, 0f);
            ShotStarted?.Invoke(shot);
        }

        private void ApplyMood(CutsceneMood mood)
        {
            if (mood == null)
            {
                return;
            }
            foreach (var other in _moods)
            {
                if (other != null && other != mood)
                {
                    other.gameObject.SetActive(false);
                }
            }
            mood.Apply();
        }

        private void Update()
        {
            var shot = CurrentShot;
            if (shot == null)
            {
                return;
            }
            // Still plays under the final fade.
            shot.Tick(Time.deltaTime);
            shot.PoseCamera(_camera, Time.deltaTime);
        }

        // Host.
        private IEnumerator LoadMapAfterFade()
        {
            if (_loadingMap)
            {
                yield break;
            }
            _loadingMap = true;
            yield return new WaitForSeconds(_endFadeTime + 0.1f);
            GameProgress.MarkIntroSeen();
            if (!SceneFlow.GoToWorldMap())
            {
                Debug.LogWarning("[StoryIntro] Could not go to the world map.", this);
            }
        }
    }
}
