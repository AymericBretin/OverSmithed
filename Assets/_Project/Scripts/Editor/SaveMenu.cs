using OverSmithed.Core;
using UnityEditor;
using UnityEngine;

namespace OverSmithed.Editor
{
    /// <summary>Editor menu to go back to a new save (e.g. to see the story intro again).</summary>
    public static class SaveMenu
    {
        [MenuItem("OverSmithed/Reset Save (story intro plays again)", priority = 200)]
        private static void ResetSave()
        {
            GameProgress.ResetAll();
            Debug.Log("[Save] Back to a new save: the story intro plays again before level 1.");
        }
    }
}
