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

        /// <summary>
        /// The card of a real order: follows the recipes from <paramref name="result"/> back to the raw materials.
        /// Each raw material is a group, with the stations it goes through, in order (ore: furnace then anvil).
        /// The station that joins several parts (the assembly table, like the plate in Overcooked) is not shown:
        /// every order ends there, so players learn it once (Aymeric's choice).
        /// Example, sword = blade + handle: [ore: furnace → anvil] [wood: workbench].
        /// </summary>
        public static OrderCardData FromRecipe(ItemDefinition result, RecipeDatabase recipes)
        {
            var data = FromItem(result);
            data.Groups = GroupsFor(result, recipes, 0);
            return data;
        }

        private static List<OrderCardGroup> GroupsFor(ItemDefinition item, RecipeDatabase recipes, int depth)
        {
            // A raw material (no recipe makes it): one group with just it. The depth limit stops a broken loop.
            if (recipes == null || depth > 10 || !recipes.TryFindByOutput(item, out var recipe))
            {
                return new List<OrderCardGroup> { new() { Materials = { item } } };
            }

            var inputs = recipe.Inputs;
            // Raw materials worked together (2 logs on the workbench): one group, the station under them
            // (not the assembly table: joining is never shown).
            if (inputs.Count > 1 && AllRaw(inputs, recipes))
            {
                var together = new OrderCardGroup();
                if (recipe.Station != StationKind.Tray)
                {
                    together.Stations.Add(recipe.Station);
                }
                together.Materials.AddRange(inputs);
                return new List<OrderCardGroup> { together };
            }

            var groups = new List<OrderCardGroup>();
            foreach (var input in inputs)
            {
                groups.AddRange(GroupsFor(input, recipes, depth + 1));
            }
            // One input changed by a station: every group of it goes through that station next. Several parts
            // joined together (assembly): nothing to show, see above.
            if (inputs.Count == 1)
            {
                foreach (var group in groups)
                {
                    group.Stations.Add(recipe.Station);
                }
            }
            return groups;
        }

        private static bool AllRaw(IReadOnlyList<ItemDefinition> items, RecipeDatabase recipes)
        {
            foreach (var item in items)
            {
                if (recipes.TryFindByOutput(item, out _))
                {
                    return false;
                }
            }
            return true;
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
