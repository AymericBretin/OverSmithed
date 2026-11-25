using OverSmithed.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Debug menu "OverSmithed/Loading Screen Preview" (Play Mode): shows the loading screen and keeps it on screen
    /// (a new tip each time, the current scene as destination); click again to hide it. Only on this machine.
    /// </summary>
    internal static class LoadingScreenPreview
    {
        private const string MenuPath = "OverSmithed/Loading Screen Preview (show or hide)";

        [MenuItem(MenuPath, priority = 140)]
        private static void Toggle()
        {
            var screen = LoadingScreen.Instance;
            if (screen.IsPreviewing)
            {
                screen.HidePreview();
            }
            else
            {
                screen.ShowPreview(SceneManager.GetActiveScene().name);
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, EditorApplication.isPlaying && LoadingScreen.Instance != null && LoadingScreen.Instance.IsPreviewing);
            return EditorApplication.isPlaying && LoadingScreen.Instance != null;
        }
    }
}
