using System;
using System.Collections.Generic;
using OverSmithed.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.Players
{
    /// <summary>
    /// Creates and removes the local players of this machine. Joining is manual (not PlayerInputManager's
    /// automatic join) so that two players can share one keyboard with different control schemes.
    /// A local player only becomes "Joined" once the host has added it to the <see cref="SessionRoster"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerInputManager))]
    public class LocalPlayersManager : MonoBehaviour
    {
        public static LocalPlayersManager Instance { get; private set; }

        [SerializeField] private LocalPlayerSlot _slotPrefab;

        private readonly List<LocalPlayerSlot> _slots = new();
        private InputActionAsset _joinActions;
        private InputAction _joinAction;
        private SessionRoster _roster;

        /// <summary>Raised when a local player is added, accepted or removed.</summary>
        public event Action SlotsChanged;

        /// <summary>Raised with a message to show the players (e.g. the host refused a join).</summary>
        public event Action<string> Notification;

        public IReadOnlyList<LocalPlayerSlot> Slots => _slots;

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

            // Private copy of the players' actions: PlayerInput instances enable/disable maps on the asset they use.
            _joinActions = Instantiate(_slotPrefab.GetComponent<PlayerInput>().actions);
            _joinAction = _joinActions.FindAction("Join/Join", throwIfNotFound: true);
            _joinAction.performed += HandleJoinPerformed;
        }

        private void Start()
        {
            NetworkManager.Singleton.OnClientStopped += HandleNetworkStopped;
            SessionRoster.InstanceChanged += BindRoster;
            BindRoster(SessionRoster.Instance);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }
            Instance = null;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientStopped -= HandleNetworkStopped;
            }
            SessionRoster.InstanceChanged -= BindRoster;
            BindRoster(null);

            _joinAction.performed -= HandleJoinPerformed;
            Destroy(_joinActions);
        }

        /// <summary>Called by the lobby: devices can only join while it is open.</summary>
        public void SetJoiningEnabled(bool joiningEnabled)
        {
            if (joiningEnabled)
            {
                _joinAction.Enable();
            }
            else
            {
                _joinAction.Disable();
            }
        }

        public LocalPlayerSlot FindSlot(byte localIndex)
        {
            foreach (var slot in _slots)
            {
                if (slot.LocalIndex == localIndex)
                {
                    return slot;
                }
            }
            return null;
        }

        // Join is a PassThrough action: every bound control reports independently, so several players
        // pressing at the same time all join (a Button action would only keep one). Releases are ignored.
        private void HandleJoinPerformed(InputAction.CallbackContext context)
        {
            if (!context.ReadValueAsButton())
            {
                return;
            }
            if (_roster == null || _roster.IsLocked || _slots.Count >= NetworkConstants.MaxPlayers)
            {
                return;
            }

            var device = context.control.device;
            var scheme = ControlSchemeOf(context);
            if (scheme == null || IsInUse(device, scheme))
            {
                return;
            }

            var input = PlayerInput.Instantiate(_slotPrefab.gameObject, controlScheme: scheme, pairWithDevice: device);
            var slot = input.GetComponent<LocalPlayerSlot>();
            slot.Initialize(DeviceLabelOf(scheme));
            slot.LeaveRequested += HandleLeaveRequested;
            slot.DeviceLost += HandleDeviceLost;
            _slots.Add(slot);

            _roster.RequestJoin(slot.LocalIndex, slot.DeviceLabel);
            SlotsChanged?.Invoke();
        }

        // The binding that fired tells which control scheme the player wants (same keyboard, two schemes).
        private static string ControlSchemeOf(InputAction.CallbackContext context)
        {
            var bindingIndex = context.action.GetBindingIndexForControl(context.control);
            return bindingIndex >= 0 ? context.action.bindings[bindingIndex].groups : null;
        }

        private static string DeviceLabelOf(string scheme)
        {
            return scheme switch
            {
                "KeyboardLeft" => "Clavier (ZQSD)",
                "KeyboardRight" => "Clavier (flèches)",
                _ => "Manette"
            };
        }

        private bool IsInUse(InputDevice device, string scheme)
        {
            foreach (var slot in _slots)
            {
                if (slot.Device == device && slot.ControlScheme == scheme)
                {
                    return true;
                }
            }
            return false;
        }

        private void HandleLeaveRequested(LocalPlayerSlot slot)
        {
            // Lobby only for now: leaving during a game is handled with disconnections.
            if (_roster != null && _roster.IsLocked)
            {
                return;
            }
            // Always tell the host, even if still pending: the join request may already have been accepted.
            _roster?.RequestLeave(slot.LocalIndex);
            RemoveSlot(slot);
        }

        private void HandleDeviceLost(LocalPlayerSlot slot)
        {
            if (_roster == null || !_roster.IsLocked)
            {
                HandleLeaveRequested(slot);
            }
        }

        private void BindRoster(SessionRoster roster)
        {
            if (_roster != null)
            {
                _roster.Changed -= HandleRosterChanged;
                _roster.JoinRejected -= HandleJoinRejected;
            }

            _roster = roster;

            if (_roster != null)
            {
                _roster.Changed += HandleRosterChanged;
                _roster.JoinRejected += HandleJoinRejected;
                HandleRosterChanged();
            }
        }

        private void HandleRosterChanged()
        {
            var localClientId = NetworkManager.Singleton.LocalClientId;
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                var slot = _slots[i];
                if (_roster.TryGetEntry(localClientId, slot.LocalIndex, out var entry))
                {
                    slot.MarkJoined(entry.PlayerNumber);
                }
                else if (slot.State == LocalPlayerSlot.SlotState.Joined)
                {
                    // Removed by the host.
                    RemoveSlot(slot);
                }
            }
            SlotsChanged?.Invoke();
        }

        private void HandleJoinRejected(byte localIndex, string reason)
        {
            var slot = FindSlot(localIndex);
            if (slot != null && slot.State == LocalPlayerSlot.SlotState.Pending)
            {
                RemoveSlot(slot);
            }
            Notification?.Invoke(reason);
        }

        private void HandleNetworkStopped(bool wasHost)
        {
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                RemoveSlot(_slots[i]);
            }
        }

        private void RemoveSlot(LocalPlayerSlot slot)
        {
            slot.LeaveRequested -= HandleLeaveRequested;
            slot.DeviceLost -= HandleDeviceLost;
            _slots.Remove(slot);
            Destroy(slot.gameObject);
            SlotsChanged?.Invoke();
        }
    }
}
