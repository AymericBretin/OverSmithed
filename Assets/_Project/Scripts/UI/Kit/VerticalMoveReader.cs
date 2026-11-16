using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// Reads single up / down pushes from the UI "Navigate" action (any gamepad, arrows, ZQSD/WASD), one step per
    /// push. For screens that move the selection by hand (the lobby and the map: left / right are taken by the game).
    /// </summary>
    public sealed class VerticalMoveReader
    {
        private const float PressThreshold = 0.6f;
        private const float ReleaseThreshold = 0.3f;
        private bool _held;

        /// <summary>+1 on a new push up, -1 on a new push down, else 0. Call once per frame.</summary>
        public int Read()
        {
            var module = EventSystem.current != null
                ? EventSystem.current.currentInputModule as InputSystemUIInputModule
                : null;
            var action = module != null && module.move != null ? module.move.action : null;
            if (action == null)
            {
                return 0;
            }
            var y = action.ReadValue<Vector2>().y;
            if (_held)
            {
                if (Mathf.Abs(y) < ReleaseThreshold)
                {
                    _held = false;
                }
                return 0;
            }
            if (Mathf.Abs(y) >= PressThreshold)
            {
                _held = true;
                return y > 0f ? 1 : -1;
            }
            return 0;
        }
    }
}
