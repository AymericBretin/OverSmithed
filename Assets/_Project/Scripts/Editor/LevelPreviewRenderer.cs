using System.Collections.Generic;
using System.IO;
using OverSmithed.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Menu "OverSmithed/Render Level Previews": takes a photo of every built level (LevelDefinition with a scene) from
    /// its game camera, for the world map popup (UI-12). The photo goes to <see cref="PhotoFolder"/> and becomes the
    /// level's preview. A drawn picture (any sprite outside the photo folder) is never replaced. The level scenes are
    /// opened one by one (real light, fog, sky and post-processing), then the scenes that were open come back.
    /// Unsaved scene changes are asked about first.
    /// </summary>
    public static class LevelPreviewRenderer
    {
        public const string PhotoFolder = "Assets/_Project/UI/Sprites/LevelPreviews/Photos";
        private const int Width = 640;
        private const int Height = 360;

        [MenuItem("OverSmithed/Render Level Previews")]
        public static void RenderAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[LevelPreviewRenderer] Stop Play Mode first.");
                return;
            }
            // Asset paths, not objects: opening a scene unloads the assets nothing uses (the definitions too).
            var levels = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(LevelDefinition)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                if (level != null && level.IsBuilt && CanReplace(level.Preview))
                {
                    levels.Add(path);
                }
            }
            if (levels.Count == 0)
            {
                Debug.Log("[LevelPreviewRenderer] Nothing to do (no built level without a drawn picture).");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            var rendered = new List<string>();
            try
            {
                EnsureFolder(PhotoFolder);
                foreach (var levelPath in levels)
                {
                    var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath);
                    var levelName = level.name;
                    var sceneName = level.SceneName;
                    var scenePath = FindScenePath(sceneName);
                    if (scenePath == null)
                    {
                        Debug.LogWarning($"[LevelPreviewRenderer] '{levelName}': scene '{sceneName}' not found.", level);
                        continue;
                    }
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var camera = FindCamera(scene);
                    if (camera == null)
                    {
                        Debug.LogWarning($"[LevelPreviewRenderer] '{sceneName}': no camera.");
                        continue;
                    }
                    var path = $"{PhotoFolder}/{levelName}.png";
                    var photo = Render(camera);
                    File.WriteAllBytes(path, photo.EncodeToPNG());
                    Object.DestroyImmediate(photo);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    ImportAsSprite(path);
                    AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath)
                        .EditorSetPreview(AssetDatabase.LoadAssetAtPath<Sprite>(path));
                    rendered.Add(levelName);
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                // The level scenes were only looked at: back to what was open, without saving them.
                if (setup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }
            Debug.Log($"[LevelPreviewRenderer] Photos: {rendered.Count} ({string.Join(", ", rendered)}).");
        }

        // Empty, or a photo made by this tool: may be (re)made. Anything else is a drawn picture: kept.
        private static bool CanReplace(Sprite preview)
        {
            return preview == null || AssetDatabase.GetAssetPath(preview).StartsWith(PhotoFolder + "/");
        }

        private static string FindScenePath(string sceneName)
        {
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (Path.GetFileNameWithoutExtension(scene.path) == sceneName)
                {
                    return scene.path;
                }
            }
            return null;
        }

        private static Camera FindCamera(UnityEngine.SceneManagement.Scene scene)
        {
            Camera first = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var camera in root.GetComponentsInChildren<Camera>())
                {
                    if (camera.CompareTag("MainCamera"))
                    {
                        return camera;
                    }
                    first ??= camera;
                }
            }
            return first;
        }

        // The game camera as players see it at the start, without the world-space UI (station bars...).
        private static Texture2D Render(Camera camera)
        {
            var big = new RenderTexture(Width * 2, Height * 2, 24) { antiAliasing = 8 };
            var small = new RenderTexture(Width, Height, 0);
            var oldTarget = camera.targetTexture;
            var oldMask = camera.cullingMask;
            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                camera.cullingMask &= ~(1 << uiLayer);
            }
            camera.targetTexture = big;
            // Warm-up render: the first one after opening a scene can be wrong.
            camera.Render();
            camera.Render();
            camera.targetTexture = oldTarget;
            camera.cullingMask = oldMask;

            var previous = RenderTexture.active;
            Graphics.Blit(big, small);
            RenderTexture.active = small;
            var photo = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            photo.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            photo.Apply();
            RenderTexture.active = previous;
            big.Release();
            small.Release();
            Object.DestroyImmediate(big);
            Object.DestroyImmediate(small);
            return photo;
        }

        private static void ImportAsSprite(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
