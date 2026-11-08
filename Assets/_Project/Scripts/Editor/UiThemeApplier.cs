using System.Collections.Generic;
using System.Linq;
using OverSmithed.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Pushes the values of a <see cref="UiTheme"/> to every themed component: first in the prefab files of
    /// the project (a prefab is always updated before the prefabs that contain it), then in the open scenes.
    /// Instances that kept the prefab's style get the same values as their prefab, so no override appears;
    /// instances that chose another style or color are updated too.
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
            foreach (var path in PrefabsInDependencyOrder())
            {
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

        private static int ApplyTo(IEnumerable<IThemed> components, UiTheme theme)
        {
            var count = 0;
            foreach (var themed in components)
            {
                if (themed.Theme != theme)
                {
                    continue;
                }
                themed.ApplyTheme();
                MarkChanged(((Component)themed).gameObject);
                count++;
            }
            return count;
        }

        /// <summary>Saves the changes made by ApplyTheme on the components of this object.</summary>
        public static void MarkChanged(GameObject gameObject)
        {
            foreach (var component in gameObject.GetComponents<Component>())
            {
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            }
        }

        // A prefab that contains another prefab comes after it, so it sees the already updated values.
        private static List<string> PrefabsInDependencyOrder()
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { ProjectFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToHashSet();
            var ordered = new List<string>();
            var visited = new HashSet<string>();

            void Visit(string path)
            {
                if (!visited.Add(path))
                {
                    return;
                }
                foreach (var dependency in AssetDatabase.GetDependencies(path, false))
                {
                    if (paths.Contains(dependency))
                    {
                        Visit(dependency);
                    }
                }
                ordered.Add(path);
            }

            foreach (var path in paths.OrderBy(p => p))
            {
                Visit(path);
            }
            return ordered;
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
