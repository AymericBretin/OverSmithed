using System;
using System.Threading.Tasks;
using OverSmithed.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
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
    /// Online, a Multiplayer Services session allocates the Relay, gives the join code and starts
    /// Netcode as host or client.
    /// </summary>
    public class SessionService : MonoBehaviour
    {
        public static SessionService Instance { get; private set; }

        [SerializeField] private SessionRoster _rosterPrefab;

        private ISession _session;

        public SessionMode Mode { get; private set; }

        /// <summary>Code to share so other machines can join. Null when not online.</summary>
        public string SessionCode => _session?.Code;

        /// <summary>Raised when the online session is created, joined or left.</summary>
        public event Action SessionChanged;

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

        /// <summary>Creates an online session through Relay. Returns an error message, or null on success.</summary>
        public async Task<string> HostOnlineAsync()
        {
            if (Mode != SessionMode.None)
            {
                return "Une partie est déjà en cours.";
            }

            // Set before the network starts so the lobby, loaded during creation, shows the right mode.
            Mode = SessionMode.Online;
            try
            {
                await ServicesInitializer.EnsureSignedInAsync();
                var options = new SessionOptions
                {
                    MaxPlayers = NetworkConstants.MaxConnections,
                    // Not listed publicly: joining is by code only.
                    IsPrivate = true
                }.WithRelayNetwork();

                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Debug.Log($"[Session] Online session created, code {_session.Code}.");
                SessionChanged?.Invoke();
                return null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                await AbortOnlineAsync();
                return "Impossible de créer la partie en ligne. Vérifie ta connexion.";
            }
        }

        /// <summary>Joins an online session by code. Returns an error message, or null on success.</summary>
        public async Task<string> JoinByCodeAsync(string code)
        {
            if (Mode != SessionMode.None)
            {
                return "Une partie est déjà en cours.";
            }
            code = code?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
            {
                return "Entre le code de la partie.";
            }

            Mode = SessionMode.Online;
            try
            {
                await ServicesInitializer.EnsureSignedInAsync();
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                Debug.Log($"[Session] Joined online session {_session.Code}.");
                SessionChanged?.Invoke();
                return null;
            }
            catch (SessionException exception)
            {
                Debug.LogWarning($"[Session] Join failed: {exception.Error} {exception.Message}");
                await AbortOnlineAsync();
                // Full and locked sessions both surface as Unknown: the service does not keep the reason.
                return exception.Error == SessionError.SessionNotFound
                    ? "Aucune partie avec ce code."
                    : "Impossible de rejoindre : partie pleine, déjà lancée ou code invalide.";
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                await AbortOnlineAsync();
                return "Impossible de rejoindre la partie. Vérifie ta connexion.";
            }
        }

        /// <summary>Host only: closes the lobby and takes every machine to the game scene.</summary>
        public bool LaunchGame()
        {
            var roster = SessionRoster.Instance;
            if (!NetworkManager.Singleton.IsServer || roster == null || roster.Count == 0 || roster.IsLocked)
            {
                return false;
            }

            roster.SetLocked(true);
            if (!SceneFlow.LoadNetworkScene(SceneNames.Game))
            {
                roster.SetLocked(false);
                return false;
            }

            // Online: also lock the session itself, so the join code stops working.
            if (_session != null && _session.IsHost)
            {
                _ = LockSessionAsync(_session.AsHost());
            }
            return true;
        }

        public void Leave()
        {
            _ = LeaveAsync();
        }

        private async Task LeaveAsync()
        {
            var session = _session;
            _session = null;

            if (session != null)
            {
                try
                {
                    // Host: deleting the session stops the network and disconnects everyone.
                    if (session.IsHost)
                    {
                        await session.AsHost().DeleteAsync();
                    }
                    else
                    {
                        await session.LeaveAsync();
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Session] Leaving the online session failed: {exception.Message}");
                }
                SessionChanged?.Invoke();
            }

            // Local play, or the session could not stop the network itself.
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsListening && !networkManager.ShutdownInProgress)
            {
                networkManager.Shutdown();
            }
        }

        private async Task AbortOnlineAsync()
        {
            await LeaveAsync();
            Mode = SessionMode.None;
        }

        private static async Task LockSessionAsync(IHostSession session)
        {
            try
            {
                session.IsLocked = true;
                await session.SavePropertiesAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Session] Could not lock the online session: {exception.Message}");
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

            // Network lost without us leaving (host gone, connection refused...): leave the online session too.
            if (_session != null)
            {
                Leave();
            }
        }
    }
}
