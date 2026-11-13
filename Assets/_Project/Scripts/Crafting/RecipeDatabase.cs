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
