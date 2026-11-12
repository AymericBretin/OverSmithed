using OverSmithed.Core;
using OverSmithed.Networking;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Debug menu "OverSmithed/Go To ..." to switch scenes in Play Mode, as the host, for every player.
    /// From the lobby it does a real launch (roster and online session locked, like the Launch button);
    /// after that it just loads the scene (e.g. end a level and go back to the world map).
    /// "Edited Scene" = the scene open in the editor when Play was pressed, to test any scene with players.
    /// Scenes must be in the build list (Netcode can only load those). A level needs a PlayerSpawner.
    /// </summary>
    [InitializeOnLoad]
    internal static class TestSceneLauncher
    {
        private const string MenuRoot = "OverSmithed/";
        private const string WorldMapMenu = MenuRoot + "Go To World Map (host)";
        private const string GameLevelMenu = MenuRoot + "Go To Level: Game (host)";
        private const string EditedSceneMenu = MenuRoot + "Go To Edited Scene (host)";
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

        [MenuItem(WorldMapMenu, priority = 100)]
        private static void GoToWorldMap()
        {
            GoTo(SceneNames.WorldMap);
        }

        [MenuItem(GameLevelMenu, priority = 101)]
        private static void GoToGameLevel()
        {
            GoTo(SceneNames.Game);
        }

        [MenuItem(EditedSceneMenu, priority = 120)]
        private static void GoToEditedScene()
        {
            var path = SessionState.GetString(EditedSceneKey, string.Empty);
            if (!IsInBuildList(path))
            {
                Debug.LogWarning($"[TestSceneLauncher] '{path}' is not in the build list: add it in Build Profiles.");
                return;
            }
            GoTo(System.IO.Path.GetFileNameWithoutExtension(path));
        }

        [MenuItem(WorldMapMenu, true)]
        [MenuItem(GameLevelMenu, true)]
        [MenuItem(EditedSceneMenu, true)]
        private static bool GoToValidate()
        {
            return EditorApplication.isPlaying && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        }

        private static void GoTo(string sceneName)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                Debug.LogWarning("[TestSceneLauncher] Only the host can change the scene. Host a game first.");
                return;
            }
            var roster = SessionRoster.Instance;
            if (roster == null || roster.Count == 0)
            {
                Debug.LogWarning("[TestSceneLauncher] Join with at least one player in the lobby first.");
                return;
            }

            // Still in the lobby: launch the game for real, straight into that scene.
            var started = roster.IsLocked
                ? SceneFlow.LoadNetworkScene(sceneName)
                : SessionService.Instance.LaunchGame(sceneName);
            if (started)
            {
                Debug.Log($"[TestSceneLauncher] Loading {sceneName} for every player.");
            }
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
