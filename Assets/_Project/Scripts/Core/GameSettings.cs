using System;
using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.Core
{
    /// <summary>
    /// Player settings of this machine (sound volumes, display), saved in PlayerPrefs so they are still there after
    /// a restart. Local only: nothing goes on the network. Loaded and applied once when the game starts.
    /// The volumes are only stored here; the audio manager (FLOW-10) reads them and listens to <see cref="Changed"/>.
    /// </summary>
    public static class GameSettings
    {
        public const float DefaultVolume = 0.8f;

        private const string MusicKey = "settings.musicVolume";
        private const string EffectsKey = "settings.effectsVolume";
        private const string FullscreenKey = "settings.fullscreen";
        private const string WidthKey = "settings.resolutionWidth";
        private const string HeightKey = "settings.resolutionHeight";

        private static bool _loaded;
        private static float _musicVolume;
        private static float _effectsVolume;
        private static bool _fullscreen;
        private static Vector2Int _resolution;

        /// <summary>Raised after any setting changed (also after a reset).</summary>
        public static event Action Changed;

        /// <summary>Music volume, 0 (silent) to 1.</summary>
        public static float MusicVolume
        {
            get { EnsureLoaded(); return _musicVolume; }
            set { EnsureLoaded(); SetVolume(ref _musicVolume, MusicKey, value); }
        }

        /// <summary>Sound effects volume, 0 (silent) to 1.</summary>
        public static float EffectsVolume
        {
            get { EnsureLoaded(); return _effectsVolume; }
            set { EnsureLoaded(); SetVolume(ref _effectsVolume, EffectsKey, value); }
        }

        /// <summary>True: fullscreen (borderless window over the whole screen). False: a normal window.</summary>
        public static bool Fullscreen
        {
            get { EnsureLoaded(); return _fullscreen; }
            set
            {
                EnsureLoaded();
                if (_fullscreen == value)
                {
                    return;
                }
                _fullscreen = value;
                PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
                SaveAndApplyDisplay();
            }
        }

        /// <summary>Screen size in pixels (width, height).</summary>
        public static Vector2Int Resolution
        {
            get { EnsureLoaded(); return _resolution; }
            set
            {
                EnsureLoaded();
                if (_resolution == value || value.x <= 0 || value.y <= 0)
                {
                    return;
                }
                _resolution = value;
                PlayerPrefs.SetInt(WidthKey, value.x);
                PlayerPrefs.SetInt(HeightKey, value.y);
                SaveAndApplyDisplay();
            }
        }

        /// <summary>
        /// Sizes the screen supports, biggest first, each size once (the same size at several refresh rates is
        /// listed only once). Always contains the current setting.
        /// </summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            var sizes = new List<Vector2Int>();
            foreach (var resolution in Screen.resolutions)
            {
                var size = new Vector2Int(resolution.width, resolution.height);
                if (!sizes.Contains(size))
                {
                    sizes.Add(size);
                }
            }
            var current = Resolution;
            if (!sizes.Contains(current))
            {
                sizes.Add(current);
            }
            sizes.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return sizes;
        }

        /// <summary>Puts every setting back to its default value, saves and applies it.</summary>
        public static void ResetToDefaults()
        {
            EnsureLoaded();
            _musicVolume = DefaultVolume;
            _effectsVolume = DefaultVolume;
            _fullscreen = true;
            _resolution = NativeResolution();
            PlayerPrefs.SetFloat(MusicKey, _musicVolume);
            PlayerPrefs.SetFloat(EffectsKey, _effectsVolume);
            PlayerPrefs.SetInt(FullscreenKey, 1);
            PlayerPrefs.SetInt(WidthKey, _resolution.x);
            PlayerPrefs.SetInt(HeightKey, _resolution.y);
            SaveAndApplyDisplay();
        }

        // Domain reload is off: forget the loaded values when Play Mode starts, so they are read again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = false;
            Changed = null;
        }

        // Applies the saved display settings when the game starts (before the first scene shows).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAtStart()
        {
            EnsureLoaded();
            ApplyDisplay();
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            _musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultVolume));
            _effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, DefaultVolume));
            _fullscreen = PlayerPrefs.GetInt(FullscreenKey, 1) == 1;
            var native = NativeResolution();
            _resolution = new Vector2Int(PlayerPrefs.GetInt(WidthKey, native.x), PlayerPrefs.GetInt(HeightKey, native.y));
        }

        private static void SetVolume(ref float field, string key, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value))
            {
                return;
            }
            field = value;
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static void SaveAndApplyDisplay()
        {
            PlayerPrefs.Save();
            ApplyDisplay();
            Changed?.Invoke();
        }

        // In the editor the Game view ignores this: test display settings in a build.
        private static void ApplyDisplay()
        {
            var mode = _fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Screen.SetResolution(_resolution.x, _resolution.y, mode);
        }

        // The monitor's own size: the best default.
        private static Vector2Int NativeResolution()
        {
            var display = Screen.currentResolution;
            return display.width > 0 && display.height > 0
                ? new Vector2Int(display.width, display.height)
                : new Vector2Int(1920, 1080);
        }
    }
}
