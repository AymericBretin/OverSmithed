using OverSmithed.Crafting;
using OverSmithed.Items;
using OverSmithed.Orders;
using UnityEditor;
using UnityEngine;

namespace OverSmithed.Editor
{
    [CustomEditor(typeof(OrderManager))]
    public class OrderManagerEditor : UnityEditor.Editor
    {
        // Weights given by "Fill weights from recipes": the simplest item goes 3 → 1, the longest 0 → 3.
        private const float SimpleStart = 3f, SimpleEnd = 1f, LongStart = 0f, LongEnd = 3f;

        private RecipeDatabase _recipes;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var manager = (OrderManager)target;
            DrawWork();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Delivery tests — no crafting needed", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !manager.IsSpawned || !manager.IsServer))
            {
                if (GUILayout.Button("Deliver oldest requested item")) manager.DebugDeliverOldest();
                if (GUILayout.Button("Deliver selected test item")) manager.DebugDeliverSelected();
                if (GUILayout.Button("Try invalid delivery (must be refused)")) manager.DebugDeliverInvalid();
            }
            if (Application.isPlaying && manager.IsSpawned)
            {
                EditorGUILayout.LabelField($"Open: {manager.Count}   Failed: {manager.FailedCount}   Coins: {manager.TeamCoins}");
                var difficulty = manager.Difficulty;
                EditorGUILayout.LabelField($"Difficulty: {difficulty:F2}   Every {manager.Ramp.IntervalAt(difficulty):F0} s   " +
                                           $"Max {manager.MaxOrders}   Patience {manager.Ramp.PatienceAt(difficulty):F0} s");
                Repaint();
            }
        }

        // The work of each menu item (from the recipes) and a button that sets the weights from it.
        private void DrawWork()
        {
            if (_recipes == null)
            {
                var guids = AssetDatabase.FindAssets("t:RecipeDatabase");
                if (guids.Length > 0)
                {
                    _recipes = AssetDatabase.LoadAssetAtPath<RecipeDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Work per item (materials + station steps)", EditorStyles.boldLabel);
            if (_recipes == null)
            {
                EditorGUILayout.HelpBox("No RecipeDatabase asset found.", MessageType.Warning);
                return;
            }

            var menu = serializedObject.FindProperty("_menu");
            for (var i = 0; i < menu.arraySize; i++)
            {
                var item = menu.GetArrayElementAtIndex(i).FindPropertyRelative("Item").objectReferenceValue as ItemDefinition;
                if (item == null)
                {
                    continue;
                }
                var work = _recipes.CountWork(item, out var materials, out var steps);
                EditorGUILayout.LabelField(item.DisplayName, $"{work}  ({materials} materials, {steps} steps)");
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(new GUIContent("Fill weights from recipes",
                        $"Simplest item: {SimpleStart} → {SimpleEnd}, longest: {LongStart} → {LongEnd}, the others in between. " +
                        "Change them by hand after if needed.")))
                {
                    FillWeights(serializedObject, _recipes);
                }
            }
        }

        /// <summary>Sets the start / end weights of the menu of an OrderManager from the work of each item.</summary>
        public static void FillWeights(SerializedObject manager, RecipeDatabase recipes)
        {
            var menu = manager.FindProperty("_menu");
            int min = int.MaxValue, max = int.MinValue;
            var works = new int[menu.arraySize];
            for (var i = 0; i < menu.arraySize; i++)
            {
                var item = menu.GetArrayElementAtIndex(i).FindPropertyRelative("Item").objectReferenceValue as ItemDefinition;
                works[i] = item != null ? recipes.CountWork(item, out _, out _) : -1;
                if (works[i] < 0)
                {
                    continue;
                }
                min = Mathf.Min(min, works[i]);
                max = Mathf.Max(max, works[i]);
            }
            for (var i = 0; i < menu.arraySize; i++)
            {
                if (works[i] < 0)
                {
                    continue;
                }
                // 0 = the simplest item, 1 = the longest. All the same work: they all stay like the simplest.
                var t = max > min ? (works[i] - min) / (float)(max - min) : 0f;
                var entry = menu.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("WeightStart").floatValue = Mathf.Round(Mathf.Lerp(SimpleStart, LongStart, t) * 10f) / 10f;
                entry.FindPropertyRelative("WeightEnd").floatValue = Mathf.Round(Mathf.Lerp(SimpleEnd, LongEnd, t) * 10f) / 10f;
            }
            manager.ApplyModifiedProperties();
        }
    }
}
