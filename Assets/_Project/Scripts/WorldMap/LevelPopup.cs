using OverSmithed.Levels;
using OverSmithed.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// The card above a level circle on the world map (UI-12): preview picture on the left; on the right the level
    /// name, the 3 star goals (coins needed for each star; a star is full when the best result reached it), the best
    /// coins (the host's records, <see cref="LevelRecords"/>) and one line saying what to do. Pops in when the walker
    /// stands on a circle, follows the circle on screen, shrinks away when it leaves. <see cref="WorldMapScreen"/>
    /// decides when and with which action line. Looks only, local to each machine.
    /// </summary>
    public class LevelPopup : MonoBehaviour
    {
        [Tooltip("The card that moves and pops (pivot at its bottom center).")]
        [SerializeField] private RectTransform _card;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private Image _preview;
        [Tooltip("Shown instead of the picture when the level has none yet (not built: a big \"?\").")]
        [SerializeField] private GameObject _previewPlaceholder;
        [Tooltip("Row of the 3 star goals. Hidden for a level not built yet.")]
        [SerializeField] private GameObject _goalsRow;
        [Tooltip("One star per goal (1, 2, 3 stars): full when the best result reached it.")]
        [SerializeField] private ThemedImage[] _goalStars = new ThemedImage[3];
        [Tooltip("Coins needed for each goal.")]
        [SerializeField] private TMP_Text[] _goalCoins = new TMP_Text[3];
        [Tooltip("\"Best: coins\" line. Hidden for a level not built yet.")]
        [SerializeField] private GameObject _bestRow;
        [SerializeField] private TMP_Text _coinsText;
        [SerializeField] private TMP_Text _actionText;

        [Header("Placement")]
        [Tooltip("World height (m) above the circle center where the bottom of the card points (just above the " +
                 "walker's head; the card hides the circle's floating number, which its title repeats).")]
        [SerializeField] private float _worldHeight = 2.2f;
        [Tooltip("Size of the card (1 = its layout size in canvas pixels).")]
        [SerializeField, Min(0.1f)] private float _cardScale = 0.85f;
        [Tooltip("Extra screen offset (canvas pixels) above that point.")]
        [SerializeField] private float _screenOffset = 12f;
        [Tooltip("The card never goes closer than this to the screen edges (canvas pixels).")]
        [SerializeField] private float _edgeMargin = 24f;

        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float _showTime = 0.25f;
        [SerializeField, Min(0.01f)] private float _hideTime = 0.12f;

        private RectTransform _area;
        private Camera _camera;
        private LevelNode _node;
        private float _shown;   // 0 hidden .. 1 shown
        private bool _wanted;

        /// <summary>The level the card shows (also while it shrinks away), or null.</summary>
        public LevelNode Node => _node;

        private void Awake()
        {
            _area = (RectTransform)transform;
            if (_card != null)
            {
                _card.localScale = Vector3.zero;
                _card.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            LevelRecords.Changed += Refresh;
        }

        private void OnDisable()
        {
            LevelRecords.Changed -= Refresh;
        }

        /// <summary>Shows the card for <paramref name="node"/> with this action line (pops in again for a new level).</summary>
        public void Show(LevelNode node, string actionLine)
        {
            if (node != _node)
            {
                _node = node;
                _shown = 0f;
                Refresh();
            }
            _wanted = node != null;
            if (_actionText != null && _actionText.text != actionLine)
            {
                _actionText.text = actionLine;
            }
        }

        public void Hide()
        {
            _wanted = false;
        }

        private void Refresh()
        {
            if (_node == null)
            {
                return;
            }
            var level = _node.Definition;
            if (_title != null)
            {
                _title.text = $"{_node.LevelNumber}. {_node.DisplayName}";
            }
            var picture = level != null ? level.Preview : null;
            if (_preview != null)
            {
                _preview.sprite = picture;
                _preview.enabled = picture != null;
            }
            if (_previewPlaceholder != null)
            {
                _previewPlaceholder.SetActive(picture == null);
            }
            if (_goalsRow != null)
            {
                _goalsRow.SetActive(_node.HasLevel);
            }
            if (_bestRow != null)
            {
                _bestRow.SetActive(_node.HasLevel);
            }
            var records = LevelRecords.Current;
            var played = false;
            int stars = 0, coins = 0;
            if (records != null)
            {
                played = records.TryGet(_node.LevelNumber, out stars, out coins);
            }
            for (var i = 0; i < _goalStars.Length; i++)
            {
                if (_goalStars[i] != null)
                {
                    // The star sprites have their own colors: no tint.
                    _goalStars[i].SetLook(i < stars ? UiSprite.StarFull : UiSprite.StarEmpty, UiColor.White);
                }
                if (i < _goalCoins.Length && _goalCoins[i] != null)
                {
                    var needed = level != null ? level.CoinsForStars(i + 1) : 0;
                    _goalCoins[i].text = needed > 0 ? needed.ToString() : "-";
                }
            }
            if (_coinsText != null)
            {
                _coinsText.text = played ? coins.ToString() : "-";
            }
        }

        private void LateUpdate()
        {
            if (_card == null)
            {
                return;
            }
            // Unscaled: the pause menu stops the time on a local map, the card still finishes its animation.
            var deltaTime = Time.unscaledDeltaTime;
            _shown = Mathf.MoveTowards(_shown, _wanted ? 1f : 0f, deltaTime / (_wanted ? _showTime : _hideTime));
            var visible = _shown > 0f && _node != null;
            if (_card.gameObject.activeSelf != visible)
            {
                _card.gameObject.SetActive(visible);
            }
            if (!visible)
            {
                if (!_wanted)
                {
                    _node = null;
                }
                return;
            }
            // Back ease on the way in (small overshoot), straight on the way out.
            var scale = _wanted
                ? 1f + 2.70158f * Mathf.Pow(_shown - 1f, 3f) + 1.70158f * Mathf.Pow(_shown - 1f, 2f)
                : _shown;
            _card.localScale = Vector3.one * (Mathf.Max(0f, scale) * _cardScale);
            Place();
        }

        // Bottom center of the card above the circle, kept inside the screen.
        private void Place()
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
            }
            if (_camera == null)
            {
                return;
            }
            var world = _node.transform.position + Vector3.up * _worldHeight;
            var screen = _camera.WorldToScreenPoint(world);
            if (screen.z <= 0f
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screen, null, out var local))
            {
                return;
            }
            local.y += _screenOffset;
            var size = _card.rect.size * _cardScale;
            var area = _area.rect;
            var minX = area.xMin + _edgeMargin + size.x * 0.5f;
            var maxX = area.xMax - _edgeMargin - size.x * 0.5f;
            var minY = area.yMin + _edgeMargin;
            var maxY = area.yMax - _edgeMargin - size.y;
            local.x = minX <= maxX ? Mathf.Clamp(local.x, minX, maxX) : area.center.x;
            local.y = minY <= maxY ? Mathf.Clamp(local.y, minY, maxY) : minY;
            _card.anchoredPosition = local;
        }
    }
}
