using System.Collections.Generic;
using OverSmithed.Players;
using OverSmithed.Story;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// The screen of the story intro cutscene: black cinema bars, the dialogue box of each shot (text typed letter by
    /// letter), the key hints and the fades. On the host machine its joined players drive the cutscene: Interact shows
    /// the whole text, then goes to the next shot; Pause (Start / Escape) skips everything. Clients only watch.
    /// Local only: it reads <see cref="StoryIntroDirector"/> (the shot number is the only network data).
    /// </summary>
    public class StoryIntroScreen : MonoBehaviour
    {
        [SerializeField] private StoryIntroDirector _director;

        [Header("Cinema bars")]
        [SerializeField] private RectTransform _topBar;
        [SerializeField] private RectTransform _bottomBar;
        [SerializeField, Min(0f)] private float _barHeight = 110f;
        [SerializeField, Min(0.01f)] private float _barTime = 0.8f;

        [Header("Dialogue box")]
        [SerializeField] private RectTransform _box;
        [SerializeField] private CanvasGroup _boxGroup;
        [SerializeField] private TMP_Text _speakerText;
        [SerializeField] private TMP_Text _bodyText;
        [Tooltip("Shown once the text is fully typed: \"A: next\".")]
        [SerializeField] private TMP_Text _nextHint;
        [Tooltip("Fades the next hint (and its arrow) in.")]
        [SerializeField] private CanvasGroup _nextHintGroup;
        [Tooltip("Small arrow after the next hint: bobs gently up and down (host only: clients can't go on).")]
        [SerializeField] private RectTransform _nextArrow;
        [SerializeField, Min(0f)] private float _arrowBob = 5f;
        [SerializeField, Min(0.1f)] private float _arrowBobPeriod = 1f;
        [SerializeField, Min(0.01f)] private float _hintFadeTime = 0.3f;
        [SerializeField, Min(1f)] private float _lettersPerSecond = 40f;
        [SerializeField, Min(0.01f)] private float _boxPopTime = 0.25f;

        [Header("Skip + fade")]
        [Tooltip("In the bottom bar: \"Start: skip\" (host) or a note for clients.")]
        [SerializeField] private TMP_Text _skipHint;
        [SerializeField] private CanvasGroup _fade;
        [SerializeField, Min(0.01f)] private float _fadeInTime = 1f;
        [Tooltip("Input is ignored for this long after the scene opens (the press that started the level).")]
        [SerializeField, Min(0f)] private float _inputDelay = 0.6f;
        [Tooltip("Color of the key names in the dialogue box (light box: dark color).")]
        [SerializeField] private Color _boxKeyColor = new(0.55f, 0.27f, 0.08f);
        [Tooltip("Color of the key names in the black bar.")]
        [SerializeField] private Color _barKeyColor = new(1f, 0.83f, 0.3f);

        private const string InteractAction = "Interact";
        private const string PauseAction = "Pause";

        private readonly List<string> _keyNames = new();
        private CutsceneShot _shot;
        private bool _boxShown;
        private float _boxClock;
        private float _typed;
        private int _letterCount;
        private float _barShown;
        private float _fadeTarget = 1f;
        private float _fadeSpeed;
        private float _clock;
        private bool _isHost;
        private string _shownNextKeys;
        private string _shownSkipKeys;
        private bool _hintsReady;
        private float _hintClock;
        private Vector2 _arrowRest;

        private void Awake()
        {
            _fade.alpha = 1f;
            _boxGroup.alpha = 0f;
            _box.gameObject.SetActive(false);
            if (_nextArrow != null)
            {
                _arrowRest = _nextArrow.anchoredPosition;
            }
            SetBars(0f);
        }

        private void OnEnable()
        {
            _director.ShotStarted += HandleShotStarted;
            _director.Ending += HandleEnding;
        }

        private void OnDisable()
        {
            _director.ShotStarted -= HandleShotStarted;
            _director.Ending -= HandleEnding;
        }

        private void Start()
        {
            // The first shot may already be playing (a client that arrives late).
            if (_director.CurrentShot != null)
            {
                HandleShotStarted(_director.CurrentShot);
            }
        }

        private void HandleShotStarted(CutsceneShot shot)
        {
            _shot = shot;
            _boxShown = false;
            _boxGroup.alpha = 0f;
            _box.gameObject.SetActive(false);
            if (shot.FadeFromBlack || _director.ShotIndex == 0)
            {
                _fade.alpha = 1f;
            }
            FadeTo(0f, _fadeInTime);
        }

        private void HandleEnding()
        {
            _shot = null;
            FadeTo(1f, _director.EndFadeTime);
        }

        private void FadeTo(float alpha, float time)
        {
            _fadeTarget = alpha;
            _fadeSpeed = 1f / Mathf.Max(0.01f, time);
        }

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            _clock += deltaTime;
            _isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

            _fade.alpha = Mathf.MoveTowards(_fade.alpha, _fadeTarget, _fadeSpeed * deltaTime);
            var barsWanted = _director.ShotIndex >= 0 ? 1f : 0f;
            _barShown = Mathf.MoveTowards(_barShown, barsWanted, deltaTime / _barTime);
            SetBars(_barShown);

            UpdateBox(deltaTime);
            UpdateHints();
            if (_isHost && _clock >= _inputDelay && !_director.IsEnding)
            {
                ReadHostInput();
            }
        }

        // ---------- Dialogue box ----------

        private void UpdateBox(float deltaTime)
        {
            if (_shot == null)
            {
                if (_boxShown)
                {
                    _boxGroup.alpha = Mathf.MoveTowards(_boxGroup.alpha, 0f, deltaTime / _boxPopTime);
                }
                return;
            }
            if (!_boxShown && _shot.Elapsed >= _shot.TextDelay)
            {
                ShowBox();
            }
            if (!_boxShown)
            {
                return;
            }
            _boxClock += deltaTime;
            var pop = Mathf.Clamp01(_boxClock / _boxPopTime);
            _boxGroup.alpha = pop;
            // Small back ease: grows a little past 1 then settles.
            var scale = 1f + 0.06f * Mathf.Sin(pop * Mathf.PI) - 0.08f * (1f - pop);
            _box.localScale = Vector3.one * scale;

            if (_typed < _letterCount)
            {
                _typed = Mathf.Min(_letterCount, _typed + _lettersPerSecond * deltaTime);
                _bodyText.maxVisibleCharacters = Mathf.FloorToInt(_typed);
            }
            UpdateNextHint(deltaTime);
        }

        // Once the text is typed: the hint fades in and stays still; only the small arrow bobs (smooth, a few pixels).
        private void UpdateNextHint(float deltaTime)
        {
            _hintClock = IsTyped ? _hintClock + deltaTime : 0f;
            SetHintAlpha(Mathf.Clamp01(_hintClock / _hintFadeTime));
            if (_nextArrow == null)
            {
                return;
            }
            _nextArrow.gameObject.SetActive(_isHost);
            var wave = 0.5f - 0.5f * Mathf.Cos(_hintClock * 2f * Mathf.PI / _arrowBobPeriod);
            _nextArrow.anchoredPosition = _arrowRest + Vector2.down * (_arrowBob * wave);
        }

        private void SetHintAlpha(float alpha)
        {
            if (_nextHintGroup != null)
            {
                _nextHintGroup.alpha = alpha;
            }
            else
            {
                _nextHint.alpha = alpha;
            }
        }

        private bool IsTyped => _boxShown && _typed >= _letterCount;

        private void ShowBox()
        {
            _boxShown = true;
            _boxClock = 0f;
            // Active first: TMP does not update the text info of an inactive object (the count would be the old text's).
            _box.gameObject.SetActive(true);
            var speaker = _shot.SpeakerName;
            _speakerText.gameObject.SetActive(!string.IsNullOrEmpty(speaker));
            _speakerText.text = speaker;
            _bodyText.text = _shot.Text;
            _bodyText.ForceMeshUpdate(true, true);
            _letterCount = _bodyText.textInfo.characterCount;
            _typed = 0f;
            _bodyText.maxVisibleCharacters = 0;
            _hintClock = 0f;
            SetHintAlpha(0f);
        }

        private void SetBars(float shown)
        {
            var ease = shown * shown * (3f - 2f * shown);
            var height = _barHeight * ease;
            _topBar.sizeDelta = new Vector2(_topBar.sizeDelta.x, height);
            _bottomBar.sizeDelta = new Vector2(_bottomBar.sizeDelta.x, height);
        }

        // ---------- Input (host machine) ----------

        private void ReadHostInput()
        {
            if (_director.CurrentShot == null)
            {
                return;
            }
            if (AnyJoinedPlayerPressed(PauseAction))
            {
                _director.HostSkip();
                return;
            }
            if (!AnyJoinedPlayerPressed(InteractAction))
            {
                return;
            }
            if (!_boxShown)
            {
                // Too early for the text: show it now (it appears, then the next press goes on).
                ShowBox();
            }
            else if (!IsTyped)
            {
                _typed = _letterCount;
                _bodyText.maxVisibleCharacters = _letterCount;
            }
            else
            {
                _director.HostNext();
            }
        }

        private static bool AnyJoinedPlayerPressed(string actionName)
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
                var action = slot.Input.actions.FindAction(actionName);
                pressed |= action != null && action.WasPressedThisFrame();
            }
            return pressed;
        }

        // ---------- Hints ----------

        private void UpdateHints()
        {
            var nextKeys = _isHost ? KeyNames(InteractAction) : null;
            if (!_hintsReady || nextKeys != _shownNextKeys)
            {
                _shownNextKeys = nextKeys;
                _nextHint.text = _isHost ? $"<color=#{ColorUtility.ToHtmlStringRGB(_boxKeyColor)}>{nextKeys}</color>  next" : "The host turns the pages";
            }
            var skipKeys = _isHost ? KeyNames(PauseAction) : null;
            if (!_hintsReady || skipKeys != _shownSkipKeys)
            {
                _shownSkipKeys = skipKeys;
                _skipHint.text = _isHost ? $"<color=#{ColorUtility.ToHtmlStringRGB(_barKeyColor)}>{skipKeys}</color>  skip" : "The host can skip";
            }
            _hintsReady = true;
        }

        // The key of this action for every joined player of this machine, each name once ("A / Space").
        private string KeyNames(string actionName)
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
                    var key = ControlHints.KeyName(slot.Input.actions, "Player/" + actionName, slot.ControlScheme,
                        slot.Device);
                    if (!string.IsNullOrEmpty(key) && !_keyNames.Contains(key))
                    {
                        _keyNames.Add(key);
                    }
                }
            }
            return _keyNames.Count > 0 ? string.Join(" / ", _keyNames) : actionName;
        }
    }
}
