using OverSmithed.Orders;
using UnityEditor;
using UnityEngine;

namespace OverSmithed.Editor
{
    [CustomEditor(typeof(OrderManager))]
    public class OrderManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var manager = (OrderManager)target;
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
                Repaint();
            }
        }
    }
}
