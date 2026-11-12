using System.Collections;
using System.Collections.Generic;
using OverSmithed.Crafting;
using UnityEngine;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>
    /// One order in the HUD, in two parts: a main panel (patience bar + the item to forge) and, hanging under it,
    /// one small panel ("tab") per group of materials, with the stations to use at the bottom of the tab.
    /// Only display: <see cref="OrderCardsHud"/> places the cards, the order system (fake for now) sets
    /// <see cref="Patience"/> and removes them.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class OrderCard : MonoBehaviour
    {
        [SerializeField] private UiTheme _theme;
        [SerializeField] private ProgressBar _patienceBar;
        [SerializeField] private IconView _resultIcon;
        [Tooltip("Main panel background (tinted red when the order is lost).")]
        [SerializeField] private Image _background;
        [Tooltip("Row of tabs under the main panel (drawn behind it, so the tabs seem to come out from under it).")]
        [SerializeField] private RectTransform _tabs;
        [Tooltip("Inactive child copied for each group: a panel with a 'Materials' row and, under it, a 'Stations' " +
                 "row (with an inactive 'ArrowTemplate' shown between two stations).")]
        [SerializeField] private RectTransform _tabTemplate;
        [SerializeField] private IconView _iconPrefab;

        [Header("Sizes")]
        [SerializeField] private float _resultIconSize = 64f;
        [SerializeField] private float _materialIconSize = 36f;
        [SerializeField] private float _stationIconSize = 26f;
        [Tooltip("The card is never narrower than this; it gets wider when the tabs need it.")]
        [SerializeField] private float _minWidth = 190f;

        [Header("Exit animations")]
        [SerializeField] private float _exitDuration = 0.35f;
        [SerializeField] private float _exitDistance = 70f;
        [SerializeField] private float _shakeDuration = 0.35f;
        [SerializeField] private float _shakeAmplitude = 9f;
        [SerializeField] private Color _lostTint = new(1f, 0.55f, 0.5f);

        private readonly List<Image> _panels = new();
        private CanvasGroup _canvasGroup;

        /// <summary>0 = no patience left, 1 = full.</summary>
        public float Patience
        {
            get => _patienceBar.Value;
            set => _patienceBar.Value = value;
        }

        public RectTransform Rect => (RectTransform)transform;
        public float Width => Rect.sizeDelta.x;
        public bool IsLeaving { get; private set; }

        // Moved by OrderCardsHud (SmoothDamp state).
        internal float X;
        internal float Velocity;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _tabTemplate.gameObject.SetActive(false);
        }

        public void Setup(OrderCardData data)
        {
            _resultIcon.SetSize(_resultIconSize);
            _resultIcon.Show(data.ResultIcon, data.ResultName);
            _panels.Clear();
            _panels.Add(_background);

            foreach (var group in data.Groups)
            {
                var tab = Instantiate(_tabTemplate, _tabs);
                tab.name = "Tab";
                tab.gameObject.SetActive(true);
                _panels.Add(tab.GetComponent<Image>());
                var materials = (RectTransform)tab.Find("Materials");
                var stations = (RectTransform)tab.Find("Stations");

                foreach (var material in group.Materials)
                {
                    if (material == null)
                    {
                        continue;
                    }
                    var icon = Instantiate(_iconPrefab, materials);
                    icon.SetSize(_materialIconSize);
                    icon.Show(material.Icon, material.DisplayName);
                }

                // Stations are used in order, left to right: an arrow goes between two of them (furnace → anvil).
                var arrowTemplate = stations.Find("ArrowTemplate");
                var stationCount = 0;
                foreach (var station in group.Stations)
                {
                    if (station == StationKind.None)
                    {
                        continue;
                    }
                    if (stationCount++ > 0 && arrowTemplate != null)
                    {
                        var arrow = Instantiate(arrowTemplate, stations);
                        arrow.name = "Arrow";
                        arrow.gameObject.SetActive(true);
                    }
                    var icon = Instantiate(_iconPrefab, stations);
                    icon.SetSize(_stationIconSize);
                    icon.Show(_theme != null ? _theme.GetSprite(StationSprite(station)) : null,
                        StationLetter(station), StationColor(station));
                }
                // Keep the row's height even without station, so every tab has the same layout.
                var stationsLayout = stations.GetComponent<LayoutElement>();
                stationsLayout.minHeight = _stationIconSize;
                // An empty layout group reports a max width of 0 (uGUI 2), which would squash the whole tab
                // to 0 wide. A big max width on the LayoutElement (higher priority) overrides it.
                stationsLayout.maxWidth = 10000f;
            }

            // The card is as wide as its tabs (at least _minWidth). The tabs row's padding keeps them inside.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tabs);
            var width = Mathf.Max(_minWidth, LayoutUtility.GetPreferredWidth(_tabs));
            Rect.sizeDelta = new Vector2(width, Rect.sizeDelta.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
        }

        /// <summary>Plays the exit animation (delivered: pops and flies up; lost: shakes red and falls), then destroys the card.</summary>
        public void Leave(bool delivered)
        {
            if (IsLeaving)
            {
                return;
            }
            IsLeaving = true;
            // Drawn above the cards that slide into its place.
            transform.SetAsLastSibling();
            StartCoroutine(delivered ? DeliveredRoutine() : LostRoutine());
        }

        // The pivot is the top-left corner (for placing cards): move while scaling so the card grows from its center.
        private void SetScaleAroundCenter(Vector2 basePosition, float scale)
        {
            Rect.localScale = Vector3.one * scale;
            var size = Rect.rect.size;
            Rect.anchoredPosition = basePosition + new Vector2(-size.x, size.y) * ((scale - 1f) * 0.5f);
        }

        private IEnumerator DeliveredRoutine()
        {
            var start = Rect.anchoredPosition;
            // Quick pop...
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.12f)
            {
                SetScaleAroundCenter(start, Mathf.Lerp(1f, 1.12f, t));
                yield return null;
            }
            // ...then flies up and fades out.
            for (var t = 0f; t < 1f; t += Time.deltaTime / _exitDuration)
            {
                var eased = t * t;
                SetScaleAroundCenter(start + Vector2.up * (_exitDistance * eased), Mathf.Lerp(1.12f, 0.8f, eased));
                _canvasGroup.alpha = 1f - eased;
                yield return null;
            }
            Destroy(gameObject);
        }

        private IEnumerator LostRoutine()
        {
            var start = Rect.anchoredPosition;
            var baseColors = new Color[_panels.Count];
            for (var i = 0; i < _panels.Count; i++)
            {
                baseColors[i] = _panels[i].color;
            }
            // Turns red and shakes...
            for (var t = 0f; t < 1f; t += Time.deltaTime / _shakeDuration)
            {
                var redness = Mathf.Min(1f, t * 3f);
                for (var i = 0; i < _panels.Count; i++)
                {
                    _panels[i].color = Color.Lerp(baseColors[i], baseColors[i] * _lostTint, redness);
                }
                var shake = Mathf.Sin(t * Mathf.PI * 8f) * _shakeAmplitude * (1f - t);
                Rect.anchoredPosition = start + Vector2.right * shake;
                yield return null;
            }
            // ...then falls and fades out.
            for (var t = 0f; t < 1f; t += Time.deltaTime / _exitDuration)
            {
                var eased = t * t;
                Rect.anchoredPosition = start + Vector2.down * (_exitDistance * eased);
                _canvasGroup.alpha = 1f - eased;
                yield return null;
            }
            Destroy(gameObject);
        }

        private static UiSprite StationSprite(StationKind station)
        {
            return station switch
            {
                StationKind.Furnace => UiSprite.StationFurnace,
                StationKind.Anvil => UiSprite.StationAnvil,
                StationKind.Workbench => UiSprite.StationWorkbench,
                StationKind.AssemblyTable => UiSprite.StationAssemblyTable,
                _ => UiSprite.None,
            };
        }

        // Placeholders until the station sprites are in the theme.
        private static string StationLetter(StationKind station)
        {
            return station switch
            {
                StationKind.Furnace => "F",
                StationKind.Anvil => "A",
                StationKind.Workbench => "W",
                StationKind.AssemblyTable => "T",
                _ => "?",
            };
        }

        private static Color StationColor(StationKind station)
        {
            return station switch
            {
                StationKind.Furnace => new Color(0.92f, 0.42f, 0.12f),
                StationKind.Anvil => new Color(0.40f, 0.43f, 0.50f),
                StationKind.Workbench => new Color(0.58f, 0.38f, 0.20f),
                StationKind.AssemblyTable => new Color(0.22f, 0.47f, 0.75f),
                _ => Color.gray,
            };
        }
    }
}
