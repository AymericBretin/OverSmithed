using OverSmithed.Core;
using OverSmithed.Networking;
using OverSmithed.Players;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// Temporary world map (FLOW-1), until the real map and its walker (FLOW-3, FLOW-4).
    /// On the host, any of its local players presses Interact to take everyone to the level.
    /// Clients just wait. The button leaves the game (host: closes it for everyone).
    /// </summary>
    public class WorldMapPlaceholder : MonoBehaviour
    {
        private const string InteractActionName = "Interact";

        [Tooltip("Level scene started by Interact. Must be in the build list.")]
        [SerializeField] private string _levelScene = SceneNames.Game;
        [SerializeField] private TMP_Text _hintText;
        [SerializeField] private Button _leaveButton;

        private bool _isHost;
        private bool _levelRequested;

        private void Start()
        {
            var networkManager = NetworkManager.Singleton;
            _isHost = networkManager != null && networkManager.IsServer;

            _hintText.text = _isHost
                ? "Press Interact to start the level."
                : "Waiting for the host to start the level...";

            var leaveLabel = _leaveButton.GetComponentInChildren<TMP_Text>();
            if (leaveLabel != null)
            {
                leaveLabel.text = _isHost ? "Close game" : "Leave";
            }
            _leaveButton.onClick.AddListener(OnLeaveClicked);
        }

        private void OnDestroy()
        {
            _leaveButton.onClick.RemoveListener(OnLeaveClicked);
        }

        private void Update()
        {
            if (!_isHost || _levelRequested || !AnyLocalPlayerPressedInteract())
            {
                return;
            }
            // Only once: a second load while the first one runs would be refused by Netcode anyway.
            _levelRequested = SceneFlow.StartLevel(_levelScene);
        }

        private static bool AnyLocalPlayerPressedInteract()
        {
            var localPlayers = LocalPlayersManager.Instance;
            if (localPlayers == null)
            {
                return false;
            }
            foreach (var slot in localPlayers.Slots)
            {
                if (slot.State != LocalPlayerSlot.SlotState.Joined)
                {
                    continue;
                }
                var interact = slot.Input.actions.FindAction(InteractActionName);
                if (interact != null && interact.WasPressedThisFrame())
                {
                    return true;
                }
            }
            return false;
        }

        private void OnLeaveClicked()
        {
            _leaveButton.interactable = false;
            SessionService.Instance.Leave();
        }
    }
}
