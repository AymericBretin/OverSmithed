using System;
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
        }

        private void OnDestroy()
        {
            if (Input != null)
            {
                Input.onActionTriggered -= HandleActionTriggered;
                Input.onDeviceLost -= HandleDeviceLost;
            }
        }

        public void Initialize(string deviceLabel)
        {
            DeviceLabel = deviceLabel;
            State = SlotState.Pending;
            name = $"LocalPlayerSlot {LocalIndex} ({ControlScheme})";
        }

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
            DeviceLost?.Invoke(this);
        }
    }
}
