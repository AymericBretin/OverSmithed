using UnityEngine.InputSystem;

namespace OverSmithed.UI
{
    /// <summary>
    /// Names of the keys / buttons bound to an action for one control scheme, read from the input actions (so they
    /// follow the bindings, and later the remapping of UI-9). Keyboard keys use the real keyboard layout ("Q" on an
    /// AZERTY keyboard for the physical A key); gamepad buttons use the gamepad's own names when one is known
    /// ("Cross" on a PlayStation pad), else the Xbox-style letters ("A", "B").
    /// </summary>
    public static class ControlHints
    {
        public const string Gamepad = "Gamepad";
        public const string KeyboardLeft = "KeyboardLeft";
        public const string KeyboardRight = "KeyboardRight";

        /// <summary>
        /// Name of the first binding of <paramref name="actionName"/> (for example "Player/Interact") in
        /// <paramref name="scheme"/>. <paramref name="compositePart"/> picks a part of a composite ("left", "right").
        /// </summary>
        public static string KeyName(InputActionAsset actions, string actionName, string scheme, InputDevice device = null,
            string compositePart = null)
        {
            var action = actions != null ? actions.FindAction(actionName) : null;
            if (action == null)
            {
                return "?";
            }
            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite != (compositePart != null))
                {
                    continue;
                }
                if (compositePart != null && binding.name != compositePart)
                {
                    continue;
                }
                if (!InBindingGroup(binding.groups, scheme))
                {
                    continue;
                }
                return NameOfPath(binding.effectivePath, device);
            }
            return "?";
        }

        /// <summary>
        /// Short name of a scheme: "Gamepad", "Arrows", and for the left keyboard side its four move keys as the
        /// keyboard shows them ("ZQSD" on AZERTY, "WASD" on QWERTY).
        /// </summary>
        public static string SchemeLabel(InputActionAsset actions, string scheme)
        {
            switch (scheme)
            {
                case KeyboardLeft:
                    return KeyName(actions, "Player/Move", scheme, null, "up") +
                           KeyName(actions, "Player/Move", scheme, null, "left") +
                           KeyName(actions, "Player/Move", scheme, null, "down") +
                           KeyName(actions, "Player/Move", scheme, null, "right");
                case KeyboardRight:
                    return "Arrows";
                default:
                    return "Gamepad";
            }
        }

        private static bool InBindingGroup(string groups, string scheme)
        {
            if (string.IsNullOrEmpty(groups))
            {
                return false;
            }
            foreach (var group in groups.Split(InputBinding.Separator))
            {
                if (group == scheme)
                {
                    return true;
                }
            }
            return false;
        }

        private static string NameOfPath(string path, InputDevice device)
        {
            InputControl control = null;
            if (device != null)
            {
                control = InputControlPath.TryFindControl(device, path);
            }
            if (control == null && path.StartsWith("<Keyboard>") && Keyboard.current != null)
            {
                control = InputControlPath.TryFindControl(Keyboard.current, path);
            }
            if (control != null)
            {
                return !string.IsNullOrEmpty(control.shortDisplayName) ? control.shortDisplayName : control.displayName;
            }
            return InputControlPath.ToHumanReadableString(path,
                InputControlPath.HumanReadableStringOptions.UseShortNames |
                InputControlPath.HumanReadableStringOptions.OmitDevice);
        }
    }
}
