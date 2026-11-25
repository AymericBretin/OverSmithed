using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Core
{
    /// <summary>
    /// Drives scene transitions. Networked scenes are loaded by the server through NetworkSceneManager so
    /// every client follows: Lobby -> World map -> Level -> World map -> ... The Menu is loaded locally once
    /// the network stops (leaving, host closed the game, connection lost).
    /// </summary>
    public class SceneFlow : MonoBehaviour
    {
        /// <summary>
        /// Host only: the level scene started last in this session (null before the first level). The world map
        /// walker starts on its spot when the players come back to the map.
        /// </summary>
        public static string LastLevelScene { get; private set; }

        /// <summary>Host only: raised when a networked scene starts loading (the loading screen listens).</summary>
        public static event Action<string> NetworkSceneLoadStarted;

        /// <summary>Host only: a delayed load could not start after all (the loading screen goes away).</summary>
        public static event Action<string> NetworkSceneLoadFailed;

        /// <summary>
        /// Host only, optional (set by the loading screen): asked before every networked load with the scene name;
        /// covers every machine and returns the seconds until they are covered. The load starts after that, so
        /// nobody sees the next scene appear before the loading screen.
        /// </summary>
        public static Func<string, float> CoverBeforeLoad;

        private static SceneFlow _instance;
        // A load waiting for the cover (only one at a time, like NGO scene events).
        private static string _pendingScene;

        // Domain reload is off: forget it when Play Mode starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LastLevelScene = null;
            NetworkSceneLoadStarted = null;
            NetworkSceneLoadFailed = null;
            CoverBeforeLoad = null;
            _instance = null;
            _pendingScene = null;
        }

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            var networkManager = NetworkManager.Singleton;
            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnClientStopped += HandleClientStopped;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                return;
            }
            networkManager.OnServerStarted -= HandleServerStarted;
            networkManager.OnClientStopped -= HandleClientStopped;
        }

        private void HandleServerStarted()
        {
            LastLevelScene = null;
            LoadNetworkScene(SceneNames.Lobby);
        }

        private void HandleClientStopped(bool wasHost)
        {
            if (SceneManager.GetActiveScene().name != SceneNames.Menu)
            {
                SceneManager.LoadScene(SceneNames.Menu);
            }
        }

        /// <summary>Host only: takes everyone to the world map (after the lobby, or at the end of a level).</summary>
        public static bool GoToWorldMap()
        {
            return LoadNetworkScene(SceneNames.WorldMap);
        }

        /// <summary>Host only: takes everyone to a level scene.</summary>
        public static bool StartLevel(string sceneName)
        {
            if (!LoadNetworkScene(sceneName))
            {
                return false;
            }
            LastLevelScene = sceneName;
            return true;
        }

        /// <summary>
        /// Server only: loads a scene on every connected machine. With a loading screen, every machine is covered
        /// first and the load starts a moment later (true = the load is on its way).
        /// </summary>
        public static bool LoadNetworkScene(string sceneName)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                Debug.LogWarning($"[SceneFlow] Only the host can load {sceneName}.");
                return false;
            }
            if (_pendingScene != null)
            {
                Debug.LogWarning($"[SceneFlow] Could not load {sceneName}: {_pendingScene} is about to load.");
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneFlow] Could not load {sceneName}: not in the build list.");
                return false;
            }
            var wait = CoverBeforeLoad != null ? CoverBeforeLoad(sceneName) : 0f;
            if (wait <= 0f || _instance == null)
            {
                return StartLoad(sceneName);
            }
            _pendingScene = sceneName;
            _instance.StartCoroutine(LoadWhenCovered(sceneName, wait));
            return true;
        }

        // Real time: a local pause must not hold a load forever.
        private static IEnumerator LoadWhenCovered(string sceneName, float wait)
        {
            yield return new WaitForSecondsRealtime(wait);
            _pendingScene = null;
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                yield break;
            }
            if (!StartLoad(sceneName))
            {
                NetworkSceneLoadFailed?.Invoke(sceneName);
            }
        }

        private static bool StartLoad(string sceneName)
        {
            // e.g. SceneEventInProgress: another scene is still loading.
            var status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[SceneFlow] Could not load {sceneName}: {status}");
                return false;
            }
            NetworkSceneLoadStarted?.Invoke(sceneName);
            return true;
        }
    }
}
