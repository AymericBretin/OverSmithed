using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// A winding path between two level spots, drawn as a line of stepping stones (like the dotted paths of
    /// Mario world maps). Editing tool only: move the bend points (children of "Bends"), then right-click the
    /// component > "Rebuild Stones" to place the stones again along a smooth curve (none in the skip areas, e.g. on
    /// water where a bridge crosses). The stones are saved in the scene; nothing runs in game.
    /// </summary>
    public class MapPath : MonoBehaviour
    {
        [SerializeField] private LevelNode _from;
        [SerializeField] private LevelNode _to;
        [Tooltip("Points the path goes through between the two spots, in order. Move them, then Rebuild Stones.")]
        [SerializeField] private List<Transform> _bends = new();
        [SerializeField] private GameObject _stonePrefab;
        [Tooltip("The stones are created under this transform (everything under it is replaced on rebuild).")]
        [SerializeField] private Transform _stones;
        [Tooltip("Distance (m) between two stones, along the path.")]
        [SerializeField, Min(0.2f)] private float _spacing = 0.9f;
        [Tooltip("No stone inside these areas (water: a bridge crosses it instead)...")]
        [SerializeField] private List<MapShape> _skipAreas = new();
        [Tooltip("...except inside these ones (an island in the lake).")]
        [SerializeField] private List<MapShape> _keepAreas = new();

        // Curve through the spot centers and the bends (Catmull-Rom), as a list of points close together.
        private List<Vector3> SampleCurve()
        {
            var points = new List<Vector3>();
            if (_from == null || _to == null)
            {
                return points;
            }
            var controls = new List<Vector3> { _from.transform.position };
            foreach (var bend in _bends)
            {
                if (bend != null)
                {
                    controls.Add(bend.position);
                }
            }
            controls.Add(_to.transform.position);

            for (var i = 0; i < controls.Count - 1; i++)
            {
                var p0 = controls[Mathf.Max(i - 1, 0)];
                var p1 = controls[i];
                var p2 = controls[i + 1];
                var p3 = controls[Mathf.Min(i + 2, controls.Count - 1)];
                const int steps = 24;
                for (var s = 0; s < steps; s++)
                {
                    points.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
                }
            }
            points.Add(controls[^1]);
            for (var i = 0; i < points.Count; i++)
            {
                points[i] = new Vector3(points[i].x, 0f, points[i].z);
            }
            return points;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                           (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Stones")]
        public void RebuildStones()
        {
            if (_stones == null || _stonePrefab == null)
            {
                Debug.LogWarning("[MapPath] Set Stones and Stone Prefab first.", this);
                return;
            }
            for (var i = _stones.childCount - 1; i >= 0; i--)
            {
                UnityEditor.Undo.DestroyObjectImmediate(_stones.GetChild(i).gameObject);
            }

            var curve = SampleCurve();
            if (curve.Count < 2)
            {
                return;
            }
            // Walk along the curve and drop a stone every _spacing meters, outside the two circles.
            var travelled = 0f;
            var nextStone = _spacing * 0.5f;
            for (var i = 1; i < curve.Count; i++)
            {
                var a = curve[i - 1];
                var b = curve[i];
                var length = Vector3.Distance(a, b);
                while (nextStone <= travelled + length)
                {
                    var point = Vector3.Lerp(a, b, (nextStone - travelled) / Mathf.Max(length, 0.0001f));
                    if (!_from.Contains(point) && !_to.Contains(point) && !IsSkipped(point))
                    {
                        var stone = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(_stonePrefab, _stones);
                        stone.transform.SetPositionAndRotation(point, Quaternion.LookRotation(b - a));
                        UnityEditor.Undo.RegisterCreatedObjectUndo(stone, "Rebuild Stones");
                    }
                    nextStone += _spacing;
                }
                travelled += length;
            }
            UnityEditor.EditorUtility.SetDirty(gameObject);
        }

        private bool IsSkipped(Vector3 point)
        {
            foreach (var keep in _keepAreas)
            {
                if (keep != null && keep.Contains(point))
                {
                    return false;
                }
            }
            foreach (var skip in _skipAreas)
            {
                if (skip != null && skip.Contains(point))
                {
                    return true;
                }
            }
            return false;
        }

        private void OnDrawGizmos()
        {
            var curve = SampleCurve();
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.8f);
            for (var i = 1; i < curve.Count; i++)
            {
                Gizmos.DrawLine(curve[i - 1] + Vector3.up * 0.1f, curve[i] + Vector3.up * 0.1f);
            }
            foreach (var bend in _bends)
            {
                if (bend != null)
                {
                    Gizmos.DrawWireSphere(bend.position, 0.3f);
                }
            }
        }
#endif
    }
}
