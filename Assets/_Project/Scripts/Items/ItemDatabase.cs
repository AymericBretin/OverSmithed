using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.Items
{
    /// <summary>
    /// The list of all items of the game. Code finds an item by its id with <see cref="TryGet"/> or
    /// <see cref="Get"/>. Every machine must use the same database (same build), because only ids travel on
    /// the network.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "OverSmithed/Item Database")]
    public class ItemDatabase : ScriptableObject
    {
        [SerializeField] private List<ItemDefinition> _items = new();

        // Built on first use, cleared when the list changes in the editor.
        private Dictionary<int, ItemDefinition> _byId;
#if UNITY_EDITOR
        private int _builtAtVersion;
#endif

        public IReadOnlyList<ItemDefinition> Items => _items;

        /// <summary>Finds the item with this id. Returns false if there is none (or if id is NoneId).</summary>
        public bool TryGet(int id, out ItemDefinition item)
        {
#if UNITY_EDITOR
            // An item's id may have changed in the Inspector since the lookup was built.
            if (_builtAtVersion != ItemDefinition.EditorVersion)
            {
                _byId = null;
                _builtAtVersion = ItemDefinition.EditorVersion;
            }
#endif
            _byId ??= BuildLookup();
            return _byId.TryGetValue(id, out item);
        }

        /// <summary>The item with this id, or null if there is none.</summary>
        public ItemDefinition Get(int id)
        {
            return TryGet(id, out ItemDefinition item) ? item : null;
        }

        private Dictionary<int, ItemDefinition> BuildLookup()
        {
            var lookup = new Dictionary<int, ItemDefinition>(_items.Count);
            foreach (ItemDefinition item in _items)
            {
                // Bad entries are reported by OnValidate; the first item with an id wins.
                if (item != null && item.Id != ItemDefinition.NoneId)
                    lookup.TryAdd(item.Id, item);
            }
            return lookup;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _byId = null;

            var seen = new Dictionary<int, ItemDefinition>();
            for (int i = 0; i < _items.Count; i++)
            {
                ItemDefinition item = _items[i];
                if (item == null)
                {
                    Debug.LogWarning($"[ItemDatabase] Entry {i} is empty.", this);
                    continue;
                }
                if (item.Id <= ItemDefinition.NoneId)
                {
                    Debug.LogWarning($"[ItemDatabase] '{item.name}' has id {item.Id}: ids must be 1 or more.", item);
                    continue;
                }
                if (seen.TryGetValue(item.Id, out ItemDefinition other))
                {
                    Debug.LogWarning(other == item
                        ? $"[ItemDatabase] '{item.name}' is in the list twice."
                        : $"[ItemDatabase] '{item.name}' and '{other.name}' have the same id {item.Id}.", item);
                    continue;
                }
                seen.Add(item.Id, item);
            }
        }
#endif
    }
}
