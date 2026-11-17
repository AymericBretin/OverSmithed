using System.Collections.Generic;
using System.Text;
using OverSmithed.Networking;
using OverSmithed.Players;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// Lobby overlay (uGUI) over the 3D room:
    /// - top center: the game code card (online only) with its Copy button ("Copied!" shows on the button),
    ///   and a short message bubble ("Player 2 left.") that fades away;
    /// - bottom center: "Hold [key] to start" with a bar, key = Throw (Triangle / Y) (host machine: only the first player who joined there, the
    ///   leader, can start; the next one takes over if they leave), or "Waiting for the host..." (clients);
    /// - bottom left: the controls of the devices in use (online: only this machine's devices; local: all);
    /// - bottom right: Leave (Escape does the same).
    /// The host starts by holding Throw (Triangle / Y) with its first joined player; the press that made a player join never
    /// counts. The buttons are for the mouse only: the join keys must not press a button.
    /// </summary>
    public class LobbyScreen : MonoBehaviour
    {
        // Start is held on the Throw button (Triangle / Y, F, /): not on A / Space / Enter, which join and press the
        // selected button. So the game can be started whatever is selected (Copy, Leave or nothing).
        private const string StartActionName = "Throw";
        private static readonly string[] AllSchemes =
            { ControlHints.Gamepad, ControlHints.KeyboardLeft, ControlHints.KeyboardRight };

        [Tooltip("The game's input actions: key names come from their bindings.")]
        [SerializeField] private InputActionAsset _actions;

        [Header("Game code (online)")]
        [SerializeField] private GameObject _codeCard;
        [SerializeField] private TMP_Text _codeText;
        [SerializeField] private Button _copyButton;
        [SerializeField] private TMP_Text _copyLabel;

        [Header("Messages")]
        [SerializeField] private CanvasGroup _toast;
        [SerializeField] private TMP_Text _toastText;
        [SerializeField] private float _toastTime = 3f;

        [Header("Start")]
        [SerializeField] private TMP_Text _startText;
        [Tooltip("Small line under the start text: which player starts (when several play on this machine).")]
        [SerializeField] private TMP_Text _leaderText;
        [SerializeField] private ProgressBar _startBar;
        [Tooltip("Seconds the start button (Throw) must be held.")]
        [SerializeField] private float _holdTime = 1f;

        [Header("Controls")]
        [SerializeField] private TMP_Text _controlsText;

        [Header("Leave")]
        [SerializeField] private Button _leaveButton;
        [SerializeField] private TMP_Text _leaveLabel;

        // Slots that were already joined last frame: only they can start holding.
        private readonly HashSet<LocalPlayerSlot> _joinedBefore = new();
        private SessionRoster _roster;
        private bool _isHost;
        private LocalPlayerSlot _holdingSlot;
        private float _holdProgress;
        private bool _leaving;
        private float _toastLeft;
        private float _copiedLeft;
        private string _controlsKey;

        private void OnEnable()
        {
            var networkManager = NetworkManager.Singleton;
            _isHost = networkManager != null && networkManager.IsHost;
            _leaveLabel.text = _isHost ? "Close game" : "Leave";
            _toast.gameObject.SetActive(false);
            _startBar.gameObject.SetActive(false);

            _copyButton.onClick.AddListener(CopyCode);
            _leaveButton.onClick.AddListener(Leave);
            // Moved by hand (UpdateButtonSelection), not by the EventSystem's automatic navigation.
            foreach (var button in new[] { _copyButton, _leaveButton })
            {
                button.navigation = new Navigation { mode = Navigation.Mode.None };
            }

            if (SessionService.Instance != null)
            {
                SessionService.Instance.SessionChanged += RefreshSession;
            }
            RefreshSession();

            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers != null)
            {
                localPlayers.Notification += ShowMessage;
                localPlayers.SetJoiningEnabled(true);
            }
            SessionRoster.InstanceChanged += BindRoster;
            BindRoster(SessionRoster.Instance);
        }

        private void OnDisable()
        {
            _copyButton.onClick.RemoveListener(CopyCode);
            _leaveButton.onClick.RemoveListener(Leave);
            if (SessionService.Instance != null)
            {
                SessionService.Instance.SessionChanged -= RefreshSession;
            }
            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers != null)
            {
                localPlayers.Notification -= ShowMessage;
                localPlayers.SetJoiningEnabled(false);
            }
            SessionRoster.InstanceChanged -= BindRoster;
            BindRoster(null);
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.sendNavigationEvents = true;
            }
        }

        private void BindRoster(SessionRoster roster)
        {
            if (_roster != null)
            {
                _roster.PlayerRemoved -= AnnounceDeparture;
            }
            _roster = roster;
            if (_roster != null)
            {
                _roster.PlayerRemoved += AnnounceDeparture;
            }
        }

        private void Update()
        {
            // Escape leaves the lobby (keyboard habit; a gamepad goes down to the Leave button).
            if (!_leaving && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Leave();
                return;
            }
            UpdateButtonSelection();
            UpdateStart();
            // After the checks above: a slot joined this frame only counts from the next frame (its join press must
            // not also press a button or start the game).
            RememberJoinedSlots();
            UpdateControls();
            UpdateToast();
            if (_copiedLeft > 0f)
            {
                _copiedLeft -= Time.unscaledDeltaTime;
                if (_copiedLeft <= 0f)
                {
                    _copyLabel.text = "Copy";
                }
            }
        }

        // ---------- Buttons: always on Copy or Leave ----------

        // There is no "room" state: one of the two buttons is always selected (Copy on top, online only; Leave at the
        // bottom). Up / down from a joined player move between them; left / right stay the skin choice and the start
        // is held on another button (Throw), so they work whatever is selected. A / Space / Enter press the selected
        // button, but only for a player who is already in: a device that is not in yet only joins (the EventSystem's
        // own navigation / submit are off here, this code does it for the joined players).
        private void UpdateButtonSelection()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }
            eventSystem.sendNavigationEvents = false;
            var copy = _copyButton.gameObject;
            var leave = _leaveButton.gameObject;
            var copyAvailable = copy.activeInHierarchy;

            var selected = eventSystem.currentSelectedGameObject;
            var hovering = SelectableFeedback.IsHoverSelection(selected);
            if (!hovering && (selected == null || (selected != leave && (selected != copy || !copyAvailable))))
            {
                eventSystem.SetSelectedGameObject(copyAvailable ? copy : leave);
            }

            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers == null || _leaving)
            {
                return;
            }
            foreach (var slot in localPlayers.Slots)
            {
                if (slot.State != LocalPlayerSlot.SlotState.Joined || !_joinedBefore.Contains(slot))
                {
                    continue;
                }
                var step = ReadVerticalStep(slot);
                if (step > 0 && copyAvailable)
                {
                    eventSystem.SetSelectedGameObject(copy);
                }
                else if (step < 0)
                {
                    eventSystem.SetSelectedGameObject(leave);
                }
                var press = slot.Input.actions.FindAction(PressActionName);
                if (press != null && press.WasPressedThisFrame())
                {
                    var target = eventSystem.currentSelectedGameObject;
                    if (target == copy || target == leave)
                    {
                        // Same as a UI "submit": the button plays its press feedback and runs its click.
                        UnityEngine.EventSystems.ExecuteEvents.Execute(target,
                            new UnityEngine.EventSystems.BaseEventData(eventSystem),
                            UnityEngine.EventSystems.ExecuteEvents.submitHandler);
                    }
                    return;
                }
            }
        }

        // One step per push of the player's own Move (stick, D-pad, ZQSD/WASD, arrows).
        private int ReadVerticalStep(LocalPlayerSlot slot)
        {
            var move = slot.Input.actions.FindAction("Move");
            if (move == null)
            {
                return 0;
            }
            var y = move.ReadValue<Vector2>().y;
            _verticalHeld.TryGetValue(slot, out var held);
            if (held)
            {
                if (Mathf.Abs(y) < 0.3f)
                {
                    _verticalHeld[slot] = false;
                }
                return 0;
            }
            if (Mathf.Abs(y) >= 0.6f && Mathf.Abs(y) > Mathf.Abs(move.ReadValue<Vector2>().x))
            {
                _verticalHeld[slot] = true;
                return y > 0f ? 1 : -1;
            }
            return 0;
        }

        private const string PressActionName = "Interact";
        private readonly Dictionary<LocalPlayerSlot, bool> _verticalHeld = new();

        // ---------- Start ----------

        // Only the leader starts: the first player of this machine who joined (roster order = join order). If they
        // leave, the next one becomes the leader. On a client nobody starts (the host does).
        private void UpdateStart()
        {
            if (!_isHost)
            {
                _startText.text = "Waiting for the host to start...";
                _leaderText.gameObject.SetActive(false);
                _startBar.gameObject.SetActive(false);
                return;
            }

            var leader = FindLeader(out var leaderEntry);
            var canStart = !_leaving && leader != null && !_roster.IsLocked;
            if (canStart && _holdingSlot == null && _joinedBefore.Contains(leader))
            {
                var startAction = leader.Input.actions.FindAction(StartActionName);
                if (startAction != null && startAction.WasPressedThisFrame())
                {
                    _holdingSlot = leader;
                }
            }
            if (!canStart || _holdingSlot != leader || !IsHoldingStart(_holdingSlot))
            {
                _holdingSlot = null;
                _holdProgress = 0f;
            }
            else
            {
                _holdProgress += Time.unscaledDeltaTime / Mathf.Max(0.01f, _holdTime);
            }

            if (canStart)
            {
                var key = ControlHints.KeyName(_actions, "Player/" + StartActionName, leader.ControlScheme, leader.Device);
                _startText.text = $"Hold <color=#B8801F>{key}</color> to start";
                // With several players on this machine, say who the leader is (by their skin: nothing above heads).
                var skins = _roster.Skins;
                var skin = skins != null ? skins.Get(leaderEntry.SkinIndex) : null;
                var several = CountJoinedLocalPlayers() > 1;
                _leaderText.gameObject.SetActive(several && skin != null);
                if (several && skin != null)
                {
                    _leaderText.text = $"{skin.DisplayName} starts the game";
                }
            }
            else
            {
                _startText.text = "Join to play!";
                _leaderText.gameObject.SetActive(false);
            }
            _startBar.gameObject.SetActive(canStart);
            _startBar.Value = Mathf.Clamp01(_holdProgress);

            if (_holdProgress >= 1f)
            {
                _leaving = true;
                _holdingSlot = null;
                if (!SessionService.Instance.LaunchGame())
                {
                    _leaving = false;
                    _holdProgress = 0f;
                    ShowMessage("Could not start the game.");
                }
            }
        }

        // The first roster entry (join order) of this machine whose local slot is joined.
        private LocalPlayerSlot FindLeader(out RosterEntry entry)
        {
            entry = default;
            var localPlayers = LocalPlayersManager.Instance;
            var networkManager = NetworkManager.Singleton;
            if (_roster == null || localPlayers == null || networkManager == null)
            {
                return null;
            }
            for (var i = 0; i < _roster.Count; i++)
            {
                if (_roster[i].ClientId != networkManager.LocalClientId)
                {
                    continue;
                }
                var slot = localPlayers.FindSlot(_roster[i].LocalIndex);
                if (slot != null && slot.State == LocalPlayerSlot.SlotState.Joined)
                {
                    entry = _roster[i];
                    return slot;
                }
            }
            return null;
        }

        private static int CountJoinedLocalPlayers()
        {
            var count = 0;
            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers != null)
            {
                foreach (var slot in localPlayers.Slots)
                {
                    if (slot.State == LocalPlayerSlot.SlotState.Joined)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        // Slots joined before this frame: the press that makes a player join (it can be confirmed in the same frame
        // on the host) never counts as a start press.
        private void RememberJoinedSlots()
        {
            _joinedBefore.Clear();
            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers == null)
            {
                return;
            }
            foreach (var slot in localPlayers.Slots)
            {
                if (slot.State == LocalPlayerSlot.SlotState.Joined)
                {
                    _joinedBefore.Add(slot);
                }
            }
        }

        private static bool IsHoldingStart(LocalPlayerSlot slot)
        {
            if (slot == null || slot.State != LocalPlayerSlot.SlotState.Joined)
            {
                return false;
            }
            var startAction = slot.Input.actions.FindAction(StartActionName);
            return startAction != null && startAction.IsPressed();
        }

        // ---------- Controls card ----------

        // Local play: every device can still join, so all of them are listed. Online: only the devices used on this
        // machine (or all of them before anyone joined).
        private void UpdateControls()
        {
            var schemes = new List<string>();
            var devices = new Dictionary<string, InputDevice>();
            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers != null)
            {
                foreach (var slot in localPlayers.Slots)
                {
                    var scheme = slot.ControlScheme;
                    if (string.IsNullOrEmpty(scheme) || devices.ContainsKey(scheme))
                    {
                        continue;
                    }
                    devices[scheme] = slot.Device;
                    schemes.Add(scheme);
                }
            }
            var online = SessionService.Instance != null && SessionService.Instance.Mode == SessionMode.Online;
            if (!online || schemes.Count == 0)
            {
                schemes.Clear();
                schemes.AddRange(AllSchemes);
            }
            if (!devices.ContainsKey(ControlHints.Gamepad) && UnityEngine.InputSystem.Gamepad.current != null)
            {
                devices[ControlHints.Gamepad] = UnityEngine.InputSystem.Gamepad.current;
            }

            // A small table: one line per device, columns Join / Skin / Leave (TMP <pos> tags place the columns).
            const string join = "<pos=27%>", skinColumn = "<pos=45%>", leaveColumn = "<pos=77%>";
            var text = new StringBuilder();
            text.Append($"<color=#8B5A2B>{join}Join{skinColumn}Skin{leaveColumn}Leave</color>");
            foreach (var scheme in schemes)
            {
                devices.TryGetValue(scheme, out var device);
                var joinKey = ControlHints.KeyName(_actions, "Join/Join", scheme, device);
                var leaveKey = ControlHints.KeyName(_actions, "Player/Leave", scheme, device);
                var skinKeys = scheme == ControlHints.Gamepad
                    ? "Stick"
                    : ControlHints.KeyName(_actions, "Player/Move", scheme, device, "left") + " / " +
                      ControlHints.KeyName(_actions, "Player/Move", scheme, device, "right");
                text.Append('\n');
                text.Append($"<color=#8B5A2B>{ControlHints.SchemeLabel(_actions, scheme)}</color>");
                text.Append($"{join}{joinKey}{skinColumn}{skinKeys}{leaveColumn}{leaveKey}");
            }
            var value = text.ToString();
            if (value != _controlsKey)
            {
                _controlsKey = value;
                _controlsText.text = value;
            }
        }

        // ---------- Session, messages ----------

        private void RefreshSession()
        {
            var session = SessionService.Instance;
            var code = session != null ? session.SessionCode : null;
            var showCode = session != null && session.Mode == SessionMode.Online && code != null;
            _codeCard.SetActive(showCode);
            if (showCode)
            {
                _codeText.text = code;
            }
        }

        private void AnnounceDeparture(RosterEntry entry)
        {
            ShowMessage($"Player {entry.PlayerNumber} left.");
        }

        private void ShowMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            _toastText.text = message;
            _toast.gameObject.SetActive(true);
            _toast.alpha = 1f;
            _toastLeft = _toastTime;
        }

        private void UpdateToast()
        {
            if (_toastLeft <= 0f)
            {
                return;
            }
            _toastLeft -= Time.unscaledDeltaTime;
            _toast.alpha = Mathf.Clamp01(_toastLeft / 0.4f);
            if (_toastLeft <= 0f)
            {
                _toast.gameObject.SetActive(false);
            }
        }

        private void CopyCode()
        {
            GUIUtility.systemCopyBuffer = SessionService.Instance.SessionCode;
            _copyLabel.text = "Copied!";
            _copiedLeft = 1.5f;
        }

        private void Leave()
        {
            if (_leaving)
            {
                return;
            }
            _leaving = true;
            _leaveButton.interactable = false;
            SessionService.Instance.Leave();
        }
    }
}
