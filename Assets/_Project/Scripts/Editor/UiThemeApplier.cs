using System.Linq;
using OverSmithed.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Pushes the values of a <see cref="UiTheme"/> to every themed component: first in the prefab files of
    /// the project, then in the open scenes. Parts of prefab instances are skipped, they follow their prefab.
    /// </summary>
    internal static class UiThemeApplier
    {
        private const string ProjectFolder = "Assets/_Project";

        [MenuItem("OverSmithed/UI/Apply theme everywhere")]
        private static void ApplyProjectTheme()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(UiTheme)))
            {
                Apply(AssetDatabase.LoadAssetAtPath<UiTheme>(AssetDatabase.GUIDToAssetPath(guid)));
            }
        }

        public static void Apply(UiTheme theme)
        {
            var prefabCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ProjectFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (ApplyTo(root.GetComponentsInChildren<IThemed>(true), theme) > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        prefabCount++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            var sceneCount = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                var themed = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<IThemed>(true));
                if (ApplyTo(themed, theme) > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    sceneCount++;
                }
            }

            Debug.Log($"[UiTheme] {theme.name} applied to {prefabCount} prefab(s) and {sceneCount} open scene(s).");
        }

        private static int ApplyTo(System.Collections.Generic.IEnumerable<IThemed> components, UiTheme theme)
        {
            var count = 0;
            foreach (var themed in components)
            {
                var component = (Component)themed;
                // A nested prefab instance gets its look from its own prefab file.
                if (themed.Theme != theme || PrefabUtility.IsPartOfPrefabInstance(component))
                {
                    continue;
                }
                themed.ApplyTheme();
                var graphic = component.GetComponent<Graphic>();
                if (graphic != null)
                {
                    EditorUtility.SetDirty(graphic);
                }
                count++;
            }
            return count;
        }
    }

    [CustomEditor(typeof(UiTheme))]
    internal class UiThemeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "After changing a value, press \"Apply theme\" to update every UI prefab and the open scenes.",
                MessageType.Info);
            if (GUILayout.Button("Apply theme", GUILayout.Height(28)))
            {
                AssetDatabase.SaveAssets();
                UiThemeApplier.Apply((UiTheme)target);
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
