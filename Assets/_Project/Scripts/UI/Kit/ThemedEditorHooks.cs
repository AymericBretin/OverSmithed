#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OverSmithed.UI
{
    /// <summary>Editor-only behavior shared by the themed components.</summary>
    internal static class ThemedEditorHooks
    {
        /// <summary>
        /// Called from OnValidate: fills an empty theme field with the project's theme and refreshes the look
        /// right away in scenes and in Prefab Mode. Prefab files on disk are not touched here (Unity forbids
        /// changing assets while it loads them): the "Apply theme" button of the theme asset updates them.
        /// </summary>
        public static void OnValidate<T>(T component, ref UiTheme theme) where T : MonoBehaviour, IThemed
        {
            if (!component.gameObject.scene.IsValid() || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            if (theme == null)
            {
                theme = FindProjectTheme();
            }

            // Changing other components inside OnValidate gives warnings: do it just after.
            EditorApplication.delayCall += () =>
            {
                if (component == null)
                {
                    return;
                }
                component.ApplyTheme();
                var graphic = component.GetComponent<Graphic>();
                if (graphic != null)
                {
                    EditorUtility.SetDirty(graphic);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
                }
            };
        }

        public static UiTheme FindProjectTheme()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(UiTheme));
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<UiTheme>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
#endif
