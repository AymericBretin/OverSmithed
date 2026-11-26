using OverSmithed.Items;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// Shared settings of the forge trays (CR-11): which items are the clean and the worn tray, the recipes used to put
    /// parts together on a tray, and the networked tray prefab (needed to take one tray off a stack). An asset, so a
    /// tray can point at its own prefab (a prefab pointing at itself would point at the copy once created).
    /// </summary>
    [CreateAssetMenu(menuName = "OverSmithed/Tray Config", fileName = "TrayConfig")]
    public class TrayConfig : ScriptableObject
    {
        [Tooltip("The clean tray: parts can be put on it, and clean empty trays stack.")]
        [SerializeField] private ItemDefinition _cleanTray;
        [Tooltip("The worn tray (back from a delivery, CR-12): nothing can be put on it until it is polished (CR-19).")]
        [SerializeField] private ItemDefinition _wornTray;
        [Tooltip("Turns the ids of the parts on a tray back into items (the same database as the items).")]
        [SerializeField] private ItemDatabase _items;
        [Tooltip("Recipes with the Tray station put parts together on a tray.")]
        [SerializeField] private RecipeDatabase _recipes;
        [Tooltip("The networked tray prefab (NetworkItem + Tray).")]
        [SerializeField] private NetworkItem _trayPrefab;

        public ItemDefinition CleanTray => _cleanTray;
        public ItemDefinition WornTray => _wornTray;
        public ItemDatabase Items => _items;
        public RecipeDatabase Recipes => _recipes;
        public NetworkItem TrayPrefab => _trayPrefab;
    }
}
