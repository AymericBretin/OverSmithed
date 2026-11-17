using System;
using OverSmithed.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverSmithed.Players
{
    /// <summary>
    /// One local player on this machine: a PlayerInput paired to a device and a control scheme.
    /// Not networked. Its <see cref="LocalIndex"/> links it to the networked player owned by this machine.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class LocalPlayerSlot : MonoBehaviour
    {
        public enum SlotState
        {
            /// <summary>Waiting for the host to accept the join request.</summary>
            Pending,
            Joined
        }

        public event Action<LocalPlayerSlot> LeaveRequested;
        public event Action<LocalPlayerSlot> DeviceLost;

        /// <summary>The lost device came back: the Input System pairs it again automatically.</summary>
        public event Action<LocalPlayerSlot> DeviceRegained;

        public bool IsDeviceConnected { get; private set; } = true;

        public PlayerInput Input { get; private set; }
        public byte LocalIndex => (byte)Input.playerIndex;
        public string ControlScheme => Input.currentControlScheme;
        public InputDevice Device => Input.devices.Count > 0 ? Input.devices[0] : null;
        public string DeviceLabel { get; private set; }
        public SlotState State { get; private set; }
        public byte PlayerNumber { get; private set; }

        private void Awake()
        {
            Input = GetComponent<PlayerInput>();
            // Device pairing must survive the Lobby -> Game scene change.
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            Input.onActionTriggered += HandleActionTriggered;
            Input.onDeviceLost += HandleDeviceLost;
            Input.onDeviceRegained += HandleDeviceRegained;
        }

        private void OnDestroy()
        {
            if (Input != null)
            {
                ControlBindings.Unregister(Input.actions);
                Input.onActionTriggered -= HandleActionTriggered;
                Input.onDeviceLost -= HandleDeviceLost;
                Input.onDeviceRegained -= HandleDeviceRegained;
            }
        }

        public void Initialize(string deviceLabel)
        {
            DeviceLabel = deviceLabel;
            State = SlotState.Pending;
            // This player's copy of the actions uses the keys chosen in the controls screen.
            ControlBindings.Register(Input.actions);
            name = $"LocalPlayerSlot {LocalIndex} ({ControlScheme})";
        }

        /// <summary>
        /// Online: the player now uses another device. The press that made the switch must not also act (pressing B
        /// on a new gamepad must not make the player leave): the actions are off until the next frame, and a button
        /// still held then does not count as a new press.
        /// </summary>
        public void SwitchDevice(string scheme, InputDevice device)
        {
            Input.SwitchCurrentControlScheme(scheme, device);
            Input.DeactivateInput();
            if (_reactivate != null)
            {
                StopCoroutine(_reactivate);
            }
            _reactivate = StartCoroutine(ReactivateNextFrame());
            name = $"LocalPlayerSlot {LocalIndex} ({ControlScheme})";
        }

        private System.Collections.IEnumerator ReactivateNextFrame()
        {
            yield return null;
            Input.ActivateInput();
            _reactivate = null;
        }

        private Coroutine _reactivate;

        public void MarkJoined(byte playerNumber)
        {
            State = SlotState.Joined;
            PlayerNumber = playerNumber;
        }

        private void HandleActionTriggered(InputAction.CallbackContext context)
        {
            if (context.performed && context.action.name == "Leave")
            {
                LeaveRequested?.Invoke(this);
            }
        }

        private void HandleDeviceLost(PlayerInput _)
        {
            IsDeviceConnected = false;
            DeviceLost?.Invoke(this);
        }

        private void HandleDeviceRegained(PlayerInput _)
        {
            IsDeviceConnected = true;
            DeviceRegained?.Invoke(this);
        }
    }
}
