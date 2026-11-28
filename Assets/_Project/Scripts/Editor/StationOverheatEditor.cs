using OverSmithed.Crafting;
using UnityEditor;
using UnityEngine;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Inspector of <see cref="StationOverheat"/>: shows the phase in Play Mode and has host debug buttons to set the
    /// station on fire or put the fire out (until the water bucket of CR-14 exists).
    /// </summary>
    [CustomEditor(typeof(StationOverheat))]
    public class StationOverheatEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var overheat = (StationOverheat)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fire tests (host, Play Mode)", EditorStyles.boldLabel);
            var canUse = Application.isPlaying && overheat.IsSpawned && overheat.IsServer;
            using (new EditorGUI.DisabledScope(!canUse || overheat.IsOnFire))
            {
                if (GUILayout.Button("Set on fire now")) overheat.IgniteNow();
            }
            using (new EditorGUI.DisabledScope(!canUse || !overheat.IsOnFire))
            {
                if (GUILayout.Button("Put the fire out")) overheat.Extinguish();
            }
            if (Application.isPlaying && overheat.IsSpawned)
            {
                var phase = overheat.Phase;
                var text = phase == OverheatPhase.Warning
                    ? $"Phase: Warning ({overheat.WarningProgress:P0})"
                    : $"Phase: {phase}";
                EditorGUILayout.LabelField(text);
                Repaint();
            }
        }
    }
}
