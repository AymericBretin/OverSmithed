using OverSmithed.Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Lobby
{
    /// <summary>
    /// Screen label under one lobby spot (uGUI, follows the spot on screen). Free spot: "Press to join" (online, once
    /// this computer has its player: "Waiting for a player").
    /// Taken spot: the skin name, with ◀ ▶ arrows when the player is on this machine (they can change it).
    /// Reads the roster on every machine: nothing goes on the network.
    /// </summary>
    public class LobbySpotLabel : MonoBehaviour
    {
        [SerializeField] private LobbySpot _spot;
        [Tooltip("Empty: the main camera.")]
        [SerializeField] private Camera _camera;
        [Tooltip("World offset from the spot (feet) to where the label sits.")]
        [SerializeField] private Vector3 _worldOffset = new(0f, -0.15f, -0.6f);

        [Header("Parts")]
        [SerializeField] private GameObject _freeGroup;
        [SerializeField] private GameObject _takenGroup;
        [SerializeField] private TMP_Text _skinName;
        [SerializeField] private RectTransform _leftArrow;
        [SerializeField] private RectTransform _rightArrow;

        [Header("Arrow bump")]
        [SerializeField] private float _bumpScale = 1.35f;
        [SerializeField] private float _bumpTime = 0.2f;

        private TMP_Text _freeText;
        private RectTransform _rect;
        private RectTransform _parent;
        private float _leftBump = 1f;
        private float _rightBump = 1f;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _parent = (RectTransform)transform.parent;
        }

        private void OnEnable()
        {
            LobbySkinPicker.SkinChangeRequested += HandleSkinChangeRequested;
        }

        private void OnDisable()
        {
            LobbySkinPicker.SkinChangeRequested -= HandleSkinChangeRequested;
        }

        private void HandleSkinChangeRequested(byte playerNumber, int direction)
        {
            if (_spot != null && playerNumber == _spot.PlayerNumber)
            {
                if (direction < 0)
                {
                    _leftBump = _bumpScale;
                }
                else
                {
                    _rightBump = _bumpScale;
                }
            }
        }

        private void LateUpdate()
        {
            if (_spot == null)
            {
                return;
            }
            FollowSpot();

            var taken = TryGetEntry(out var entry);
            _freeGroup.SetActive(!taken);
            _takenGroup.SetActive(taken);
            if (!taken)
            {
                // Online, a computer that already has its player can't add another: the free spot waits for others.
                if (_freeText == null)
                {
                    _freeText = _freeGroup.GetComponentInChildren<TMP_Text>(true);
                }
                if (_freeText != null)
                {
                    _freeText.text = OnlineAndAlreadyJoined() ? "Waiting for a player" : "Press to join";
                }
                return;
            }
            var skins = SessionRoster.Instance.Skins;
            var skin = skins != null ? skins.Get(entry.SkinIndex) : null;
            _skinName.text = skin != null ? skin.DisplayName : "?";

            var networkManager = NetworkManager.Singleton;
            var isLocal = networkManager != null && entry.ClientId == networkManager.LocalClientId;
            _leftArrow.gameObject.SetActive(isLocal);
            _rightArrow.gameObject.SetActive(isLocal);
            var settle = Time.unscaledDeltaTime / Mathf.Max(0.01f, _bumpTime) * (_bumpScale - 1f);
            _leftBump = Mathf.MoveTowards(_leftBump, 1f, settle);
            _rightBump = Mathf.MoveTowards(_rightBump, 1f, settle);
            _leftArrow.localScale = Vector3.one * _leftBump;
            _rightArrow.localScale = Vector3.one * _rightBump;
        }

        private void FollowSpot()
        {
            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null)
            {
                return;
            }
            var screen = cam.WorldToScreenPoint(_spot.transform.position + _worldOffset);
            var canvas = _parent.GetComponentInParent<Canvas>();
            var eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screen, eventCamera, out var local))
            {
                _rect.anchoredPosition = local;
            }
        }

        private static bool OnlineAndAlreadyJoined()
        {
            var session = SessionService.Instance;
            var localPlayers = OverSmithed.Players.LocalPlayersManager.Instance;
            return session != null && session.Mode == SessionMode.Online && localPlayers != null &&
                   localPlayers.Slots.Count > 0;
        }

        private bool TryGetEntry(out RosterEntry entry)
        {
            var roster = SessionRoster.Instance;
            if (roster != null)
            {
                for (var i = 0; i < roster.Count; i++)
                {
                    if (roster[i].PlayerNumber == _spot.PlayerNumber)
                    {
                        entry = roster[i];
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }
    }
}
