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

        // Domain reload is off: forget it when Play Mode starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LastLevelScene = null;
        }

        private void Start()
        {
            var networkManager = NetworkManager.Singleton;
            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnClientStopped += HandleClientStopped;
        }

        private void OnDestroy()
        {
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

        /// <summary>Server only: loads a scene on every connected machine.</summary>
        public static bool LoadNetworkScene(string sceneName)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                Debug.LogWarning($"[SceneFlow] Only the host can load {sceneName}.");
                return false;
            }
            // e.g. SceneEventInProgress: another scene is still loading.
            var status = networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[SceneFlow] Could not load {sceneName}: {status}");
                return false;
            }
            return true;
        }
    }
}
