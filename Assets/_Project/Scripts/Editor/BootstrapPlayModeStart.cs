using UnityEditor;
using UnityEditor.SceneManagement;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Pressing Play always starts from the Bootstrap scene, whatever scene is open, so the
    /// NetworkManager and application services exist (also applies to Multiplayer Play Mode players).
    /// </summary>
    [InitializeOnLoad]
    internal static class BootstrapPlayModeStart
    {
        private const string BootstrapScenePath = "Assets/_Project/Scenes/Bootstrap.unity";
        private const string MenuPath = "OverSmithed/Toujours démarrer depuis Bootstrap";
        private const string PrefKey = "OverSmithed.AlwaysStartFromBootstrap";

        static BootstrapPlayModeStart()
        {
            EditorApplication.delayCall += Apply;
            // The Bootstrap scene may be created or moved after this class is initialized.
            EditorApplication.projectChanged += Apply;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Apply();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void Apply()
        {
            EditorSceneManager.playModeStartScene = Enabled
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath)
                : null;
        }
    }
}
