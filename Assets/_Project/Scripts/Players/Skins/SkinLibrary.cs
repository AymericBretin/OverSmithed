using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.Players
{
    /// <summary>
    /// The list of player skins. A skin is sent on the network as its index in this list, so every machine
    /// must use the same list (same build). The host gives each new player the first skin nobody uses.
    /// </summary>
    [CreateAssetMenu(fileName = "SkinLibrary", menuName = "OverSmithed/Skin Library")]
    public class SkinLibrary : ScriptableObject
    {
        [Tooltip("Keep at least as many skins as players (4), so every player gets a different one.")]
        [SerializeField] private List<SkinDefinition> _skins = new();

        public int Count => _skins.Count;

        /// <summary>The skin at this index, or null if there is none.</summary>
        public SkinDefinition Get(int index)
        {
            return index >= 0 && index < _skins.Count ? _skins[index] : null;
        }
    }
}
