using System;
using System.Linq;
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
        private const string HostClosedMessage = "L'hôte a fermé la partie.";
        private const string ConnectionLostMessage = "La connexion avec l'hôte a été perdue.";
        private const string NetworkStoppedMessage = "Le réseau a été interrompu.";

        public static SessionService Instance { get; private set; }

        [SerializeField] private SessionRoster _rosterPrefab;

        private ISession _session;
        private bool _leavingOnPurpose;
        private string _disconnectReason;
        private string _disconnectMessage;

        public SessionMode Mode { get; private set; }

        /// <summary>Code to share so other machines can join. Null when not online.</summary>
        public string SessionCode => _session?.Code;

        /// <summary>Raised when the online session is created, joined or left.</summary>
        public event Action SessionChanged;

        /// <summary>Raised when the network stopped without the player asking for it, with a message to show.</summary>
        public event Action<string> Disconnected;

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
            var networkManager = NetworkManager.Singleton;
            ConnectionApproval.Install(networkManager);
            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            networkManager.OnClientStopped += HandleClientStopped;
        }

        private void OnDestroy()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                networkManager.OnServerStarted -= HandleServerStarted;
                networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                networkManager.OnClientStopped -= HandleClientStopped;
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Returns (and forgets) the message explaining the last unexpected disconnection, if any.
        /// The menu calls it when it opens.
        /// </summary>
        public string ConsumeDisconnectMessage()
        {
            var message = _disconnectMessage;
            _disconnectMessage = null;
            return message;
        }

        /// <summary>Starts a host on the loopback interface only: no Relay, no Internet, nobody can join.</summary>
        public bool StartLocal()
        {
            if (Mode != SessionMode.None)
            {
                return false;
            }

            PrepareStart();
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

            PrepareStart();
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

                SetSession(await MultiplayerService.Instance.CreateSessionAsync(options));
                EnsureNetworkRunning();
                Debug.Log($"[Session] Online session created, code {_session.Code}.");
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

            PrepareStart();
            Mode = SessionMode.Online;
            try
            {
                await ServicesInitializer.EnsureSignedInAsync();
                SetSession(await MultiplayerService.Instance.JoinSessionByCodeAsync(code));
                EnsureNetworkRunning();
                Debug.Log($"[Session] Joined online session {_session.Code}.");
                return null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Session] Join failed: {exception.Message}");
                await AbortOnlineAsync();

                // Refused by the host's connection approval: its reason is more useful than ours.
                var hostReason = ConsumeDisconnectMessage();
                if (hostReason != null)
                {
                    return hostReason;
                }
                // Full and locked sessions both surface as Unknown: the service does not keep the reason.
                return exception is SessionException { Error: SessionError.SessionNotFound }
                    ? "Aucune partie avec ce code."
                    : "Impossible de rejoindre : partie pleine, déjà lancée ou code invalide.";
            }
        }

        /// <summary>
        /// Host only: closes the lobby (nobody can join any more) and takes every machine to the world map,
        /// or to <paramref name="firstScene"/> (the editor "Go To" menu uses it to jump to a level).
        /// </summary>
        public bool LaunchGame(string firstScene = SceneNames.WorldMap)
        {
            var roster = SessionRoster.Instance;
            if (!NetworkManager.Singleton.IsServer || roster == null || roster.Count == 0 || roster.IsLocked)
            {
                return false;
            }

            roster.SetLocked(true);
            if (!SceneFlow.LoadNetworkScene(firstScene))
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

        /// <summary>The player asked to leave (or the host to close the game).</summary>
        public void Leave()
        {
            _leavingOnPurpose = true;
            _ = LeaveAsync();
        }

        private void PrepareStart()
        {
            _leavingOnPurpose = false;
            _disconnectReason = null;
            _disconnectMessage = null;
        }

        private void SetSession(ISession session)
        {
            _session = session;
            SessionChanged?.Invoke();
        }

        // The session's network handler swallows a Netcode start that gets cancelled (e.g. the host refused
        // the connection): the session call then "succeeds" without a running network.
        private static void EnsureNetworkRunning()
        {
            if (!NetworkManager.Singleton.IsListening)
            {
                throw new InvalidOperationException("The session started but Netcode is not running.");
            }
        }

        private async Task LeaveAsync()
        {
            var networkManager = NetworkManager.Singleton;

            // Host: tell every client why it is being disconnected before the network goes away.
            if (networkManager != null && networkManager.IsServer && networkManager.IsListening)
            {
                var clients = networkManager.ConnectedClientsIds.Where(id => id != NetworkManager.ServerClientId).ToList();
                foreach (var clientId in clients)
                {
                    networkManager.DisconnectClient(clientId, HostClosedMessage);
                }
                if (clients.Count > 0)
                {
                    // Let the transport flush the disconnect messages.
                    await Task.Delay(200);
                }
            }

            var session = _session;
            if (session != null)
            {
                _session = null;
                try
                {
                    // Host: deleting the session stops the network and removes it from the service.
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
            if (networkManager != null && networkManager.IsListening && !networkManager.ShutdownInProgress)
            {
                networkManager.Shutdown();
            }
        }

        private async Task AbortOnlineAsync()
        {
            _leavingOnPurpose = true;
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

        // On a client, raised with its own id when the host disconnects it: the reason is readable here.
        private void HandleClientDisconnected(ulong clientId)
        {
            var networkManager = NetworkManager.Singleton;
            if (!networkManager.IsServer && clientId == networkManager.LocalClientId
                && !string.IsNullOrEmpty(networkManager.DisconnectReason))
            {
                _disconnectReason = networkManager.DisconnectReason;
            }
        }

        // Raised on every machine (the host is also a client) once the network has fully stopped.
        private void HandleClientStopped(bool wasHost)
        {
            Mode = SessionMode.None;

            if (!_leavingOnPurpose)
            {
                _disconnectMessage = _disconnectReason ?? (wasHost ? NetworkStoppedMessage : ConnectionLostMessage);
                Debug.LogWarning($"[Session] Disconnected: {_disconnectMessage}");
                Disconnected?.Invoke(_disconnectMessage);

                // Network lost without us leaving (host gone, connection refused...): leave the online session too.
                if (_session != null)
                {
                    _ = LeaveAsync();
                }
            }
            _leavingOnPurpose = false;
            _disconnectReason = null;
        }
    }
}
