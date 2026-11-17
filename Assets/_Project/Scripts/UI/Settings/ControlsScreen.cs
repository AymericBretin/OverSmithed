using System;
using System.Collections.Generic;
using OverSmithed.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// The controls panel (prefab UI/Prefabs/Screens/ControlsPanel): one tab per control scheme (gamepad, left keyboard
    /// side, right keyboard side), one line per action. Pressing a line's button waits for the new key or button
    /// (Escape / Start cancels, it stops by itself after a few seconds). Rules:
    /// a key used by another action of the same tab = the two actions swap keys; a key used on the other keyboard side
    /// = refused (two players share the keyboard), with a message on the line. Saved right away
    /// (<see cref="ControlBindings"/>). "Reset to default" resets the tab shown.
    /// The screen that opens it (settings panel, later the pause menu) selects <see cref="FirstSelectable"/>, listens to
    /// <see cref="BackButton"/> and ignores its own Cancel while <see cref="IsListening"/>.
    /// </summary>
    public class ControlsScreen : MonoBehaviour
    {
        [Serializable]
        private class Tab
        {
            [Tooltip("Control scheme of the input actions: Gamepad, KeyboardLeft or KeyboardRight.")]
            public string Scheme;
            public Button Button;
            public TMPro.TMP_Text Label;
            [Tooltip("Shown under the tab while it is open.")]
            public GameObject Marker;
        }

        // Gamepad controls a player can pick: the same on every gamepad (Xbox, PlayStation, Switch...). The D-pad and
        // the sticks move, Start and Select are kept for joining and the pause menu.
        private static readonly HashSet<string> GamepadButtons = new()
        {
            "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder",
            "leftTrigger", "rightTrigger", "leftStickPress", "rightStickPress"
        };

        [Tooltip("The game's input actions. The screen edits its own copy and saves it through ControlBindings.")]
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Tab[] _tabs;
        [Tooltip("Move lines (keyboard only).")]
        [SerializeField] private RemapRow[] _moveRows;
        [Tooltip("Interact, Use, Throw, Dash, Leave lines.")]
        [SerializeField] private RemapRow[] _actionRows;
        [Tooltip("Shown instead of the Move lines on the gamepad tab.")]
        [SerializeField] private GameObject _gamepadMoveText;
        [SerializeField] private Button _resetButton;
        [SerializeField] private Button _backButton;
        [Tooltip("Seconds to press the new key before the line gives up.")]
        [SerializeField] private float _listenTime = 5f;

        private InputActionAsset _edit;
        private string _scheme;
        private RemapRow _listeningRow;
        private int _listenStartFrame;
        private float _listenLeft;
        private IDisposable _pressListener;
        private int _busyUntilFrame = -1;
        private bool _restoreNavigation;
        private bool _navigationWasOn = true;

        /// <summary>What a gamepad should select when the panel opens: the open tab.</summary>
        public Selectable FirstSelectable => TabOf(_scheme).Button;

        /// <summary>The Back button: the screen that opened this panel closes it on click.</summary>
        public Button BackButton => _backButton;

        /// <summary>
        /// True while a line waits for a key, and during the frame after: then Escape / B are the new key or the
        /// cancel, they must not also close the panel.
        /// </summary>
        public bool IsListening => _listeningRow != null || Time.frameCount <= _busyUntilFrame;

        private void Awake()
        {
            _edit = Instantiate(_actions);
            _scheme = Gamepad.current != null ? ControlHints.Gamepad : ControlHints.KeyboardLeft;

            foreach (var tab in _tabs)
            {
                var scheme = tab.Scheme;
                tab.Button.onClick.AddListener(() =>
                {
                    if (!IsListening)
                    {
                        ShowScheme(scheme);
                    }
                });
            }
            foreach (var row in AllRows())
            {
                var current = row;
                row.Button.onClick.AddListener(() => StartListening(current));
            }
            _resetButton.onClick.AddListener(() =>
            {
                if (IsListening)
                {
                    return;
                }
                ControlBindings.ResetScheme(_edit, _scheme);
                RefreshKeys();
            });
        }

        private void OnEnable()
        {
            // Start from what is saved (the keys may have changed since the last time the panel was open).
            ControlBindings.Apply(_edit);
            ShowScheme(_scheme);
        }

        private void OnDisable()
        {
            StopListening();
            if (_restoreNavigation)
            {
                SetNavigation(_navigationWasOn);
                _restoreNavigation = false;
            }
        }

        private void OnDestroy()
        {
            _pressListener?.Dispose();
            if (_edit != null)
            {
                Destroy(_edit);
            }
        }

        private void Update()
        {
            if (_listeningRow != null)
            {
                _listenLeft -= Time.unscaledDeltaTime;
                if (_listenLeft <= 0f)
                {
                    StopListening();
                }
            }
            // Menu navigation comes back one frame after the new key, so that press does not also move or click.
            if (_restoreNavigation && Time.frameCount > _busyUntilFrame)
            {
                SetNavigation(_navigationWasOn);
                _restoreNavigation = false;
            }
        }

        private void ShowScheme(string scheme)
        {
            _scheme = scheme;
            var gamepad = scheme == ControlHints.Gamepad;
            foreach (var tab in _tabs)
            {
                if (tab.Marker != null)
                {
                    tab.Marker.SetActive(tab.Scheme == scheme);
                }
            }
            foreach (var row in AllRows())
            {
                row.gameObject.SetActive(!(gamepad && row.KeyboardOnly));
            }
            if (_gamepadMoveText != null)
            {
                _gamepadMoveText.SetActive(gamepad);
            }
            RefreshKeys();
            BuildNavigation();
        }

        private void RefreshKeys()
        {
            foreach (var row in AllRows())
            {
                if (row.gameObject.activeSelf)
                {
                    row.ShowKey(KeyName(row));
                }
            }
            foreach (var tab in _tabs)
            {
                if (tab.Label != null)
                {
                    tab.Label.text = ControlHints.SchemeLabel(_edit, tab.Scheme);
                }
            }
        }

        private string KeyName(RemapRow row)
        {
            InputDevice device = _scheme == ControlHints.Gamepad ? Gamepad.current : Keyboard.current;
            return ControlHints.KeyName(_edit, row.ActionName, _scheme, device, row.CompositePart);
        }

        // ---- Listening for the new key ----

        private void StartListening(RemapRow row)
        {
            if (IsListening)
            {
                return;
            }
            _listeningRow = row;
            _listenStartFrame = Time.frameCount;
            _listenLeft = _listenTime;
            row.ShowWaiting(_scheme == ControlHints.Gamepad ? "Press a button..." : "Press a key...");

            // The menu must not react to the keys pressed now (moving, clicking, going back).
            if (!_restoreNavigation)
            {
                _navigationWasOn = EventSystem.current == null || EventSystem.current.sendNavigationEvents;
            }
            SetNavigation(false);
            _restoreNavigation = true;
            _pressListener = InputSystem.onAnyButtonPress.Call(HandlePress);
        }

        private void StopListening()
        {
            _pressListener?.Dispose();
            _pressListener = null;
            if (_listeningRow == null)
            {
                return;
            }
            _listeningRow = null;
            _busyUntilFrame = Time.frameCount + 1;
            RefreshKeys();
        }

        private void HandlePress(InputControl control)
        {
            // The press that opened the line is not the new key.
            if (_listeningRow == null || Time.frameCount == _listenStartFrame)
            {
                return;
            }
            var keyboardTab = _scheme != ControlHints.Gamepad;
            switch (control.device)
            {
                case Keyboard keyboard:
                    if (control == keyboard.escapeKey)
                    {
                        StopListening();
                    }
                    else if (keyboardTab)
                    {
                        TryBind("<Keyboard>/" + control.name);
                    }
                    break;
                case Gamepad pad:
                    if (control == pad.startButton || control == pad.selectButton)
                    {
                        StopListening();
                    }
                    else if (!keyboardTab && GamepadButtons.Contains(control.name))
                    {
                        TryBind("<Gamepad>/" + control.name);
                    }
                    break;
            }
        }

        private void TryBind(string newPath)
        {
            var row = _listeningRow;
            var action = _edit.FindAction(row.ActionName);
            var mine = action != null ? BindingIndices(action, _scheme, row.CompositePart) : new List<int>();
            if (mine.Count == 0)
            {
                StopListening();
                return;
            }
            var oldPath = action.bindings[mine[0]].effectivePath;

            // Two players share the keyboard: a key of the other side is refused.
            if (_scheme != ControlHints.Gamepad)
            {
                var otherSide = _scheme == ControlHints.KeyboardLeft ? ControlHints.KeyboardRight : ControlHints.KeyboardLeft;
                if (FindUser(otherSide, newPath, out var otherRow, out _, out _))
                {
                    StopListening();
                    row.ShowMessage($"{ControlHints.SchemeLabel(_edit, otherSide)}: {otherRow.DisplayName}", UiColor.Danger);
                    return;
                }
            }

            // Same side: the other action takes this action's old key (or just loses this key if it has another one).
            RemapRow swapped = null;
            if (FindUser(_scheme, newPath, out var sameRow, out var sameAction, out var sameIndex) && sameRow != row)
            {
                var hasAnotherKey = false;
                foreach (var index in BindingIndices(sameAction, _scheme, sameRow.CompositePart))
                {
                    if (index != sameIndex && !string.IsNullOrEmpty(sameAction.bindings[index].effectivePath))
                    {
                        hasAnotherKey = true;
                    }
                }
                SetPath(sameAction, sameIndex, hasAnotherKey ? string.Empty : oldPath);
                if (!hasAnotherKey)
                {
                    swapped = sameRow;
                }
            }

            // The new key replaces every key of the action on this tab (Dash had RB and RT: now only the new one).
            SetPath(action, mine[0], newPath);
            for (var i = 1; i < mine.Count; i++)
            {
                SetPath(action, mine[i], string.Empty);
            }
            ControlBindings.Save(_edit);
            StopListening();
            if (swapped != null)
            {
                swapped.ShowMessage(KeyName(swapped), UiColor.Gold);
            }
        }

        // The line (and binding) of this tab that uses the key, if any.
        private bool FindUser(string scheme, string path, out RemapRow user, out InputAction action, out int bindingIndex)
        {
            foreach (var row in AllRows())
            {
                if (scheme == ControlHints.Gamepad && row.KeyboardOnly)
                {
                    continue;
                }
                var rowAction = _edit.FindAction(row.ActionName);
                if (rowAction == null)
                {
                    continue;
                }
                foreach (var index in BindingIndices(rowAction, scheme, row.CompositePart))
                {
                    if (string.Equals(rowAction.bindings[index].effectivePath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        user = row;
                        action = rowAction;
                        bindingIndex = index;
                        return true;
                    }
                }
            }
            user = null;
            action = null;
            bindingIndex = -1;
            return false;
        }

        // Indexes (in the action) of the bindings of this scheme: a normal action, or one part of a composite.
        private static List<int> BindingIndices(InputAction action, string scheme, string compositePart)
        {
            var indices = new List<int>();
            var bindings = action.bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.isComposite || binding.isPartOfComposite != (compositePart != null))
                {
                    continue;
                }
                if (compositePart != null && !string.Equals(binding.name, compositePart, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (ControlBindings.InGroup(binding.groups, scheme))
                {
                    indices.Add(i);
                }
            }
            return indices;
        }

        // The default key needs no override (keeps the save small); an empty path turns the binding off.
        private static void SetPath(InputAction action, int index, string path)
        {
            if (path == action.bindings[index].path)
            {
                action.RemoveBindingOverride(index);
            }
            else
            {
                action.ApplyBindingOverride(index, path);
            }
        }

        private static void SetNavigation(bool on)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = on;
            }
        }

        // ---- Navigation (tabs, two columns, buttons) ----

        private void BuildNavigation()
        {
            var left = ActiveButtons(_moveRows);
            var right = ActiveButtons(_actionRows);
            var tab = TabOf(_scheme).Button;
            var firstRow = left.Count > 0 ? left[0] : right.Count > 0 ? right[0] : _backButton;

            for (var i = 0; i < _tabs.Length; i++)
            {
                SetNav(_tabs[i].Button,
                    up: _resetButton,
                    down: firstRow,
                    left: _tabs[(i + _tabs.Length - 1) % _tabs.Length].Button,
                    right: _tabs[(i + 1) % _tabs.Length].Button);
            }
            LinkColumn(left, right, tab, _resetButton, isLeft: true);
            LinkColumn(right, left, tab, _backButton, isLeft: false);
            SetNav(_resetButton, up: Last(left) ?? Last(right), down: tab, left: null, right: _backButton);
            SetNav(_backButton, up: Last(right) ?? Last(left), down: tab, left: _resetButton, right: null);
        }

        private static void LinkColumn(List<Selectable> column, List<Selectable> other, Selectable tab, Selectable below,
            bool isLeft)
        {
            for (var i = 0; i < column.Count; i++)
            {
                var side = other.Count > 0 ? other[Mathf.Min(i, other.Count - 1)] : null;
                SetNav(column[i],
                    up: i == 0 ? tab : column[i - 1],
                    down: i == column.Count - 1 ? below : column[i + 1],
                    left: isLeft ? null : side,
                    right: isLeft ? side : null);
            }
        }

        private static void SetNav(Selectable selectable, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            selectable.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right
            };
        }

        private static List<Selectable> ActiveButtons(RemapRow[] rows)
        {
            var buttons = new List<Selectable>();
            foreach (var row in rows)
            {
                if (row.gameObject.activeSelf)
                {
                    buttons.Add(row.Button);
                }
            }
            return buttons;
        }

        private static Selectable Last(List<Selectable> list) => list.Count > 0 ? list[^1] : null;

        private Tab TabOf(string scheme)
        {
            foreach (var tab in _tabs)
            {
                if (tab.Scheme == scheme)
                {
                    return tab;
                }
            }
            return _tabs[0];
        }

        private IEnumerable<RemapRow> AllRows()
        {
            foreach (var row in _moveRows)
            {
                yield return row;
            }
            foreach (var row in _actionRows)
            {
                yield return row;
            }
        }
    }
}
