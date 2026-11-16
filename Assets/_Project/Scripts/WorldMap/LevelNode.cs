using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// A level spot on the world map: a circle the walker stands on to enter a level (FLOW-5).
    /// It knows which level it opens (scene name; empty = level not built yet). Scene data only, the same on
    /// every machine: nothing goes on the network. <see cref="All"/> lists the spots of the loaded map.
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
        [SerializeField] private TMP_Text _label;

        private MaterialPropertyBlock _block;

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

        /// <summary>True when <paramref name="position"/> stands on this spot (height ignored).</summary>
        public bool Contains(Vector3 position)
        {
            var offset = position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= _radius * _radius;
        }

        private void Awake()
        {
            ApplyLook();
        }

        private void OnEnable()
        {
            if (!AllNodes.Contains(this))
            {
                AllNodes.Add(this);
            }
        }

        private void OnDisable()
        {
            AllNodes.Remove(this);
        }

        private void ApplyLook()
        {
            if (_label != null)
            {
                _label.text = _levelNumber.ToString();
            }
            if (_platform != null)
            {
                _block ??= new MaterialPropertyBlock();
                _platform.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", _color);
                _platform.SetPropertyBlock(_block);
            }
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
