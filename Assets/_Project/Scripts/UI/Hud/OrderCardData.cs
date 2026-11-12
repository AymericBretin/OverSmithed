using System;
using System.Collections.Generic;
using OverSmithed.Crafting;
using OverSmithed.Items;
using UnityEngine;

namespace OverSmithed.UI
{
    /// <summary>
    /// What an order card shows. Only display data: the real orders (UI-10) build one of these from their recipe.
    /// </summary>
    [Serializable]
    public class OrderCardData
    {
        [Tooltip("Name of the item to forge (used by the placeholder icon).")]
        public string ResultName;
        [Tooltip("Icon of the item to forge. Empty: placeholder.")]
        public Sprite ResultIcon;
        [Tooltip("Materials, by group. Each group shows its stations under it.")]
        public List<OrderCardGroup> Groups = new();

        public static OrderCardData FromItem(ItemDefinition result)
        {
            return new OrderCardData { ResultName = result.DisplayName, ResultIcon = result.Icon };
        }
    }

    /// <summary>
    /// Materials worked together, for example 2 logs on the workbench. The station icons are drawn under them,
    /// in order (ore: furnace then anvil). No station: the materials are used as they are.
    /// </summary>
    [Serializable]
    public class OrderCardGroup
    {
        public List<ItemDefinition> Materials = new();
        public List<StationKind> Stations = new();
    }
}
