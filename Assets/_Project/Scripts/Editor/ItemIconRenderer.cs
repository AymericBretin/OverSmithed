using System.Collections.Generic;
using System.IO;
using OverSmithed.Items;
using OverSmithed.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Menu "OverSmithed/Render Icon Photos": takes a "photo" of every item's 3D model (same angle, light and
    /// framing for all, transparent background, dark outline like the UI kit) and uses it as the item's HUD icon.
    /// Photos are temporary icons: an item whose icon is a real sprite (any sprite outside the photo folder) is
    /// never touched. An item with no icon, or with an old photo, gets a new photo. So new items get an icon by
    /// running the menu again, and a hand-made sprite put in the Icon field replaces the photo for good.
    /// Stations work the same way, in the UI theme (keys StationAnvil…): their temporary pictogram is a photo of
    /// the tool used there (hammer, saw, wrench), until real pictograms are drawn.
    /// Rendering happens in a preview scene (in memory only): open scenes are not changed.
    /// </summary>
    public static class ItemIconRenderer
    {
        public const string PhotoFolder = "Assets/_Project/UI/Sprites/Items/Photos";

        // Rendered bigger, then shrunk 2× (smooth edges), then outlined.
        private const int RenderSize = 512;
        private const int IconSize = 256;
        // Empty space around the model, as a part of the icon size: just enough room for the outline.
        private const float Padding = 0.035f;
        // Same angle for every item: from the front, a bit to the left, looking down.
        private static readonly Vector3 CameraAngles = new(30f, -30f, 0f);
        // Long items (sword, blade…) longer than this on screen are turned diagonally, like classic game icons:
        // they fill the square instead of being a thin line.
        private const float LongItemRatio = 1.6f;
        private const float MaxDiagonalTurn = 60f;
        // Outline: thick and dark, like the UI kit icons (clock, coin…).
        private const float OutlineWidth = 7f;
        private static readonly Color OutlineColor = new(0.11f, 0.10f, 0.13f, 1f);

        // Station pictograms (what you do there). The furnace has none: KayKit has no flame, it keeps its letter
        // until its real pictogram exists.
        private static readonly (UiSprite Key, string Model)[] StationTools =
        {
            (UiSprite.StationAnvil, "Assets/ThirdParty/KayKit/RPGToolsBits/Models/hammer.fbx"),
            (UiSprite.StationWorkbench, "Assets/ThirdParty/KayKit/RPGToolsBits/Models/saw.fbx"),
            (UiSprite.StationAssemblyTable, "Assets/ThirdParty/KayKit/RPGToolsBits/Models/wrench_A.fbx"),
        };

        [MenuItem("OverSmithed/Render Icon Photos")]
        public static void RenderAll()
        {
            EnsureFolder(PhotoFolder);
            var rendered = new List<string>();
            var skipped = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ItemDefinition)))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null)
                {
                    continue;
                }
                if (!NeedsPhoto(definition.Icon))
                {
                    skipped.Add(definition.name);
                    continue;
                }
                if (definition.Prefab == null)
                {
                    Debug.LogWarning($"[ItemIconRenderer] '{definition.name}' has no prefab: no photo.", definition);
                    continue;
                }
                var path = $"{PhotoFolder}/{definition.name}.png";
                File.WriteAllBytes(path, RenderIcon(definition.Prefab).EncodeToPNG());
                ImportAsSprite(path);
                SetIcon(definition, AssetDatabase.LoadAssetAtPath<Sprite>(path));
                rendered.Add(definition.name);
            }
            RenderStationPhotos(rendered, skipped);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ItemIconRenderer] Photos: {rendered.Count} ({string.Join(", ", rendered)}). " +
                      $"Real sprites kept: {skipped.Count} ({string.Join(", ", skipped)}).");
        }

        // No icon, or an icon that is one of our photos: (re)take the photo. A real sprite is never replaced.
        private static bool NeedsPhoto(Sprite icon)
        {
            return icon == null || AssetDatabase.GetAssetPath(icon).StartsWith(PhotoFolder + "/");
        }

        // The station pictograms live in the UI theme (one sprite per UiSprite key).
        private static void RenderStationPhotos(List<string> rendered, List<string> skipped)
        {
            var themeGuids = AssetDatabase.FindAssets("t:" + nameof(UiTheme));
            if (themeGuids.Length == 0)
            {
                Debug.LogWarning("[ItemIconRenderer] No UiTheme found: no station pictograms.");
                return;
            }
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(AssetDatabase.GUIDToAssetPath(themeGuids[0]));
            var serialized = new SerializedObject(theme);
            var sprites = serialized.FindProperty("_sprites");
            foreach (var (key, modelPath) in StationTools)
            {
                SerializedProperty entry = null;
                for (var i = 0; i < sprites.arraySize; i++)
                {
                    if (sprites.GetArrayElementAtIndex(i).FindPropertyRelative("Key").intValue == (int)key)
                    {
                        entry = sprites.GetArrayElementAtIndex(i);
                        break;
                    }
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (entry == null || model == null)
                {
                    Debug.LogWarning($"[ItemIconRenderer] {key}: no theme entry or no model at {modelPath}.");
                    continue;
                }
                var spriteProperty = entry.FindPropertyRelative("Sprite");
                if (!NeedsPhoto(spriteProperty.objectReferenceValue as Sprite))
                {
                    skipped.Add(key.ToString());
                    continue;
                }
                var path = $"{PhotoFolder}/{key}.png";
                File.WriteAllBytes(path, RenderIcon(model).EncodeToPNG());
                ImportAsSprite(path);
                spriteProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                rendered.Add(key.ToString());
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);
        }

        private static Texture2D RenderIcon(GameObject prefab)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var model = (GameObject)Object.Instantiate(prefab);
                SceneManager_Move(model, scene);

                var camera = new GameObject("IconCamera").AddComponent<Camera>();
                SceneManager_Move(camera.gameObject, scene);
                camera.scene = scene;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 50f;
                camera.allowHDR = false;
                camera.transform.rotation = Quaternion.Euler(CameraAngles);

                AddLight(scene, new Vector3(50f, -40f, 0f), 1.2f, new Color(1f, 0.95f, 0.88f));   // key, warm
                AddLight(scene, new Vector3(20f, 150f, 0f), 0.55f, new Color(0.85f, 0.9f, 1f));   // fill, cool
                AddLight(scene, new Vector3(-30f, 20f, 0f), 0.35f, Color.white);                    // from below-front

                Frame(camera, model);
                // The mesh boxes are bigger than the real shape (a crystal seen from above): a first render
                // finds where the model really is in the picture, then the camera frames exactly that.
                var draft = Capture(camera, RenderSize / 2);
                FitToPixels(camera, draft);
                Object.DestroyImmediate(draft);

                var full = Capture(camera, RenderSize);
                var icon = AddOutline(Shrink(full));
                Object.DestroyImmediate(full);
                return icon;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Texture2D Capture(Camera camera, int size)
        {
            var target = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB, 8);
            camera.targetTexture = target;
            camera.Render();
            camera.Render(); // the first frame after creating objects can be incomplete

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var picture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            picture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            return picture;
        }

        // Moves and zooms the camera so the model's pixels in the draft fill the square, with the padding.
        private static void FitToPixels(Camera camera, Texture2D draft)
        {
            var size = draft.width;
            var pixels = draft.GetPixels32();
            int minX = size, minY = size, maxX = -1, maxY = -1;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (pixels[y * size + x].a > 8)
                    {
                        minX = Mathf.Min(minX, x);
                        maxX = Mathf.Max(maxX, x);
                        minY = Mathf.Min(minY, y);
                        maxY = Mathf.Max(maxY, y);
                    }
                }
            }
            if (maxX < 0)
            {
                return;
            }
            // Picture coordinates (0..1) → camera offsets: the picture is 2 × orthographicSize wide and high.
            var viewSize = camera.orthographicSize * 2f;
            var centerX = ((minX + maxX + 1) * 0.5f / size - 0.5f) * viewSize;
            var centerY = ((minY + maxY + 1) * 0.5f / size - 0.5f) * viewSize;
            var extent = Mathf.Max(maxX - minX + 1, maxY - minY + 1) / (float)size * viewSize;
            camera.transform.position += camera.transform.right * centerX + camera.transform.up * centerY;
            camera.orthographicSize = extent * 0.5f / (1f - 2f * Padding);
        }

        private static void SceneManager_Move(GameObject gameObject, UnityEngine.SceneManagement.Scene scene)
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, scene);
        }

        private static void AddLight(UnityEngine.SceneManagement.Scene scene, Vector3 angles, float intensity, Color color)
        {
            var light = new GameObject("IconLight").AddComponent<Light>();
            SceneManager_Move(light.gameObject, scene);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(angles);
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
        }

        // Points the camera at the model and zooms so the whole model fits, with the padding around it.
        private static void Frame(Camera camera, GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return;
            }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            // The model's points seen from the camera: their spread on screen gives the size to show.
            var points = ModelPoints(renderers);
            var rotation = camera.transform.rotation;
            ScreenBox(points, rotation, out var min, out var max);

            // A long item: turn the camera around its view axis until the item fills the square best.
            var width = max.x - min.x;
            var height = max.y - min.y;
            if (Mathf.Max(width, height) > LongItemRatio * Mathf.Min(width, height))
            {
                var bestSize = Mathf.Max(width, height);
                var bestRotation = rotation;
                for (var turn = -MaxDiagonalTurn; turn <= MaxDiagonalTurn; turn += 2.5f)
                {
                    var turned = rotation * Quaternion.AngleAxis(turn, Vector3.forward);
                    ScreenBox(points, turned, out var turnedMin, out var turnedMax);
                    var turnedSize = Mathf.Max(turnedMax.x - turnedMin.x, turnedMax.y - turnedMin.y);
                    if (turnedSize < bestSize - 0.001f)
                    {
                        bestSize = turnedSize;
                        bestRotation = turned;
                    }
                }
                rotation = bestRotation;
                camera.transform.rotation = rotation;
                ScreenBox(points, rotation, out min, out max);
            }

            var inverse = Quaternion.Inverse(rotation);
            var centerOnScreen = (min + max) * 0.5f;
            var size = Mathf.Max(max.x - min.x, max.y - min.y);
            camera.orthographicSize = size * 0.5f / (1f - 2f * Padding);

            // Back along the view direction from the box center, shifted so the box is centered on screen.
            var depth = (inverse * bounds.center).z;
            camera.transform.position = rotation * new Vector3(centerOnScreen.x, centerOnScreen.y, depth) -
                                        camera.transform.forward * (bounds.extents.magnitude + 5f);
        }

        // The mesh vertices in world space (tighter than the boxes, so turned items are framed well).
        private static List<Vector3> ModelPoints(Renderer[] renderers)
        {
            var points = new List<Vector3>();
            foreach (var renderer in renderers)
            {
                var mesh = renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                var bounds = mesh != null ? mesh.bounds : new Bounds(renderer.transform.InverseTransformPoint(renderer.bounds.center),
                    renderer.transform.InverseTransformVector(renderer.bounds.size));
                // The 8 corners of each mesh's own box, moved to world space: close to the real shape, and cheap.
                for (var corner = 0; corner < 8; corner++)
                {
                    var local = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    points.Add(renderer.transform.TransformPoint(local));
                }
            }
            return points;
        }

        private static void ScreenBox(List<Vector3> points, Quaternion cameraRotation, out Vector2 min, out Vector2 max)
        {
            var inverse = Quaternion.Inverse(cameraRotation);
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (var point in points)
            {
                Vector2 local = inverse * point;
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
        }

        // 2×2 average: smooth edges at the final size.
        private static Texture2D Shrink(Texture2D source)
        {
            var result = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            var pixels = source.GetPixels();
            var output = new Color[IconSize * IconSize];
            var factor = source.width / IconSize;
            for (var y = 0; y < IconSize; y++)
            {
                for (var x = 0; x < IconSize; x++)
                {
                    var sum = Color.clear;
                    for (var dy = 0; dy < factor; dy++)
                    {
                        for (var dx = 0; dx < factor; dx++)
                        {
                            var pixel = pixels[(y * factor + dy) * source.width + x * factor + dx];
                            // Premultiplied sum: transparent pixels don't darken the edge.
                            sum += new Color(pixel.r * pixel.a, pixel.g * pixel.a, pixel.b * pixel.a, pixel.a);
                        }
                    }
                    sum /= factor * factor;
                    output[y * IconSize + x] = sum.a > 0f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;
                }
            }
            result.SetPixels(output);
            result.Apply();
            return result;
        }

        // A dark band around the model: each pixel takes the strongest model alpha within OutlineWidth
        // (soft at the outer edge), then the model is drawn on top.
        private static Texture2D AddOutline(Texture2D source)
        {
            var size = source.width;
            var pixels = source.GetPixels();
            var radius = Mathf.CeilToInt(OutlineWidth + 1f);
            var offsets = new List<(int dx, int dy, float weight)>();
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var weight = Mathf.Clamp01(OutlineWidth + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    if (weight > 0f)
                    {
                        offsets.Add((dx, dy, weight));
                    }
                }
            }

            var output = new Color[pixels.Length];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var outline = 0f;
                    foreach (var (dx, dy, weight) in offsets)
                    {
                        int sx = x + dx, sy = y + dy;
                        if (sx < 0 || sy < 0 || sx >= size || sy >= size)
                        {
                            continue;
                        }
                        outline = Mathf.Max(outline, pixels[sy * size + sx].a * weight);
                        if (outline >= 1f)
                        {
                            break;
                        }
                    }
                    var model = pixels[y * size + x];
                    // Model over outline ("over" blending).
                    var alpha = model.a + outline * (1f - model.a);
                    var color = alpha > 0f
                        ? (model * model.a + OutlineColor * outline * (1f - model.a)) / alpha
                        : Color.clear;
                    color.a = alpha;
                    output[y * size + x] = color;
                }
            }
            var result = new Texture2D(size, size, TextureFormat.RGBA32, false);
            result.SetPixels(output);
            result.Apply();
            Object.DestroyImmediate(source);
            return result;
        }

        private static void ImportAsSprite(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = IconSize;
            importer.SaveAndReimport();
        }

        private static void SetIcon(ItemDefinition definition, Sprite sprite)
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_icon").objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
