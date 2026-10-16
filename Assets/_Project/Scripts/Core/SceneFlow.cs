using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Core
{
    /// <summary>
    /// Drives scene transitions. Networked scenes (Lobby, Game) are loaded by the server through
    /// NetworkSceneManager so every client follows; the Menu is loaded locally once the network stops.
    /// </summary>
    public class SceneFlow : MonoBehaviour
    {
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
            LoadNetworkScene(SceneNames.Lobby);
        }

        private void HandleClientStopped(bool wasHost)
        {
            if (SceneManager.GetActiveScene().name != SceneNames.Menu)
            {
                SceneManager.LoadScene(SceneNames.Menu);
            }
        }

        /// <summary>Server only: loads a scene on every connected machine.</summary>
        public static bool LoadNetworkScene(string sceneName)
        {
            var status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[SceneFlow] Could not load {sceneName}: {status}");
                return false;
            }
            return true;
        }
    }
}
