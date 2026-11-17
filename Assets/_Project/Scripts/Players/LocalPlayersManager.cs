using System;
using System.Collections.Generic;
using OverSmithed.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

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

        private IDisposable _anyButtonListener;
        private InputControl _lastPressedControl;
        private InputDevice _autoJoinDevice;
        private string _autoJoinScheme;

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

            // Remembers which device was used last (mouse click, key or gamepad button), for auto-join.
            _anyButtonListener = InputSystem.onAnyButtonPress.Call(HandleAnyButtonPress);
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
            _anyButtonListener?.Dispose();
        }

        // Auto-join waits until the session is up: roster spawned and this machine connected.
        private void Update()
        {
            if (_autoJoinDevice == null || _roster == null || !_roster.IsSpawned
                || !NetworkManager.Singleton.IsConnectedClient)
            {
                return;
            }

            var device = _autoJoinDevice;
            var scheme = _autoJoinScheme;
            CancelAutoJoin();
            if (device.added)
            {
                TryJoin(device, scheme);
            }
        }

        /// <summary>
        /// Online: the device used to click "Host" or "Join" becomes a player as soon as the session is ready.
        /// A mouse click means the keyboard (ZQSD scheme); Enter means the arrows scheme.
        /// </summary>
        public void QueueAutoJoinFromLastInput()
        {
            CancelAutoJoin();
            var control = _lastPressedControl;
            if (control?.device is Gamepad gamepad)
            {
                _autoJoinDevice = gamepad;
                _autoJoinScheme = "Gamepad";
            }
            else if (Keyboard.current != null)
            {
                var keyboard = Keyboard.current;
                var arrowsPlayer = control == keyboard.enterKey || control == keyboard.numpadEnterKey;
                _autoJoinDevice = keyboard;
                _autoJoinScheme = arrowsPlayer ? "KeyboardRight" : "KeyboardLeft";
            }
        }

        public void CancelAutoJoin()
        {
            _autoJoinDevice = null;
            _autoJoinScheme = null;
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
            if (context.ReadValueAsButton())
            {
                TryJoin(context.control.device, ControlSchemeOf(context), context.control);
            }
        }

        private void TryJoin(InputDevice device, string scheme, InputControl pressed = null)
        {
            if (_roster == null || _roster.IsLocked || _slots.Count >= NetworkConstants.MaxPlayers)
            {
                return;
            }
            if (scheme == null || IsInUse(device, scheme))
            {
                return;
            }
            if (pressed != null && IsMirrorOfJoinedGamepad(device, pressed))
            {
                Debug.Log($"[LocalPlayersManager] '{device.displayName}' (#{device.deviceId}) ignored: a joined gamepad " +
                          "has the same button down (one controller seen twice, e.g. USB + Bluetooth).");
                return;
            }
            // Online, each computer has one player (couch play with several devices is the local mode): another device
            // is not a new player, it is that player switching device (HandleAnyButtonPress). The host checks it too.
            if (IsOnline && _slots.Count > 0)
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

        // One gamepad can show up as two devices (macOS: the same DualSense by USB cable and by Bluetooth): every press
        // arrives on both. A gamepad that is not joined yet, pressing a button that a joined gamepad also holds down
        // right now, is that copy. Checked on every press (not only the first one), so pressing A again later (to start
        // the game...) never adds a ghost player. Two real players would only clash if they press at the very same
        // moment; the second one just presses again.
        private bool IsMirrorOfJoinedGamepad(InputDevice device, InputControl pressed)
        {
            if (device is not UnityEngine.InputSystem.Gamepad)
            {
                return false;
            }
            foreach (var slot in _slots)
            {
                if (slot.Device is not UnityEngine.InputSystem.Gamepad joined || joined == device)
                {
                    continue;
                }
                if (joined.TryGetChildControl(pressed.name) is UnityEngine.InputSystem.Controls.ButtonControl button &&
                    (button.isPressed || button.wasPressedThisFrame || button.wasReleasedThisFrame))
                {
                    return true;
                }
            }
            return false;
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
                "KeyboardLeft" => "Keyboard (left)",
                "KeyboardRight" => "Keyboard (arrows)",
                _ => "Gamepad"
            };
        }

        // Remembers the last device used (for auto-join), and online moves the only local player to the device they
        // just pressed a button on: clicking "Host" with the mouse then playing with a gamepad must just work.
        private void HandleAnyButtonPress(InputControl control)
        {
            _lastPressedControl = control;
            if (IsOnline && _slots.Count == 1)
            {
                SwitchDevice(_slots[0], control);
            }
        }

        private void SwitchDevice(LocalPlayerSlot slot, InputControl control)
        {
            var device = control.device;
            string scheme;
            if (device is Gamepad)
            {
                // A copy of the same gamepad (USB + Bluetooth) must not steal the player from the real one.
                if (IsMirrorOfJoinedGamepad(device, control))
                {
                    return;
                }
                scheme = "Gamepad";
            }
            else if (device is Keyboard)
            {
                scheme = IsBoundInScheme(control, "KeyboardRight") ? "KeyboardRight" : "KeyboardLeft";
            }
            else
            {
                return; // Mouse, pen...: not a way to play.
            }
            if (slot.Device == device && slot.ControlScheme == scheme)
            {
                return;
            }
            slot.SwitchDevice(scheme, device);
            Debug.Log($"[LocalPlayersManager] Online player now uses {device.displayName} ({scheme}).");
            SlotsChanged?.Invoke();
        }

        // True when the key is used by a binding of that scheme (arrows, Enter... for "KeyboardRight").
        private bool IsBoundInScheme(InputControl control, string scheme)
        {
            foreach (var map in _joinActions.actionMaps)
            {
                foreach (var binding in map.bindings)
                {
                    if (binding.isComposite || string.IsNullOrEmpty(binding.effectivePath) ||
                        binding.groups == null || !binding.groups.Contains(scheme))
                    {
                        continue;
                    }
                    if (InputControlPath.Matches(binding.effectivePath, control))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public const string OnlineOnePlayerMessage = "Online games: one player per computer.";

        private static bool IsOnline =>
            SessionService.Instance != null && SessionService.Instance.Mode == SessionMode.Online;

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
            CancelAutoJoin();
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
