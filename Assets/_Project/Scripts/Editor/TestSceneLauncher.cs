using OverSmithed.Core;
using OverSmithed.Networking;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Test any scene in game, with players. Open the scene in the editor, press Play (it starts from Bootstrap),
    /// host a game and join in the lobby, then use the menu "OverSmithed/Launch Edited Scene": the host loads
    /// that scene for everyone, like the Launch button does for the Game scene.
    /// The scene must be in the build list (Netcode can only load those) and have a PlayerSpawner.
    /// </summary>
    [InitializeOnLoad]
    internal static class TestSceneLauncher
    {
        private const string MenuPath = "OverSmithed/Launch Edited Scene (host, in lobby)";
        private const string EditedSceneKey = "OverSmithed.EditedScenePath";

        static TestSceneLauncher()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        // Remember the scene open in the editor, before Play Mode replaces it with Bootstrap.
        private static void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                SessionState.SetString(EditedSceneKey, EditorSceneManager.GetActiveScene().path);
            }
        }

        [MenuItem(MenuPath)]
        private static void Launch()
        {
            var path = SessionState.GetString(EditedSceneKey, string.Empty);
            var sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!IsInBuildList(path))
            {
                Debug.LogWarning($"[TestSceneLauncher] '{path}' is not in the build list: add it in Build Profiles.");
                return;
            }
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                Debug.LogWarning("[TestSceneLauncher] Only the host can launch a scene. Host a game first.");
                return;
            }
            var roster = SessionRoster.Instance;
            if (roster == null || roster.Count == 0 || roster.IsLocked)
            {
                Debug.LogWarning("[TestSceneLauncher] Join with at least one player in the lobby first.");
                return;
            }

            // Same steps as SessionService.LaunchGame, with another scene.
            roster.SetLocked(true);
            if (!SceneFlow.LoadNetworkScene(sceneName))
            {
                roster.SetLocked(false);
                return;
            }
            Debug.Log($"[TestSceneLauncher] Loading {sceneName} for every player.");
        }

        [MenuItem(MenuPath, true)]
        private static bool LaunchValidate()
        {
            return EditorApplication.isPlaying;
        }

        private static bool IsInBuildList(string path)
        {
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && scene.path == path)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
