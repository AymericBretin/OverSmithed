using System.Collections.Generic;
using OverSmithed.Core;
using OverSmithed.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Helps to build levels on the 1 m grid (menu OverSmithed/Level Grid). Cells have their edges on whole meters,
    /// so a 1×1 object sits on x.5, and a 2×1 object (the grindstone) on a whole meter along its long side.
    /// - Snap Selection To Grid (Ctrl/Cmd+Alt+G): the selected objects go to the nearest cells. Their size comes from
    ///   the BoxCollider on their root (1×1 if none). Stations, counters, crates... are also turned to 0/90/180/270.
    /// - Auto Snap In Levels: in a level (a scene with a PlayArea), a station / counter / crate... (anything with an
    ///   Interactable) snaps by itself when you let go of it after moving or rotating it, or after dropping a prefab.
    /// - While one is selected, its cells are drawn on the floor: green = fine, orange = off the grid, red = another
    ///   object already uses one of these cells.
    /// - Check Level Grid: lists (and selects) every station / counter of the open level that is off the grid or
    ///   on the same cell as another one.
    /// </summary>
    [InitializeOnLoad]
    public static class LevelGrid
    {
        private const string MenuRoot = "OverSmithed/Level Grid/";
        private const string AutoSnapMenu = MenuRoot + "Auto Snap In Levels";
        private const string AutoSnapKey = "OverSmithed.LevelGrid.AutoSnap";
        private const float Cell = 1f;
        private const float Tolerance = 0.001f;

        private static readonly Color FineColor = new(0.3f, 1f, 0.4f);
        private static readonly Color OffGridColor = new(1f, 0.65f, 0.1f);
        private static readonly Color OverlapColor = new(1f, 0.2f, 0.2f);

        private static readonly Dictionary<Transform, (Vector3 position, Quaternion rotation)> DragStart = new();
        private static readonly Vector3[] Corners = new Vector3[4];

        static LevelGrid()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static bool AutoSnap
        {
            get => EditorPrefs.GetBool(AutoSnapKey, true);
            set => EditorPrefs.SetBool(AutoSnapKey, value);
        }

        // ---------- Menu ----------

        [MenuItem(MenuRoot + "Snap Selection To Grid %&g", priority = 100)]
        private static void SnapSelection()
        {
            var moved = 0;
            foreach (var target in Selection.transforms)
            {
                if (Snap(target))
                {
                    moved++;
                }
            }
            Debug.Log($"[Level Grid] {moved} of {Selection.transforms.Length} selected object(s) moved onto the grid.");
        }

        [MenuItem(MenuRoot + "Snap Selection To Grid %&g", true)]
        private static bool CanSnapSelection() => Selection.transforms.Length > 0;

        [MenuItem(AutoSnapMenu, priority = 101)]
        private static void ToggleAutoSnap()
        {
            AutoSnap = !AutoSnap;
            Debug.Log($"[Level Grid] Auto snap in levels: {(AutoSnap ? "on" : "off")}.");
        }

        [MenuItem(AutoSnapMenu, true)]
        private static bool ToggleAutoSnapCheck()
        {
            UnityEditor.Menu.SetChecked(AutoSnapMenu, AutoSnap);
            return true;
        }

        [MenuItem(MenuRoot + "Check Level Grid", priority = 102)]
        private static void CheckLevel()
        {
            var problems = new List<Object>();
            var levels = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !IsLevel(scene))
                {
                    continue;
                }
                levels++;
                var objects = GridObjects(scene);
                var owners = Occupancy(objects);
                foreach (var target in objects)
                {
                    var offGrid = !IsOnGrid(target);
                    var overlap = Overlaps(target, owners, out var other);
                    if (offGrid)
                    {
                        Debug.LogWarning($"[Level Grid] {Path(target)} is off the grid (at {target.position.x:0.###}, " +
                                         $"{target.position.z:0.###}, turned {target.eulerAngles.y:0.#}°).", target);
                    }
                    if (overlap)
                    {
                        Debug.LogWarning($"[Level Grid] {Path(target)} uses the same cell as {Path(other)}.", target);
                    }
                    if (offGrid || overlap)
                    {
                        problems.Add(target.gameObject);
                    }
                }
            }
            if (levels == 0)
            {
                Debug.Log("[Level Grid] No level open (a level is a scene with a PlayArea).");
                return;
            }
            if (problems.Count == 0)
            {
                Debug.Log("[Level Grid] All good: every station / counter is on the grid, one per cell.");
                return;
            }
            Selection.objects = problems.ToArray();
            Debug.Log($"[Level Grid] {problems.Count} object(s) to fix (now selected). Ctrl/Cmd+Alt+G snaps them.");
        }

        // ---------- Grid rules ----------

        // Stations, counters, crates, bins, the delivery point: what players use, always on the grid.
        private static bool IsGridObject(Transform target)
        {
            return target != null && target.TryGetComponent<Interactable>(out _);
        }

        private static bool IsLevel(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<PlayArea>(true) != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static List<Transform> GridObjects(Scene scene)
        {
            var list = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var interactable in root.GetComponentsInChildren<Interactable>(false))
                {
                    list.Add(interactable.transform);
                }
            }
            return list;
        }

        private static float SnappedYaw(Transform target)
        {
            return Mathf.Repeat(Mathf.Round(target.eulerAngles.y / 90f) * 90f, 360f);
        }

        // Size in cells (x, z) once turned by yaw, from the BoxCollider on the root.
        private static Vector2Int Footprint(Transform target, float yaw)
        {
            var size = Vector2.one * Cell;
            if (target.TryGetComponent<BoxCollider>(out var box))
            {
                var scaled = Vector3.Scale(box.size, target.lossyScale);
                size = new Vector2(Mathf.Abs(scaled.x), Mathf.Abs(scaled.z));
            }
            var width = Mathf.Max(1, Mathf.RoundToInt(size.x / Cell));
            var depth = Mathf.Max(1, Mathf.RoundToInt(size.y / Cell));
            var quarterTurns = ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;
            return quarterTurns % 2 == 1 ? new Vector2Int(depth, width) : new Vector2Int(width, depth);
        }

        // An odd number of cells is centered on a cell (x.5), an even number on a cell edge (a whole meter).
        private static float SnapAxis(float value, int cells)
        {
            return cells % 2 == 1 ? Mathf.Floor(value / Cell) * Cell + Cell * 0.5f : Mathf.Round(value / Cell) * Cell;
        }

        private static void Target(Transform target, out Vector3 position, out Quaternion rotation, out Vector2Int cells)
        {
            var gridObject = IsGridObject(target);
            var yaw = gridObject ? SnappedYaw(target) : target.eulerAngles.y;
            rotation = gridObject ? Quaternion.Euler(0f, yaw, 0f) : target.rotation;
            cells = Footprint(target, yaw);
            var current = target.position;
            position = new Vector3(SnapAxis(current.x, cells.x), current.y, SnapAxis(current.z, cells.y));
        }

        private static bool IsOnGrid(Transform target)
        {
            Target(target, out var position, out var rotation, out _);
            return (target.position - position).sqrMagnitude < Tolerance * Tolerance
                   && Quaternion.Angle(target.rotation, rotation) < 0.1f;
        }

        /// <summary>Moves (and for stations / counters, turns) <paramref name="target"/> onto the grid. Undo works.</summary>
        private static bool Snap(Transform target)
        {
            if (target == null || IsOnGrid(target))
            {
                return false;
            }
            Target(target, out var position, out var rotation, out _);
            Undo.RecordObject(target, "Snap To Grid");
            target.SetPositionAndRotation(position, rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            return true;
        }

        // The cells (whole-meter corner indices) an object uses once on the grid.
        private static IEnumerable<Vector2Int> Cells(Transform target)
        {
            Target(target, out var position, out _, out var cells);
            var minX = Mathf.RoundToInt(position.x / Cell - cells.x * 0.5f);
            var minZ = Mathf.RoundToInt(position.z / Cell - cells.y * 0.5f);
            for (var x = 0; x < cells.x; x++)
            {
                for (var z = 0; z < cells.y; z++)
                {
                    yield return new Vector2Int(minX + x, minZ + z);
                }
            }
        }

        private static Dictionary<Vector2Int, List<Transform>> Occupancy(List<Transform> objects)
        {
            var owners = new Dictionary<Vector2Int, List<Transform>>();
            foreach (var target in objects)
            {
                foreach (var cell in Cells(target))
                {
                    if (!owners.TryGetValue(cell, out var list))
                    {
                        owners[cell] = list = new List<Transform>();
                    }
                    list.Add(target);
                }
            }
            return owners;
        }

        private static bool Overlaps(Transform target, Dictionary<Vector2Int, List<Transform>> owners, out Transform other)
        {
            foreach (var cell in Cells(target))
            {
                if (!owners.TryGetValue(cell, out var list))
                {
                    continue;
                }
                foreach (var owner in list)
                {
                    if (owner != target)
                    {
                        other = owner;
                        return true;
                    }
                }
            }
            other = null;
            return false;
        }

        private static string Path(Transform target)
        {
            return target.parent != null ? $"{target.parent.name}/{target.name}" : target.name;
        }

        // ---------- Scene view ----------

        private static void OnSceneGui(SceneView view)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return;
            }
            var current = Event.current;
            switch (current.rawType)
            {
                case EventType.MouseDown when current.button == 0:
                    DragStart.Clear();
                    foreach (var target in Selection.transforms)
                    {
                        DragStart[target] = (target.position, target.rotation);
                    }
                    break;
                case EventType.MouseUp when current.button == 0:
                    if (AutoSnap)
                    {
                        var moved = new List<Transform>();
                        foreach (var pair in DragStart)
                        {
                            var target = pair.Key;
                            if (target != null && (target.position != pair.Value.position
                                                   || target.rotation != pair.Value.rotation))
                            {
                                moved.Add(target);
                            }
                        }
                        // After the move tool has finished its own undo step.
                        EditorApplication.delayCall += () => AutoSnapAll(moved);
                    }
                    DragStart.Clear();
                    break;
                case EventType.DragPerform:
                    if (AutoSnap)
                    {
                        // A prefab dropped in the scene is selected once the drop is done.
                        EditorApplication.delayCall += () => AutoSnapAll(new List<Transform>(Selection.transforms));
                    }
                    break;
                case EventType.Repaint:
                    DrawSelection();
                    break;
            }
        }

        private static void AutoSnapAll(List<Transform> targets)
        {
            foreach (var target in targets)
            {
                if (target != null && IsGridObject(target) && IsLevel(target.gameObject.scene))
                {
                    Snap(target);
                }
            }
        }

        private static void DrawSelection()
        {
            Dictionary<Vector2Int, List<Transform>> owners = null;
            foreach (var target in Selection.transforms)
            {
                if (!IsGridObject(target) || !IsLevel(target.gameObject.scene))
                {
                    continue;
                }
                owners ??= Occupancy(GridObjects(target.gameObject.scene));
                var color = Overlaps(target, owners, out _) ? OverlapColor
                    : IsOnGrid(target) ? FineColor : OffGridColor;
                var y = target.position.y + 0.02f;
                foreach (var cell in Cells(target))
                {
                    var x0 = cell.x * Cell + 0.04f;
                    var z0 = cell.y * Cell + 0.04f;
                    var x1 = (cell.x + 1) * Cell - 0.04f;
                    var z1 = (cell.y + 1) * Cell - 0.04f;
                    Corners[0] = new Vector3(x0, y, z0);
                    Corners[1] = new Vector3(x0, y, z1);
                    Corners[2] = new Vector3(x1, y, z1);
                    Corners[3] = new Vector3(x1, y, z0);
                    Handles.DrawSolidRectangleWithOutline(Corners, new Color(color.r, color.g, color.b, 0.18f), color);
                }
            }
        }
    }
}
