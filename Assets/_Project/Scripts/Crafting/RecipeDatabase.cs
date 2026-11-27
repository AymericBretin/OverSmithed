using System.Collections.Generic;
using OverSmithed.Items;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// The list of all recipes of the game. Stations ask it what they make with an item
    /// (<see cref="TryFind(StationKind, ItemDefinition, out RecipeDefinition)"/>); order cards ask it how an item
    /// is made (<see cref="TryFindByOutput"/>). Code gets it through a serialized reference.
    /// </summary>
    [CreateAssetMenu(fileName = "RecipeDatabase", menuName = "OverSmithed/Recipe Database")]
    public class RecipeDatabase : ScriptableObject
    {
        [SerializeField] private List<RecipeDefinition> _recipes = new();

        public IReadOnlyList<RecipeDefinition> Recipes => _recipes;

        // A plain loop: there are a few dozen recipes at most, and nothing to rebuild when one is edited.

        /// <summary>"What does this station make with this item?" Returns false if nothing.</summary>
        public bool TryFind(StationKind station, ItemDefinition input, out RecipeDefinition recipe)
        {
            foreach (var candidate in _recipes)
            {
                if (IsUsable(candidate) && candidate.Station == station && candidate.HasSingleInput(input))
                {
                    recipe = candidate;
                    return true;
                }
            }
            recipe = null;
            return false;
        }

        /// <summary>Same question with several items (assembly table). The order of the items does not matter.</summary>
        public bool TryFind(StationKind station, IReadOnlyList<ItemDefinition> inputs, out RecipeDefinition recipe)
        {
            foreach (var candidate in _recipes)
            {
                if (IsUsable(candidate) && candidate.Station == station && candidate.HasInputs(inputs))
                {
                    recipe = candidate;
                    return true;
                }
            }
            recipe = null;
            return false;
        }

        /// <summary>
        /// True if some recipe of this station uses all of <paramref name="items"/> (maybe with more items to add).
        /// For example blade alone on a tray: yes (blade + handle = sword).
        /// </summary>
        public bool CanStillMake(StationKind station, IReadOnlyList<ItemDefinition> items)
        {
            foreach (var candidate in _recipes)
            {
                if (IsUsable(candidate) && candidate.Station == station && candidate.ContainsInputs(items))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>"How is this item made?" Returns false for items that are not made (raw materials).</summary>
        public bool TryFindByOutput(ItemDefinition output, out RecipeDefinition recipe)
        {
            foreach (var candidate in _recipes)
            {
                if (IsUsable(candidate) && candidate.Output == output)
                {
                    recipe = candidate;
                    return true;
                }
            }
            recipe = null;
            return false;
        }

        /// <summary>
        /// "How much work is this item?" <paramref name="materials"/> = raw materials to take from the crates,
        /// <paramref name="steps"/> = uses of a work station (furnace, anvil, workbench...). Joining parts on a forge
        /// tray is not counted: every order ends with it. Sword = 2 materials (ore, wood) + 3 steps = 5 of work.
        /// Used to rank the orders from simple to long (CR-16).
        /// </summary>
        public int CountWork(ItemDefinition item, out int materials, out int steps)
        {
            materials = 0;
            steps = 0;
            CountWork(item, 0, ref materials, ref steps);
            return materials + steps;
        }

        private void CountWork(ItemDefinition item, int depth, ref int materials, ref int steps)
        {
            // A raw material (no recipe makes it). The depth limit stops a broken loop.
            if (item == null || depth > 10 || !TryFindByOutput(item, out var recipe))
            {
                materials++;
                return;
            }
            if (recipe.Station != StationKind.Tray)
            {
                steps++;
            }
            foreach (var input in recipe.Inputs)
            {
                CountWork(input, depth + 1, ref materials, ref steps);
            }
        }

        private static bool IsUsable(RecipeDefinition recipe)
        {
            return recipe != null && recipe.FindProblem() == null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (var i = 0; i < _recipes.Count; i++)
            {
                var recipe = _recipes[i];
                if (recipe == null)
                {
                    Debug.LogWarning($"[RecipeDatabase] Entry {i} is empty.", this);
                    continue;
                }
                var problem = recipe.FindProblem();
                if (problem != null)
                {
                    Debug.LogWarning($"[RecipeDatabase] '{recipe.name}' {problem}: it is ignored.", recipe);
                    continue;
                }
                for (var j = 0; j < i; j++)
                {
                    var other = _recipes[j];
                    if (other == null || other.FindProblem() != null)
                    {
                        continue;
                    }
                    if (other == recipe)
                    {
                        Debug.LogWarning($"[RecipeDatabase] '{recipe.name}' is in the list twice.", recipe);
                    }
                    else if (other.Station == recipe.Station && other.HasInputs(recipe.Inputs))
                    {
                        Debug.LogWarning($"[RecipeDatabase] '{recipe.name}' and '{other.name}' use the same items " +
                                         $"on the same station: only '{other.name}' is used.", recipe);
                    }
                    else if (other.Output == recipe.Output)
                    {
                        Debug.LogWarning($"[RecipeDatabase] '{recipe.name}' and '{other.name}' make the same item: " +
                                         $"order cards show '{other.name}'.", recipe);
                    }
                }
            }
        }
#endif
    }
}
