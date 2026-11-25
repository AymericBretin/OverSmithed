using System;
using OverSmithed.Core;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace OverSmithed.UI
{
    /// <summary>
    /// Loading screen (prefab UI/Prefabs/Screens/LoadingScreen, under App in the Bootstrap scene so it lives for the
    /// whole session). Covers every machine while a networked scene loads (Lobby, World map, levels) and while a
    /// client joining a game loads the host's scene; goes away once every player has loaded it.
    /// Cover first, then load: the host asks every machine to cover (named message) and <see cref="SceneFlow"/>
    /// starts the load only once the cover animation is done, so nobody sees the next scene pop in.
    /// Look: a forge shutter (curtain with a wooden beam) drops from the top with a small bounce and lifts away at the
    /// end; inside, a scrolling wallpaper of forge pictograms, a little forge (a hammer strikes the item on an anvil,
    /// sparks, the item turns into the next one every two strikes), embers rising, the destination name and a random
    /// tip. Debug: editor menu OverSmithed/Loading Screen Preview keeps it on screen.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class LoadingScreen : MonoBehaviour
    {
        [Serializable]
        private struct SceneTitle
        {
            public string Scene;
            public string Title;
        }

        private enum Phase
        {
            Hidden,
            Entering,
            Covered,
            Leaving
        }

        private const string CoverMessage = "OverSmithed.LoadingScreen.Cover";

        [Header("Texts")]
        [SerializeField, TextArea] private string[] _tips;
        [SerializeField] private TMP_Text _tipText;
        [SerializeField] private TMP_Text _destinationText;
        [SerializeField] private TMP_Text _loadingText;
        [Tooltip("Name shown for each scene. A scene not in the list shows no name.")]
        [SerializeField] private SceneTitle[] _sceneTitles =
        {
            new() { Scene = SceneNames.Lobby, Title = "Lobby" },
            new() { Scene = SceneNames.WorldMap, Title = "World Map" },
            new() { Scene = SceneNames.Game, Title = "The Village" },
            new() { Scene = "StoryIntro", Title = "The Old Forge" },
        };
        [SerializeField] private string _joiningTitle = "Joining the game";

        [Header("Shutter (seconds, real time)")]
        [Tooltip("Everything that slides: background, beam and content. Its rest position is 0 (covering).")]
        [SerializeField] private RectTransform _curtain;
        [Tooltip("Extra height below the screen (the beam), so the curtain is fully out when lifted.")]
        [SerializeField, Min(0f)] private float _beamHeight = 60f;
        [SerializeField, Min(0.05f)] private float _dropTime = 0.38f;
        [SerializeField, Min(0f)] private float _bounceHeight = 22f;
        [SerializeField, Min(0.01f)] private float _bounceTime = 0.22f;
        [SerializeField, Min(0.05f)] private float _liftTime = 0.42f;
        [Tooltip("Stays at least this long once covering, so it never just flashes.")]
        [SerializeField, Min(0f)] private float _minimumTime = 0.7f;
        [Tooltip("Pop in one after the other once the shutter starts dropping (logo, forge, texts, tip card).")]
        [SerializeField] private RectTransform[] _popIns;
        [SerializeField, Min(0f)] private float _popDelay = 0.08f;
        [SerializeField, Min(0f)] private float _popStep = 0.05f;
        [SerializeField, Min(0.05f)] private float _popTime = 0.3f;
        [Tooltip("Safety: a cover asked by the host with no load following is lifted after this long.")]
        [SerializeField, Min(1f)] private float _coverTimeout = 6f;

        [Header("Forge")]
        [Tooltip("Hammer image; its pivot is the end of the handle.")]
        [SerializeField] private RectTransform _hammer;
        [SerializeField] private float _hammerRaisedAngle = -20f;
        [SerializeField] private float _hammerHitAngle = 62f;
        [SerializeField] private Image _item;
        [Tooltip("The item turns into the next one every two strikes (ore, hot ingot, blade, sword...).")]
        [SerializeField] private Sprite[] _itemSteps;
        [SerializeField, Min(0.2f)] private float _strikePeriod = 0.62f;
        [Tooltip("Where the sparks fly from (top of the item).")]
        [SerializeField] private RectTransform _sparkOrigin;
        [SerializeField] private RectTransform[] _sparks;
        [SerializeField] private Image _glow;
        [SerializeField, Range(0f, 1f)] private float _glowAlpha = 0.22f;
        [SerializeField] private RectTransform _logo;

        [Header("Background pattern")]
        [Tooltip("Tiled silhouettes of forge pictograms, scrolling slowly (RawImage, texture in Repeat mode).")]
        [SerializeField] private RawImage _pattern;
        [Tooltip("Size of one pattern tile on screen (canvas pixels).")]
        [SerializeField, Min(16f)] private float _patternTileSize = 640f;
        [Tooltip("Scroll speed in canvas pixels per second (diagonal: right and down).")]
        [SerializeField] private Vector2 _patternSpeed = new(28f, -28f);

        [Header("Embers")]
        [SerializeField] private RectTransform _embersArea;
        [SerializeField] private RectTransform[] _embers;

        private CanvasGroup _group;
        private NetworkManager _networkManager;
        private NetworkSceneManager _sceneManager;
        private bool _messagesRegistered;

        private Phase _phase = Phase.Hidden;
        private float _phaseTime;
        private float _cover;          // 0 = lifted out above the screen, 1 = covering
        private float _fromCover;
        private bool _wanted;
        private float _coveredAt;
        private float _waitingForLoadSince = -1f;
        private float _clock;
        private float _popStart;
        private int _lastTip = -1;
        private Vector2 _logoRest;

        // Forge
        private int _strikes;
        private int _itemStep;
        private float _lastPhase;
        private float _hitFlash;
        private float _itemPop;
        private Vector2[] _sparkVelocity;
        private float[] _sparkLife;
        private Vector2[] _emberSpeed;
        private float[] _emberPhase;

        /// <summary>True while the loading screen is on screen, even partly (moving in or out).</summary>
        public static bool IsVisible { get; private set; }

        /// <summary>The loading screen of the session (Bootstrap), null before it exists.</summary>
        public static LoadingScreen Instance { get; private set; }

        /// <summary>Debug: true while the screen is held open by <see cref="ShowPreview"/>.</summary>
        public bool IsPreviewing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsVisible = false;
            Instance = null;
        }

        private void Awake()
        {
            Instance = this;
            _group = GetComponent<CanvasGroup>();
            _group.interactable = false;
            if (_logo != null)
            {
                _logoRest = _logo.anchoredPosition;
            }
            _sparkVelocity = new Vector2[_sparks.Length];
            _sparkLife = new float[_sparks.Length];
            foreach (var spark in _sparks)
            {
                spark.gameObject.SetActive(false);
            }
            _emberSpeed = new Vector2[_embers.Length];
            _emberPhase = new float[_embers.Length];
            for (var i = 0; i < _embers.Length; i++)
            {
                SpawnEmber(i, true);
            }
            ApplyCurtain(0f, 0f);
        }

        private void Start()
        {
            SceneFlow.NetworkSceneLoadStarted += HandleNetworkSceneLoadStarted;
            SceneFlow.NetworkSceneLoadFailed += HandleNetworkSceneLoadFailed;
            SceneFlow.CoverBeforeLoad = CoverEveryone;
            _networkManager = NetworkManager.Singleton;
            if (_networkManager != null)
            {
                _networkManager.OnClientStarted += HandleClientStarted;
                _networkManager.OnClientStopped += HandleClientStopped;
            }
        }

        private void OnDestroy()
        {
            SceneFlow.NetworkSceneLoadStarted -= HandleNetworkSceneLoadStarted;
            SceneFlow.NetworkSceneLoadFailed -= HandleNetworkSceneLoadFailed;
            if (SceneFlow.CoverBeforeLoad == (Func<string, float>)CoverEveryone)
            {
                SceneFlow.CoverBeforeLoad = null;
            }
            UnsubscribeNetwork();
            if (_networkManager != null)
            {
                _networkManager.OnClientStarted -= HandleClientStarted;
                _networkManager.OnClientStopped -= HandleClientStopped;
            }
            IsVisible = false;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ---------- Debug preview (editor menu OverSmithed/Loading Screen Preview) ----------

        /// <summary>Debug: shows the screen and keeps it until <see cref="HidePreview"/> (or a real load ends).</summary>
        public void ShowPreview(string sceneName)
        {
            IsPreviewing = true;
            Show(sceneName);
        }

        /// <summary>Debug: lets the previewed screen go away.</summary>
        public void HidePreview()
        {
            Hide();
        }

        // ---------- Network ----------

        // Host and clients: the network scene manager and the messaging exist from here on.
        private void HandleClientStarted()
        {
            UnsubscribeNetwork();
            _sceneManager = _networkManager.SceneManager;
            if (_sceneManager != null)
            {
                _sceneManager.OnSceneEvent += HandleSceneEvent;
            }
            if (_networkManager.CustomMessagingManager != null)
            {
                _networkManager.CustomMessagingManager.RegisterNamedMessageHandler(CoverMessage, HandleCoverMessage);
                _messagesRegistered = true;
            }
        }

        // Left the game or lost the connection: never stay stuck on the loading screen.
        private void HandleClientStopped(bool wasHost)
        {
            UnsubscribeNetwork();
            Hide();
        }

        private void UnsubscribeNetwork()
        {
            if (_sceneManager != null)
            {
                _sceneManager.OnSceneEvent -= HandleSceneEvent;
                _sceneManager = null;
            }
            if (_messagesRegistered)
            {
                if (_networkManager != null && _networkManager.CustomMessagingManager != null)
                {
                    _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(CoverMessage);
                }
                _messagesRegistered = false;
            }
        }

        // Host (SceneFlow asks before a load): cover every machine, then tell SceneFlow how long to wait.
        private float CoverEveryone(string sceneName)
        {
            var manager = _networkManager;
            if (manager != null && manager.IsServer && manager.CustomMessagingManager != null)
            {
                var writer = new FastBufferWriter(128, Allocator.Temp);
                using (writer)
                {
                    writer.WriteValueSafe(new FixedString64Bytes(sceneName));
                    manager.CustomMessagingManager.SendNamedMessageToAll(CoverMessage, writer);
                }
            }
            Show(sceneName);
            _waitingForLoadSince = Time.unscaledTime;
            return TimeUntilSettled();
        }

        // Client: the host is about to load a scene.
        private void HandleCoverMessage(ulong senderId, FastBufferReader reader)
        {
            if (_networkManager == null || _networkManager.IsServer)
            {
                return;
            }
            reader.ReadValueSafe(out FixedString64Bytes sceneName);
            Show(sceneName.ToString());
            _waitingForLoadSince = Time.unscaledTime;
        }

        // Host: SceneFlow started the load.
        private void HandleNetworkSceneLoadStarted(string sceneName)
        {
            Show(sceneName);
            _waitingForLoadSince = -1f;
        }

        private void HandleNetworkSceneLoadFailed(string sceneName)
        {
            Hide();
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            var isMe = sceneEvent.ClientId == _networkManager.LocalClientId;
            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.Load when isMe:
                    Show(sceneEvent.SceneName);
                    _waitingForLoadSince = -1f;
                    break;
                case SceneEventType.Synchronize when isMe:
                    Show(null);
                    _waitingForLoadSince = -1f;
                    break;
                // Every player has loaded the scene.
                case SceneEventType.LoadEventCompleted:
                    Hide();
                    break;
                // This client finished joining.
                case SceneEventType.SynchronizeComplete when isMe:
                    Hide();
                    break;
            }
        }

        // ---------- Show / hide ----------

        private void Show(string sceneName)
        {
            if (sceneName != null || _phase == Phase.Hidden)
            {
                SetDestination(sceneName);
            }
            _wanted = true;
            switch (_phase)
            {
                case Phase.Hidden:
                    PickTip();
                    ResetForge();
                    StartPhase(Phase.Entering);
                    break;
                case Phase.Leaving:
                    // Drop back down from where it is.
                    StartPhase(Phase.Entering);
                    break;
            }
        }

        private void Hide()
        {
            _wanted = false;
            IsPreviewing = false;
            _waitingForLoadSince = -1f;
        }

        // Time until the whole entrance is over (drop, bounce and pop-ins). The scene loads only after it: switching
        // scenes freezes a few frames, which would make the entrance stutter.
        private float TimeUntilSettled()
        {
            if (_phase != Phase.Entering)
            {
                return 0f;
            }
            var shutter = _dropTime * (1f - _fromCover) + _bounceTime - _phaseTime;
            var pops = PopEnd - (Time.unscaledTime - _popStart);
            return Mathf.Max(0f, Mathf.Max(shutter, pops));
        }

        private float PopEnd => _popDelay + Mathf.Max(0, _popIns.Length - 1) * _popStep + _popTime;

        private void StartPhase(Phase phase)
        {
            _phase = phase;
            _phaseTime = 0f;
            _fromCover = _cover;
            if (phase == Phase.Entering && _fromCover <= 0f)
            {
                _popStart = Time.unscaledTime;
                foreach (var pop in _popIns)
                {
                    pop.localScale = Vector3.zero;
                }
            }
        }

        // ---------- Every frame ----------

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            // Safety: a cover with no load behind it (message lost, load refused) does not stay forever.
            if (_waitingForLoadSince >= 0f && Time.unscaledTime - _waitingForLoadSince > _coverTimeout)
            {
                Hide();
            }
            UpdateShutter(deltaTime);
            if (_phase == Phase.Hidden)
            {
                return;
            }
            _clock += deltaTime;
            AnimatePopIns();
            AnimateForge(deltaTime);
            AnimatePattern();
            AnimateEmbers(deltaTime);
            AnimateTexts();
        }

        private void UpdateShutter(float deltaTime)
        {
            _phaseTime += deltaTime;
            var bounce = 0f;
            switch (_phase)
            {
                case Phase.Entering:
                {
                    // Drop: fast then soft landing, then a small bounce (the beam hits the floor).
                    var dropTime = _dropTime * (1f - _fromCover);
                    var t = dropTime > 0f ? Mathf.Clamp01(_phaseTime / dropTime) : 1f;
                    var eased = 1f - (1f - t) * (1f - t) * (1f - t);
                    _cover = Mathf.Lerp(_fromCover, 1f, eased);
                    if (t >= 1f)
                    {
                        var u = (_phaseTime - dropTime) / _bounceTime;
                        bounce = u < 1f ? Mathf.Sin(u * Mathf.PI) * _bounceHeight * (1f - u) : 0f;
                        if (u >= 1f)
                        {
                            _phase = Phase.Covered;
                            _coveredAt = Time.unscaledTime;
                        }
                    }
                    break;
                }
                case Phase.Covered:
                    _cover = 1f;
                    if (!_wanted && Time.unscaledTime - _coveredAt >= _minimumTime)
                    {
                        StartPhase(Phase.Leaving);
                    }
                    break;
                case Phase.Leaving:
                {
                    // Lift: a small dip first (anticipation), then up and away.
                    var t = Mathf.Clamp01(_phaseTime / _liftTime);
                    var eased = t * t * t;
                    _cover = Mathf.Lerp(_fromCover, 0f, eased);
                    bounce = Mathf.Sin(Mathf.Clamp01(t / 0.25f) * Mathf.PI) * _bounceHeight * 0.6f;
                    if (t >= 1f)
                    {
                        _phase = Phase.Hidden;
                        _cover = 0f;
                        bounce = 0f;
                    }
                    break;
                }
            }
            ApplyCurtain(_cover, bounce);
        }

        // Cover 1 = at rest on the screen, 0 = lifted above it (beam included). Bounce pushes it down a little.
        private void ApplyCurtain(float cover, float bounce)
        {
            if (_curtain != null)
            {
                var height = ((RectTransform)transform).rect.height + _beamHeight;
                _curtain.anchoredPosition = new Vector2(0f, (1f - cover) * height - bounce);
            }
            var visible = _phase != Phase.Hidden;
            _group.alpha = visible ? 1f : 0f;
            // Blocks the mouse on the scene behind while it is there.
            _group.blocksRaycasts = visible;
            IsVisible = visible;
        }

        // Logo, forge, texts and tip card pop in one after the other (back ease), timed from the start of the drop
        // whatever the shutter does meanwhile (the last ones end after it has landed).
        private void AnimatePopIns()
        {
            var since = Time.unscaledTime - _popStart;
            if (since > PopEnd + 0.1f)
            {
                return;
            }
            for (var i = 0; i < _popIns.Length; i++)
            {
                var t = Mathf.Clamp01((since - _popDelay - i * _popStep) / _popTime);
                const float overshoot = 1.9f;
                var scale = t <= 0f ? 0f : 1f + (overshoot + 1f) * Mathf.Pow(t - 1f, 3f) + overshoot * Mathf.Pow(t - 1f, 2f);
                _popIns[i].localScale = Vector3.one * scale;
            }
        }

        // ---------- Texts ----------

        private void SetDestination(string sceneName)
        {
            if (_destinationText == null)
            {
                return;
            }
            string title = null;
            if (string.IsNullOrEmpty(sceneName))
            {
                title = _joiningTitle;
            }
            else
            {
                foreach (var entry in _sceneTitles)
                {
                    if (entry.Scene == sceneName)
                    {
                        title = entry.Title;
                        break;
                    }
                }
            }
            _destinationText.gameObject.SetActive(!string.IsNullOrEmpty(title));
            _destinationText.text = title;
        }

        private void PickTip()
        {
            if (_tipText == null || _tips == null || _tips.Length == 0)
            {
                return;
            }
            var index = Random.Range(0, _tips.Length);
            if (_tips.Length > 1 && index == _lastTip)
            {
                index = (index + 1) % _tips.Length;
            }
            _lastTip = index;
            _tipText.text = _tips[index];
        }

        // "Loading" + 0..3 dots; the hidden dots keep their room so the word never moves.
        private void AnimateTexts()
        {
            if (_loadingText != null)
            {
                var dots = Mathf.FloorToInt(_clock * 2.5f) % 4;
                _loadingText.text = "Loading" + new string('.', dots) + "<alpha=#00>" + new string('.', 3 - dots);
            }
            if (_logo != null)
            {
                _logo.anchoredPosition = _logoRest + Vector2.up * (Mathf.Sin(_clock * 1.6f) * 6f);
            }
        }

        // ---------- Forge ----------

        private void ResetForge()
        {
            _clock = 0f;
            _strikes = 0;
            _lastPhase = 0f;
            _hitFlash = 0f;
            _itemPop = 0f;
            _itemStep = _itemSteps != null && _itemSteps.Length > 0 ? Random.Range(0, _itemSteps.Length) : 0;
            ShowItemStep();
        }

        private void AnimateForge(float deltaTime)
        {
            // One strike per period: slow raise (0..0.6), fast swing (0.6..0.75), hit, small rebound, rest.
            var phase = _clock / _strikePeriod % 1f;
            if (_lastPhase < 0.75f && phase >= 0.75f)
            {
                Strike();
            }
            _lastPhase = phase;

            float angle;
            if (phase < 0.6f)
            {
                var t = phase / 0.6f;
                angle = Mathf.Lerp(_hammerHitAngle, _hammerRaisedAngle, 1f - (1f - t) * (1f - t));
            }
            else if (phase < 0.75f)
            {
                var t = (phase - 0.6f) / 0.15f;
                angle = Mathf.Lerp(_hammerRaisedAngle, _hammerHitAngle, t * t);
            }
            else
            {
                var t = (phase - 0.75f) / 0.25f;
                angle = _hammerHitAngle - Mathf.Sin(t * Mathf.PI) * 9f;
            }
            if (_hammer != null)
            {
                _hammer.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            // Item squash on the hit, pop when it changes.
            _hitFlash = Mathf.Max(0f, _hitFlash - deltaTime * 5f);
            _itemPop = Mathf.Max(0f, _itemPop - deltaTime * 4f);
            if (_item != null)
            {
                var squash = _hitFlash * 0.16f;
                var pop = Mathf.Sin(_itemPop * Mathf.PI) * 0.18f;
                _item.rectTransform.localScale = new Vector3(1f + squash + pop, 1f - squash + pop, 1f);
            }
            if (_glow != null)
            {
                var pulse = 0.85f + 0.15f * Mathf.Sin(_clock * 2.2f);
                var color = _glow.color;
                color.a = _glowAlpha * pulse + _hitFlash * 0.16f;
                _glow.color = color;
            }
            AnimateSparks(deltaTime);
        }

        private void Strike()
        {
            _strikes++;
            _hitFlash = 1f;
            for (var i = 0; i < _sparks.Length; i++)
            {
                var spark = _sparks[i];
                spark.gameObject.SetActive(true);
                spark.anchoredPosition = _sparkOrigin != null ? _sparkOrigin.anchoredPosition : Vector2.zero;
                var angle = Random.Range(20f, 160f) * Mathf.Deg2Rad;
                _sparkVelocity[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(260f, 520f);
                _sparkLife[i] = Random.Range(0.3f, 0.5f);
                spark.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 90f));
            }
            // Every two strikes the item becomes the next one.
            if (_strikes % 2 == 0 && _itemSteps != null && _itemSteps.Length > 1)
            {
                _itemStep = (_itemStep + 1) % _itemSteps.Length;
                ShowItemStep();
                _itemPop = 1f;
            }
        }

        private void ShowItemStep()
        {
            if (_item != null && _itemSteps != null && _itemSteps.Length > 0)
            {
                _item.sprite = _itemSteps[_itemStep];
            }
        }

        private void AnimateSparks(float deltaTime)
        {
            for (var i = 0; i < _sparks.Length; i++)
            {
                var spark = _sparks[i];
                if (!spark.gameObject.activeSelf)
                {
                    continue;
                }
                _sparkLife[i] -= deltaTime;
                if (_sparkLife[i] <= 0f)
                {
                    spark.gameObject.SetActive(false);
                    continue;
                }
                _sparkVelocity[i] += Vector2.down * (1400f * deltaTime);
                spark.anchoredPosition += _sparkVelocity[i] * deltaTime;
                spark.localScale = Vector3.one * Mathf.Clamp01(_sparkLife[i] / 0.25f);
            }
        }

        // ---------- Background pattern ----------

        // The tile repeats (texture wrap mode Repeat): only the UV offset moves, so it never runs out.
        private void AnimatePattern()
        {
            if (_pattern == null)
            {
                return;
            }
            var size = _pattern.rectTransform.rect.size / _patternTileSize;
            var offset = -_patternSpeed * (Time.unscaledTime / _patternTileSize);
            offset.x -= Mathf.Floor(offset.x);
            offset.y -= Mathf.Floor(offset.y);
            _pattern.uvRect = new Rect(offset, size);
        }

        // ---------- Embers ----------

        private void AnimateEmbers(float deltaTime)
        {
            if (_embersArea == null)
            {
                return;
            }
            var height = _embersArea.rect.height;
            for (var i = 0; i < _embers.Length; i++)
            {
                var ember = _embers[i];
                var position = ember.anchoredPosition;
                position.y += _emberSpeed[i].y * deltaTime;
                position.x += Mathf.Sin(_clock * 1.3f + _emberPhase[i]) * _emberSpeed[i].x * deltaTime;
                ember.anchoredPosition = position;
                // Fades out toward the top.
                var graphic = ember.GetComponent<Graphic>();
                var color = graphic.color;
                color.a = Mathf.Clamp01(1.2f - position.y / height) * 0.8f;
                graphic.color = color;
                if (position.y > height)
                {
                    SpawnEmber(i, false);
                }
            }
        }

        private void SpawnEmber(int index, bool anywhere)
        {
            if (_embersArea == null)
            {
                return;
            }
            var rect = _embersArea.rect;
            var ember = _embers[index];
            ember.anchoredPosition = new Vector2(Random.Range(0f, rect.width), anywhere ? Random.Range(0f, rect.height) : -20f);
            ember.localScale = Vector3.one * Random.Range(0.6f, 1.3f);
            ember.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 90f));
            _emberSpeed[index] = new Vector2(Random.Range(10f, 30f), Random.Range(35f, 80f));
            _emberPhase[index] = Random.Range(0f, 6.3f);
        }
    }
}
