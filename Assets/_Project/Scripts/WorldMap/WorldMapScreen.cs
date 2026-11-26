using System.Collections.Generic;
using OverSmithed.Core;
using OverSmithed.Players;
using OverSmithed.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// World map UI and level start (FLOW-5). On the host, a joined player of this machine presses Interact while the
    /// walker stands on a built level: <see cref="MapWalker.TryEnterLevel"/> starts it for everyone. Away from a spot,
    /// on a level not built yet, or on a client, Interact does nothing. Away from a spot, the hint at the bottom says
    /// to walk to a level; on a spot, the level popup (UI-12, <see cref="LevelPopup"/>) shows the level and what
    /// Interact does here (with this machine's real key names). When a level starts, the screen fades to black on
    /// every machine. Leaving the map = the pause menu (Escape / Start), not this screen.
    /// </summary>
    public class WorldMapScreen : MonoBehaviour
    {
        private const string InteractActionName = "Interact";

        [SerializeField] private TMP_Text _hintText;
        [Tooltip("Hidden while a level starts. Empty = the hint text's own object.")]
        [SerializeField] private GameObject _hintPanel;
        [Tooltip("Card above the circle the walker stands on.")]
        [SerializeField] private LevelPopup _popup;

        [Header("Fade when a level starts")]
        [Tooltip("Full-screen black image (no raycast), alpha 0 at start.")]
        [SerializeField] private CanvasGroup _fade;
        [Tooltip("Seconds after Interact before the fade starts (the jump plays first).")]
        [SerializeField, Min(0f)] private float _fadeDelay = 0.5f;
        [SerializeField, Min(0.01f)] private float _fadeTime = 0.45f;

        private const string KeyColor = "#B8801F";

        private bool _isHost;
        private string _shownHint;
        // Time since the level start (negative = no fade).
        private float _fadeClock = -1f;
        private readonly List<string> _keyNames = new();

        private void Start()
        {
            var networkManager = NetworkManager.Singleton;
            _isHost = networkManager != null && networkManager.IsServer;
            if (_hintPanel == null && _hintText != null)
            {
                _hintPanel = _hintText.gameObject;
            }
            if (_fade != null)
            {
                _fade.alpha = 0f;
                _fade.blocksRaycasts = false;
            }
        }

        private void OnEnable()
        {
            MapWalker.LevelLaunching += HandleLevelLaunching;
        }

        private void OnDisable()
        {
            MapWalker.LevelLaunching -= HandleLevelLaunching;
        }

        private void HandleLevelLaunching(LevelNode node)
        {
            // Null = cancelled: show the map again.
            _fadeClock = node != null ? 0f : -1f;
            if (_fade != null && node == null)
            {
                _fade.alpha = 0f;
            }
        }

        private void Update()
        {
            var walker = MapWalker.Current;
            UpdateFade();

            var launching = walker != null && walker.IsLaunching;
            var node = walker != null && !launching ? walker.CurrentNode : null;
            // On a spot the popup says it all; the bottom hint only says to walk to a level.
            var showHint = !launching && node == null;
            if (_hintPanel != null && _hintPanel.activeSelf != showHint)
            {
                _hintPanel.SetActive(showHint);
            }
            if (_popup != null)
            {
                if (node != null)
                {
                    _popup.Show(node, BuildAction(node));
                }
                else
                {
                    _popup.Hide();
                }
            }
            if (launching)
            {
                return;
            }
            if (showHint)
            {
                ShowHint(_isHost ? "Walk to a level" : "The host walks to a level...");
            }

            // The pause menu is open (or this press just closed it): Interact belongs to the menu.
            if (!_isHost || walker == null || GamePause.BlocksPlayerInput || !AnyLocalPlayerPressedInteract())
            {
                return;
            }
            walker.TryEnterLevel(); // does nothing away from a spot or on a level not built yet
        }

        private void UpdateFade()
        {
            if (_fadeClock < 0f || _fade == null)
            {
                return;
            }
            _fadeClock += Time.deltaTime;
            var t = Mathf.Clamp01((_fadeClock - _fadeDelay) / _fadeTime);
            _fade.alpha = t * t * (3f - 2f * t);
        }

        // ---------- Hint and popup ----------

        // Last line of the popup: what Interact does on this spot, for this machine.
        private string BuildAction(LevelNode node)
        {
            if (!node.HasLevel)
            {
                return "Coming soon";
            }
            return _isHost
                ? $"Press <color={KeyColor}>{InteractKeys()}</color> to play"
                : "The host starts the level";
        }

        private void ShowHint(string hint)
        {
            if (_hintText == null || hint == _shownHint)
            {
                return;
            }
            _shownHint = hint;
            _hintText.text = hint;
        }

        // The Interact keys of this machine's joined players, each name once ("A / Space").
        private string InteractKeys()
        {
            _keyNames.Clear();
            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers != null)
            {
                foreach (var slot in localPlayers.Slots)
                {
                    if (slot == null || slot.State != LocalPlayerSlot.SlotState.Joined)
                    {
                        continue;
                    }
                    var key = ControlHints.KeyName(slot.Input.actions, "Player/" + InteractActionName,
                        slot.ControlScheme, slot.Device);
                    if (!_keyNames.Contains(key))
                    {
                        _keyNames.Add(key);
                    }
                }
            }
            return _keyNames.Count > 0 ? string.Join(" / ", _keyNames) : "Interact";
        }

        // ---------- Input ----------

        private static bool AnyLocalPlayerPressedInteract()
        {
            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers == null)
            {
                return false;
            }
            var pressed = false;
            foreach (var slot in localPlayers.Slots)
            {
                if (slot == null || slot.State != LocalPlayerSlot.SlotState.Joined)
                {
                    continue;
                }
                var interact = slot.Input.actions.FindAction(InteractActionName);
                pressed |= interact != null && interact.WasPressedThisFrame();
            }
            return pressed;
        }
    }
}
