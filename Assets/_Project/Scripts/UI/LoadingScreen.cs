using System.Collections;
using OverSmithed.Core;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// Loading screen (prefab UI/Prefabs/Screens/LoadingScreen, under App in the Bootstrap scene so it lives for the
    /// whole session). Shown on every machine while a networked scene loads (Lobby, World map, levels) and while a
    /// client joining a game loads the host's scene. Shows a random tip and goes away once every player has finished
    /// loading the scene.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class LoadingScreen : MonoBehaviour
    {
        [Tooltip("One of these is shown at random on each loading.")]
        [SerializeField, TextArea] private string[] _tips;
        [SerializeField] private TMP_Text _tipText;

        [Tooltip("The screen stays at least this long (seconds), so it does not just flash.")]
        [SerializeField] private float _minimumDuration = 1f;
        [Tooltip("Fade in / fade out duration (seconds).")]
        [SerializeField] private float _fadeDuration = 0.25f;

        private CanvasGroup _group;
        private NetworkManager _networkManager;
        private NetworkSceneManager _sceneManager;
        private Coroutine _fade;
        private float _shownAt;
        private bool _shown;
        private int _lastTip = -1;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            ApplyAlpha(0f);
        }

        private void Start()
        {
            SceneFlow.NetworkSceneLoadStarted += HandleNetworkSceneLoadStarted;
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
            UnsubscribeSceneEvents();
            if (_networkManager != null)
            {
                _networkManager.OnClientStarted -= HandleClientStarted;
                _networkManager.OnClientStopped -= HandleClientStopped;
            }
        }

        // Called on the host and on clients: the network scene manager exists from here on.
        private void HandleClientStarted()
        {
            UnsubscribeSceneEvents();
            _sceneManager = _networkManager.SceneManager;
            if (_sceneManager != null)
            {
                _sceneManager.OnSceneEvent += HandleSceneEvent;
            }
        }

        // Left the game (or lost the connection): never stay stuck on the loading screen.
        private void HandleClientStopped(bool wasHost)
        {
            UnsubscribeSceneEvents();
            Hide();
        }

        private void UnsubscribeSceneEvents()
        {
            if (_sceneManager != null)
            {
                _sceneManager.OnSceneEvent -= HandleSceneEvent;
                _sceneManager = null;
            }
        }

        // Host: SceneFlow is about to load a scene for everyone.
        private void HandleNetworkSceneLoadStarted(string sceneName)
        {
            Show();
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            switch (sceneEvent.SceneEventType)
            {
                // This machine starts loading a scene (or, when joining, the host's current scene).
                case SceneEventType.Load:
                case SceneEventType.Synchronize:
                    if (sceneEvent.ClientId == _networkManager.LocalClientId)
                    {
                        Show();
                    }
                    break;
                // Every player has loaded the scene.
                case SceneEventType.LoadEventCompleted:
                    Hide();
                    break;
                // This client finished joining.
                case SceneEventType.SynchronizeComplete:
                    if (sceneEvent.ClientId == _networkManager.LocalClientId)
                    {
                        Hide();
                    }
                    break;
            }
        }

        private void Show()
        {
            if (_shown)
            {
                return;
            }
            _shown = true;
            _shownAt = Time.unscaledTime;
            PickTip();
            StartFade(1f, 0f);
        }

        private void Hide()
        {
            if (!_shown)
            {
                return;
            }
            _shown = false;
            // Keep it on screen for the minimum duration, then fade out.
            var wait = Mathf.Max(0f, _minimumDuration - (Time.unscaledTime - _shownAt));
            StartFade(0f, wait);
        }

        private void PickTip()
        {
            if (_tipText == null || _tips == null || _tips.Length == 0)
            {
                return;
            }
            var index = Random.Range(0, _tips.Length);
            // Avoid showing the same tip twice in a row.
            if (_tips.Length > 1 && index == _lastTip)
            {
                index = (index + 1) % _tips.Length;
            }
            _lastTip = index;
            _tipText.text = _tips[index];
        }

        private void StartFade(float target, float delay)
        {
            if (_fade != null)
            {
                StopCoroutine(_fade);
            }
            _fade = StartCoroutine(Fade(target, delay));
        }

        // Unscaled time: works even while a local game is paused.
        private IEnumerator Fade(float target, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }
            var start = _group.alpha;
            var elapsed = 0f;
            while (_fadeDuration > 0f && elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                ApplyAlpha(Mathf.Lerp(start, target, elapsed / _fadeDuration));
                yield return null;
            }
            ApplyAlpha(target);
            _fade = null;
        }

        private void ApplyAlpha(float alpha)
        {
            _group.alpha = alpha;
            // Blocks clicks on the scene behind while it is visible.
            _group.blocksRaycasts = alpha > 0f;
            _group.interactable = false;
        }
    }
}
