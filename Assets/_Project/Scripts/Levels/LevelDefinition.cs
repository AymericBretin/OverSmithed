using UnityEngine;

namespace OverSmithed.Levels
{
    /// <summary>
    /// One level of the game, as an asset (Create > OverSmithed > Level): its number on the world map, its name, its
    /// scene, the preview picture shown in the world map popup (UI-12) and the coins needed for each star (shown in
    /// the popup; CR-17 (#53) gives the stars with them). A level circle on the map points to its asset. FLOW-6 (#61)
    /// adds the other settings here (recipes, shift time).
    /// Scene data, the same on every machine: nothing goes on the network.
    /// </summary>
    [CreateAssetMenu(menuName = "OverSmithed/Level", fileName = "Level_")]
    public class LevelDefinition : ScriptableObject
    {
        [Tooltip("Number on the world map (1 = first level). Also the key of the level's records in the save.")]
        [SerializeField, Min(1)] private int _number = 1;
        [SerializeField] private string _displayName = "Village";
        [Tooltip("Scene loaded for this level. It must be in the build list. Empty: the level is not built yet.")]
        [SerializeField] private string _sceneName;
        [Tooltip("Small picture of the level (16:9) for the world map popup. Empty: the popup shows a big \"?\". " +
                 "Menu OverSmithed/Render Level Previews takes a photo from the level's camera; a drawn picture " +
                 "(outside the Photos folder) is never replaced.")]
        [SerializeField] private Sprite _preview;
        [Tooltip("Coins needed for 1, 2 and 3 stars (growing). Placeholder values until the recipes are balanced " +
                 "(CR-15 / CR-16).")]
        [SerializeField] private int[] _starCoins = { 60, 120, 180 };

        public int Number => _number;
        public string DisplayName => _displayName;
        public string SceneName => _sceneName;
        public bool IsBuilt => !string.IsNullOrEmpty(_sceneName);
        public Sprite Preview => _preview;

        /// <summary>Number of stars a level can give (3).</summary>
        public const int MaxStars = 3;

        /// <summary>Coins needed for <paramref name="stars"/> stars (1 to 3); 0 if not set.</summary>
        public int CoinsForStars(int stars)
        {
            var index = stars - 1;
            return _starCoins != null && index >= 0 && index < _starCoins.Length ? _starCoins[index] : 0;
        }

#if UNITY_EDITOR
        /// <summary>Editor only (preview photo tool).</summary>
        public void EditorSetPreview(Sprite preview)
        {
            _preview = preview;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private void OnValidate()
        {
            if (_starCoins == null || _starCoins.Length != MaxStars)
            {
                Debug.LogWarning($"[LevelDefinition] '{name}': give exactly {MaxStars} star limits.", this);
            }
            else if (_starCoins[0] <= 0 || _starCoins[1] <= _starCoins[0] || _starCoins[2] <= _starCoins[1])
            {
                Debug.LogWarning($"[LevelDefinition] '{name}': star limits must be above 0 and growing.", this);
            }
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
            Debug.LogWarning($"[LevelDefinition] '{name}': scene '{_sceneName}' is not in the build list.", this);
        }
#endif
    }
}
