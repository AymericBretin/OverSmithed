using UnityEngine;

namespace OverSmithed.Items
{
    /// <summary>
    /// One kind of item of the game (metal ore, wood, a blade...). To add an item: create one of these assets,
    /// give it a new id, and add it to the <see cref="ItemDatabase"/>. No code change is needed.
    /// The network only sends the <see cref="Id"/>: every machine finds the same asset in its own database.
    /// </summary>
    [CreateAssetMenu(fileName = "Item", menuName = "OverSmithed/Item")]
    public class ItemDefinition : ScriptableObject
    {
        /// <summary>Id that means "no item" (for example: a player who holds nothing).</summary>
        public const int NoneId = 0;

        [Tooltip("Unique number, sent on the network. Must be 1 or more, and never used by another item.")]
        [SerializeField] private int _id;
        [Tooltip("Name shown to players. Empty = the asset name.")]
        [SerializeField] private string _displayName;
        [Tooltip("2D icon for the HUD (order cards).")]
        [SerializeField] private Sprite _icon;
        [Tooltip("Visual only: no NetworkObject, no collider. Pivot at the bottom, centered.")]
        [SerializeField] private GameObject _prefab;

        public int Id => _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public Sprite Icon => _icon;
        public GameObject Prefab => _prefab;

#if UNITY_EDITOR
        /// <summary>Goes up each time an item is edited, so databases know their lookup is out of date.</summary>
        internal static int EditorVersion { get; private set; }

        private void OnValidate()
        {
            EditorVersion++;
        }
#endif
    }
}
