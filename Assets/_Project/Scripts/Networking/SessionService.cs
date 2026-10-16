using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace OverSmithed.Networking
{
    public enum SessionMode
    {
        None,
        Local,
        Online
    }

    /// <summary>
    /// Entry point for starting and leaving a session. Whatever the mode, once the network is started
    /// the rest of the game only reacts to NetworkManager events: local play is just a host nobody joins.
    /// </summary>
    public class SessionService : MonoBehaviour
    {
        public static SessionService Instance { get; private set; }

        [SerializeField] private SessionRoster _rosterPrefab;

        public SessionMode Mode { get; private set; }

        // Domain reload is disabled on entering Play Mode: statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
            NetworkManager.Singleton.OnClientStopped += HandleClientStopped;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
                NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Starts a host on the loopback interface only: no Relay, no Internet, nobody can join.</summary>
        public bool StartLocal()
        {
            if (Mode != SessionMode.None)
            {
                return false;
            }

            var networkManager = NetworkManager.Singleton;
            var transport = networkManager.GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", NetworkConstants.LocalPort, "127.0.0.1");

            if (!networkManager.StartHost())
            {
                Debug.LogError("[Session] Failed to start local host.");
                return false;
            }

            Mode = SessionMode.Local;
            return true;
        }

        public void Leave()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsListening && !networkManager.ShutdownInProgress)
            {
                networkManager.Shutdown();
            }
        }

        // The roster lives for the whole session, across networked scene loads.
        private void HandleServerStarted()
        {
            var roster = Instantiate(_rosterPrefab);
            DontDestroyOnLoad(roster.gameObject);
            roster.NetworkObject.Spawn();
        }

        // Raised on every machine (the host is also a client) once the network has fully stopped.
        private void HandleClientStopped(bool wasHost)
        {
            Mode = SessionMode.None;
        }
    }
}
