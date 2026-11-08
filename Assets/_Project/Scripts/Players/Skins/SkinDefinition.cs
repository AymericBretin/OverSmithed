using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// One look a player can have. To add a skin: create one of these assets, give it a visual prefab, and add
    /// it to the <see cref="SkinLibrary"/>. No code change is needed.
    /// </summary>
    [CreateAssetMenu(fileName = "Skin", menuName = "OverSmithed/Player Skin")]
    public class SkinDefinition : ScriptableObject
    {
        [SerializeField] private string _displayName;
        [Tooltip("Visual only: no NetworkObject, no collider. Pivot at the feet, facing +Z.")]
        [SerializeField] private GameObject _prefab;

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public GameObject Prefab => _prefab;
    }
}
