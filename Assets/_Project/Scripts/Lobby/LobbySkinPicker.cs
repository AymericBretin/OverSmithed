using System;
using OverSmithed.Networking;
using OverSmithed.Players;
using UnityEngine;

namespace OverSmithed.Lobby
{
    /// <summary>
    /// Lobby skin choice: each joined player of this machine pushes left or right on their own device (stick, D-pad,
    /// Q/A-D or arrows: the Move action) to ask the host for the previous / next free skin. One push = one step.
    /// The host checks that nobody else has that skin (<see cref="SessionRoster.RequestSkinChange"/>); the new skin
    /// then reaches every machine through the roster, and the game uses it.
    /// </summary>
    public class LobbySkinPicker : MonoBehaviour
    {
        private const string MoveActionName = "Move";

        [Tooltip("How far the stick must be pushed to count as a press.")]
        [SerializeField, Range(0.2f, 0.95f)] private float _pressThreshold = 0.6f;
        [Tooltip("Below this, the stick is back in the middle (ready for the next press).")]
        [SerializeField, Range(0.05f, 0.9f)] private float _releaseThreshold = 0.3f;

        // One flag per local slot: is its stick still pushed since the last step?
        private readonly System.Collections.Generic.Dictionary<LocalPlayerSlot, bool> _held = new();

        /// <summary>A local player asked for another skin (player number, direction -1 / +1). For the arrows' bump.</summary>
        public static event Action<byte, int> SkinChangeRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SkinChangeRequested = null;
        }

        private void Update()
        {
            var roster = SessionRoster.Instance;
            var localPlayers = LocalPlayersManager.Instance;
            if (roster == null || roster.IsLocked || localPlayers == null)
            {
                return;
            }
            foreach (var slot in localPlayers.Slots)
            {
                if (slot.State != LocalPlayerSlot.SlotState.Joined)
                {
                    _held.Remove(slot);
                    continue;
                }
                var move = slot.Input.actions.FindAction(MoveActionName);
                if (move == null)
                {
                    continue;
                }
                var x = move.ReadValue<Vector2>().x;
                _held.TryGetValue(slot, out var held);
                if (held)
                {
                    if (Mathf.Abs(x) < _releaseThreshold)
                    {
                        _held[slot] = false;
                    }
                    continue;
                }
                if (Mathf.Abs(x) >= _pressThreshold && Mathf.Abs(x) > Mathf.Abs(move.ReadValue<Vector2>().y))
                {
                    _held[slot] = true;
                    var direction = x > 0f ? 1 : -1;
                    roster.RequestSkinChange(slot.LocalIndex, direction);
                    SkinChangeRequested?.Invoke(slot.PlayerNumber, direction);
                }
            }
        }
    }
}
