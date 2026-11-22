using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// A level spot on the world map: a circle the walker stands on to enter a level (FLOW-5).
    /// It knows which level it opens (scene name; empty = level not built yet). Scene data only, the same on
    /// every machine: nothing goes on the network. <see cref="All"/> lists the spots of the loaded map.
    /// Looks (every machine, local): the number floats; the circle under the walker lights up, breathes and its
    /// number grows; a level not built yet is dimmed; when its level starts, a burst of 3D particles.
    /// </summary>
    public class LevelNode : MonoBehaviour
    {
        private static readonly List<LevelNode> AllNodes = new();

        [SerializeField, Min(1)] private int _levelNumber = 1;
        [SerializeField] private string _displayName = "Village";
        [Tooltip("Scene loaded for this level. It must be in the build list. Empty: the level is not built yet.")]
        [SerializeField] private string _sceneName;
        [Tooltip("Color of the circle (graybox: one color per level theme).")]
        [SerializeField] private Color _color = new(0.85f, 0.65f, 0.4f);
        [Tooltip("The walker is on this spot when it is closer than this (m, on the ground) to its center.")]
        [SerializeField, Min(0.1f)] private float _radius = 1.5f;
        [SerializeField] private Renderer _platform;
        [Tooltip("Ring around the platform: breathes while the walker stands on the circle.")]
        [SerializeField] private Transform _rim;
        [SerializeField] private TMP_Text _label;

        [Header("Looks")]
        [Tooltip("How much a level that is not built yet is grayed (0 = normal color, 1 = gray).")]
        [SerializeField, Range(0f, 1f)] private float _unbuiltDim = 0.5f;
        [Tooltip("How much the circle under the walker goes toward white (0 = no change).")]
        [SerializeField, Range(0f, 1f)] private float _activeGlow = 0.3f;
        [Tooltip("Time (s) to light up / go back when the walker arrives / leaves.")]
        [SerializeField, Min(0.01f)] private float _activeTime = 0.2f;
        [Tooltip("Height (m) of the number's float, and seconds per float.")]
        [SerializeField] private float _labelBob = 0.08f;
        [SerializeField, Min(0.1f)] private float _labelBobPeriod = 2.4f;
        [Tooltip("The number rises (m) and grows (×) while the walker stands on the circle.")]
        [SerializeField] private float _activeLabelLift = 0.35f;
        [SerializeField] private float _activeLabelScale = 1.3f;
        [Tooltip("The rim grows and shrinks by this much (0.04 = 4 %) while the walker stands on the circle.")]
        [SerializeField] private float _rimPulse = 0.04f;
        [SerializeField, Min(0.1f)] private float _rimPulsePeriod = 1.2f;
        [Tooltip("Quick squash of the circle when the walker steps on it (0.08 = 8 %).")]
        [SerializeField] private float _stepSquash = 0.08f;
        [Tooltip("3D particle burst created on the circle when its level starts (world map effect prefab).")]
        [SerializeField] private ParticleSystem _launchBurst;

        private MaterialPropertyBlock _block;
        private bool _restSaved;
        private Vector3 _labelRestPosition;
        private Vector3 _labelRestScale;
        private Vector3 _rimRestScale;
        private Vector3 _platformRestScale;
        private float _active;
        private float _activeSpeed;
        private float _shownGlow = -1f;
        private float _squashTime = -1f;
        private bool _wasActive;

        /// <summary>Every level spot of the loaded map.</summary>
        public static IReadOnlyList<LevelNode> All => AllNodes;

        public int LevelNumber => _levelNumber;
        public string DisplayName => _displayName;
        public string SceneName => _sceneName;
        public bool HasLevel => !string.IsNullOrEmpty(_sceneName);
        public float Radius => _radius;

        // Domain reload is off: the static list must be emptied by hand when Play Mode starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AllNodes.Clear();
        }

        /// <summary>The spot <paramref name="position"/> stands on (the closest one if several), else null.</summary>
        public static LevelNode FindAt(Vector3 position)
        {
            LevelNode best = null;
            var bestDistance = float.MaxValue;
            foreach (var node in AllNodes)
            {
                var offset = position - node.transform.position;
                offset.y = 0f;
                var distance = offset.sqrMagnitude;
                if (distance <= node._radius * node._radius && distance < bestDistance)
                {
                    best = node;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>The spot with this level number in the loaded map, else null.</summary>
        public static LevelNode FindByNumber(int levelNumber)
        {
            foreach (var node in AllNodes)
            {
                if (node._levelNumber == levelNumber)
                {
                    return node;
                }
            }
            return null;
        }

        /// <summary>True when <paramref name="position"/> stands on this spot (height ignored).</summary>
        public bool Contains(Vector3 position)
        {
            var offset = position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= _radius * _radius;
        }

        private void Awake()
        {
            SaveRestPose();
            ApplyLook();
        }

        private void OnEnable()
        {
            if (!AllNodes.Contains(this))
            {
                AllNodes.Add(this);
            }
            MapWalker.LevelLaunching += HandleLevelLaunching;
        }

        private void OnDisable()
        {
            AllNodes.Remove(this);
            MapWalker.LevelLaunching -= HandleLevelLaunching;
        }

        // ---------- Looks (every machine, local) ----------

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            var walker = MapWalker.Current;
            var isActive = walker != null && walker.CurrentNode == this;
            if (isActive && !_wasActive)
            {
                _squashTime = 0f;
            }
            _wasActive = isActive;
            _active = Mathf.SmoothDamp(_active, isActive ? 1f : 0f, ref _activeSpeed, _activeTime, Mathf.Infinity,
                deltaTime);

            var time = Time.time;
            if (_label != null)
            {
                // Each number floats with its own phase, so they don't all move together.
                var bob = Mathf.Sin((time / _labelBobPeriod + _levelNumber * 0.37f) * Mathf.PI * 2f) * _labelBob;
                _label.transform.localPosition = _labelRestPosition + Vector3.up * (bob + _active * _activeLabelLift);
                _label.transform.localScale = _labelRestScale * Mathf.Lerp(1f, _activeLabelScale, _active);
            }
            if (_rim != null)
            {
                var pulse = 1f + Mathf.Sin(time / _rimPulsePeriod * Mathf.PI * 2f) * _rimPulse * _active;
                _rim.localScale = new Vector3(_rimRestScale.x * pulse, _rimRestScale.y, _rimRestScale.z * pulse);
            }
            if (_platform != null)
            {
                // Stepping on it: wider and flatter for a moment, with a small damped wobble.
                var squash = 0f;
                if (_squashTime >= 0f)
                {
                    _squashTime += deltaTime;
                    const float duration = 0.35f;
                    var t = _squashTime / duration;
                    squash = t >= 1f ? 0f : Mathf.Sin(t * Mathf.PI * 2.5f) * (1f - t) * _stepSquash;
                    if (t >= 1f)
                    {
                        _squashTime = -1f;
                    }
                }
                _platform.transform.localScale = new Vector3(_platformRestScale.x * (1f + squash * 0.5f),
                    _platformRestScale.y * (1f - squash), _platformRestScale.z * (1f + squash * 0.5f));
            }
            // The color only changes while lighting up / going back (no property block write every frame).
            if (!Mathf.Approximately(_shownGlow, _active))
            {
                ApplyColor(_active);
            }
        }

        private void HandleLevelLaunching(LevelNode node)
        {
            if (node != this || _launchBurst == null)
            {
                return;
            }
            var burst = Instantiate(_launchBurst, transform.position + Vector3.up * 0.2f, Quaternion.identity);
            // Mix the circle's color with the burst's own colors.
            var main = burst.main;
            main.startColor = new ParticleSystem.MinMaxGradient(main.startColor.color, _color);
            burst.Play();
        }

        private void SaveRestPose()
        {
            if (_restSaved)
            {
                return;
            }
            _restSaved = true;
            if (_label != null)
            {
                _labelRestPosition = _label.transform.localPosition;
                _labelRestScale = _label.transform.localScale;
            }
            if (_rim != null)
            {
                _rimRestScale = _rim.localScale;
            }
            if (_platform != null)
            {
                _platformRestScale = _platform.transform.localScale;
            }
        }

        private void ApplyLook()
        {
            if (_label != null)
            {
                _label.text = _levelNumber.ToString();
            }
            ApplyColor(0f);
        }

        private void ApplyColor(float glow)
        {
            _shownGlow = glow;
            if (_platform == null)
            {
                return;
            }
            var color = _color;
            if (!HasLevel)
            {
                var gray = color.grayscale * 0.85f;
                color = Color.Lerp(color, new Color(gray, gray, gray, color.a), _unbuiltDim);
            }
            color = Color.Lerp(color, Color.white, glow * _activeGlow);
            _block ??= new MaterialPropertyBlock();
            _platform.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", color);
            _platform.SetPropertyBlock(_block);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyLook();
            if (string.IsNullOrEmpty(_sceneName))
            {
                return;
            }
            foreach (var scene in UnityEditor.EditorBuildSettings.scenes)
            {
                if (scene.enabled && System.IO.Path.GetFileNameWithoutExtension(scene.path) == _sceneName)
                {
                    return;
                }
            }
            Debug.LogWarning($"[LevelNode] '{name}': scene '{_sceneName}' is not in the build list.", this);
        }

        private void OnDrawGizmos()
        {
            UnityEditor.Handles.color = _color;
            UnityEditor.Handles.DrawWireDisc(transform.position + Vector3.up * 0.05f, Vector3.up, _radius);
            var scene = HasLevel ? _sceneName : "not built yet";
            UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, $"{_levelNumber}. {_displayName} ({scene})");
        }
#endif
    }
}
