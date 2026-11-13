using System.Collections.Generic;
using OverSmithed.Items;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// One recipe: these input item(s), worked on this kind of station, become the output item.
    /// For example metal ore in the furnace becomes a hot ingot. Add the asset to the <see cref="RecipeDatabase"/>.
    /// Recipes are the same on every machine (same build): nothing about them is sent on the network.
    /// </summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "OverSmithed/Recipe")]
    public class RecipeDefinition : ScriptableObject
    {
        [Tooltip("Items used. One for the furnace, the anvil and the workbench; several for the assembly table " +
                 "(the order does not matter). Put an item twice if two are needed.")]
        [SerializeField] private List<ItemDefinition> _inputs = new();
        [SerializeField] private StationKind _station;
        [SerializeField] private ItemDefinition _output;
        [Tooltip("Seconds of work. Furnace: time on the fire. Anvil, workbench: time holding Use. 0 = instant.")]
        [Min(0f)]
        [SerializeField] private float _workTime = 3f;

        public IReadOnlyList<ItemDefinition> Inputs => _inputs;
        public StationKind Station => _station;
        public ItemDefinition Output => _output;
        public float WorkTime => _workTime;

        /// <summary>True if <paramref name="items"/> are exactly this recipe's inputs, in any order.</summary>
        public bool HasInputs(IReadOnlyList<ItemDefinition> items)
        {
            if (items == null || items.Count != _inputs.Count)
            {
                return false;
            }
            // Same items, same number of each (a few items only: no need for anything faster).
            foreach (var input in _inputs)
            {
                if (Count(_inputs, input) != Count(items, input))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>True if this recipe needs exactly one item, and it is <paramref name="item"/>.</summary>
        public bool HasSingleInput(ItemDefinition item)
        {
            return _inputs.Count == 1 && _inputs[0] == item;
        }

        /// <summary>What is wrong with this recipe, or null if it can be used.</summary>
        public string FindProblem()
        {
            if (_output == null)
            {
                return "has no output item";
            }
            if (_inputs.Count == 0)
            {
                return "has no input item";
            }
            if (_inputs.Contains(null))
            {
                return "has an empty input";
            }
            if (_station == StationKind.None)
            {
                return "has no station";
            }
            return null;
        }

        private static int Count(IReadOnlyList<ItemDefinition> items, ItemDefinition item)
        {
            var count = 0;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] == item)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
