using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// A flat area of free shape on the world map (lake, island, village dirt, castle paving), drawn just above the
    /// ground. Editing tool only, like <see cref="MapPath"/>: move the outline points (children of "Points", in order
    /// around the shape), then right-click the component > "Rebuild Shape". It builds:
    /// the flat surface (child "Fill"), an optional band around it (child "Rim", e.g. the shore of a lake) and, if
    /// <see cref="BlocksWalker"/>, invisible walls along the outline (child "Blocker", a MeshCollider) so the world map
    /// walker can't go in (water) or can't leave (an island). The walls have openings at the "gaps" (bridge ends).
    /// The meshes are saved as assets in Assets/_Project/Meshes/WorldMap/; nothing runs in game except
    /// <see cref="Contains"/>.
    /// </summary>
    public class MapShape : MonoBehaviour
    {
        private const string MeshFolder = "Assets/_Project/Meshes/WorldMap";

        [Tooltip("Outline points, in order around the shape (the curve goes smoothly through them).")]
        [SerializeField] private Transform _pointsRoot;
        [SerializeField, Range(2, 24)] private int _samplesPerPoint = 8;
        [Tooltip("Height of the flat surface above the ground (a few cm, so it never flickers with the ground).")]
        [SerializeField] private float _height = 0.02f;
        [SerializeField] private MeshFilter _fill;

        [Header("Rim (band around the shape)")]
        [Tooltip("0 = no rim.")]
        [SerializeField, Min(0f)] private float _rimWidth;
        [SerializeField] private float _rimHeight = 0.015f;
        [SerializeField] private MeshFilter _rim;

        [Header("Invisible walls for the walker")]
        [SerializeField] private bool _blocksWalker;
        [SerializeField, Min(0.1f)] private float _blockerHeight = 2f;
        [Tooltip("The walls are moved this far outside the outline (+) or inside it (-).")]
        [SerializeField] private float _blockerOffset;
        [Tooltip("No wall within Gap Radius of these points (where a bridge or a path goes in).")]
        [SerializeField] private List<Transform> _gaps = new();
        [SerializeField, Min(0f)] private float _gapRadius = 1.2f;
        [Tooltip("No wall under these boxes either (the walkway colliders of the bridges): works whatever the bridge angle.")]
        [SerializeField] private List<BoxCollider> _gapBoxes = new();
        [SerializeField] private MeshCollider _blocker;

        private List<Vector2> _outline;

        public bool BlocksWalker => _blocksWalker;

        /// <summary>Is <paramref name="worldPoint"/> inside the shape (seen from above)?</summary>
        public bool Contains(Vector3 worldPoint)
        {
            _outline ??= BuildOutline();
            var p = new Vector2(worldPoint.x, worldPoint.z);
            var inside = false;
            for (int i = 0, j = _outline.Count - 1; i < _outline.Count; j = i++)
            {
                var a = _outline[i];
                var b = _outline[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        // Closed smooth curve (Catmull-Rom) through the points, in world X/Z, counter-clockwise seen from above.
        private List<Vector2> BuildOutline()
        {
            var controls = new List<Vector2>();
            if (_pointsRoot != null)
            {
                foreach (Transform point in _pointsRoot)
                {
                    controls.Add(new Vector2(point.position.x, point.position.z));
                }
            }
            var outline = new List<Vector2>();
            var count = controls.Count;
            if (count < 3)
            {
                return outline;
            }
            for (var i = 0; i < count; i++)
            {
                var p0 = controls[(i - 1 + count) % count];
                var p1 = controls[i];
                var p2 = controls[(i + 1) % count];
                var p3 = controls[(i + 2) % count];
                for (var s = 0; s < _samplesPerPoint; s++)
                {
                    var t = s / (float)_samplesPerPoint;
                    var t2 = t * t;
                    var t3 = t2 * t;
                    outline.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                                        (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            if (SignedArea(outline) < 0f)
            {
                outline.Reverse();
            }
            return outline;
        }

        private static float SignedArea(List<Vector2> polygon)
        {
            var area = 0f;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                area += polygon[j].x * polygon[i].y - polygon[i].x * polygon[j].y;
            }
            return area * 0.5f;
        }

        // Each outline point pushed out by `distance` along the mean of its two edge normals.
        private static List<Vector2> Offset(List<Vector2> polygon, float distance)
        {
            var result = new List<Vector2>(polygon.Count);
            for (var i = 0; i < polygon.Count; i++)
            {
                var prev = polygon[(i - 1 + polygon.Count) % polygon.Count];
                var next = polygon[(i + 1) % polygon.Count];
                var tangent = (next - prev).normalized;
                // Counter-clockwise: the outside is on the right of the direction of travel.
                var normal = new Vector2(tangent.y, -tangent.x);
                result.Add(polygon[i] + normal * distance);
            }
            return result;
        }

#if UNITY_EDITOR
        /// <summary>Editor: makes sure the walls open under <paramref name="box"/> (and forgets deleted boxes).</summary>
        public void EnsureGapBox(BoxCollider box)
        {
            UnityEditor.Undo.RecordObject(this, "Gap Box");
            _gapBoxes.RemoveAll(existing => existing == null);
            if (box != null && !_gapBoxes.Contains(box))
            {
                _gapBoxes.Add(box);
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Rebuild Shape")]
        public void RebuildShape()
        {
            _outline = BuildOutline();
            if (_outline.Count < 3)
            {
                Debug.LogWarning("[MapShape] Needs at least 3 points under Points.", this);
                return;
            }
            UnityEditor.Undo.RecordObject(this, "Rebuild Shape");

            if (_fill != null)
            {
                _fill.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _fill.transform.localScale = Vector3.one;
                _fill.sharedMesh = SaveMesh(BuildFillMesh(_outline, _height), "Fill");
            }
            if (_rim != null)
            {
                _rim.gameObject.SetActive(_rimWidth > 0f);
                if (_rimWidth > 0f)
                {
                    _rim.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    _rim.transform.localScale = Vector3.one;
                    _rim.sharedMesh = SaveMesh(BuildBandMesh(_outline, Offset(_outline, _rimWidth), _rimHeight), "Rim");
                }
            }
            if (_blocker != null)
            {
                _blocker.gameObject.SetActive(_blocksWalker);
                if (_blocksWalker)
                {
                    _blocker.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    _blocker.transform.localScale = Vector3.one;
                    _blocker.sharedMesh = SaveMesh(BuildWallMesh(Offset(_outline, _blockerOffset)), "Blocker");
                }
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }

        // Flat polygon, triangulated by ear clipping (the outline may be concave).
        private static Mesh BuildFillMesh(List<Vector2> outline, float height)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            foreach (var p in outline)
            {
                vertices.Add(new Vector3(p.x, height, p.y));
                uvs.Add(p * 0.25f);
            }
            var triangles = new List<int>();
            var remaining = new List<int>();
            for (var i = 0; i < outline.Count; i++)
            {
                remaining.Add(i);
            }
            var guard = 0;
            while (remaining.Count > 3 && guard++ < 10000)
            {
                var clipped = false;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var ia = remaining[(i - 1 + remaining.Count) % remaining.Count];
                    var ib = remaining[i];
                    var ic = remaining[(i + 1) % remaining.Count];
                    var a = outline[ia];
                    var b = outline[ib];
                    var c = outline[ic];
                    if (Cross(b - a, c - b) <= 0f)
                    {
                        continue; // reflex corner
                    }
                    var anyInside = false;
                    foreach (var other in remaining)
                    {
                        if (other != ia && other != ib && other != ic && InTriangle(outline[other], a, b, c))
                        {
                            anyInside = true;
                            break;
                        }
                    }
                    if (anyInside)
                    {
                        continue;
                    }
                    // Counter-clockwise from above = clockwise in Unity's winding for an upward face.
                    triangles.Add(ia);
                    triangles.Add(ic);
                    triangles.Add(ib);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    break;
                }
            }
            if (remaining.Count == 3)
            {
                triangles.Add(remaining[0]);
                triangles.Add(remaining[2]);
                triangles.Add(remaining[1]);
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Flat band between two outlines of the same length (inner = the shape, outer = pushed out).
        private static Mesh BuildBandMesh(List<Vector2> inner, List<Vector2> outer, float height)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var count = inner.Count;
            for (var i = 0; i < count; i++)
            {
                vertices.Add(new Vector3(inner[i].x, height, inner[i].y));
                vertices.Add(new Vector3(outer[i].x, height, outer[i].y));
                uvs.Add(inner[i] * 0.25f);
                uvs.Add(outer[i] * 0.25f);
            }
            for (var i = 0; i < count; i++)
            {
                var a = i * 2;
                var b = (i + 1) % count * 2;
                // Same winding as the fill (faces up).
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            // A band can come out facing down depending on the order: make sure it faces up.
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
            {
                for (var t = 0; t < triangles.Count; t += 3)
                {
                    (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
                }
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        // Vertical walls along the outline (both sides), except near the gaps.
        private Mesh BuildWallMesh(List<Vector2> outline)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                if (NearGap(a) || NearGap(b))
                {
                    continue;
                }
                var start = vertices.Count;
                vertices.Add(new Vector3(a.x, -0.5f, a.y));
                vertices.Add(new Vector3(b.x, -0.5f, b.y));
                vertices.Add(new Vector3(a.x, _blockerHeight, a.y));
                vertices.Add(new Vector3(b.x, _blockerHeight, b.y));
                triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
                triangles.AddRange(new[] { start, start + 1, start + 2, start + 1, start + 3, start + 2 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private bool NearGap(Vector2 point)
        {
            foreach (var gap in _gaps)
            {
                if (gap != null && Vector2.Distance(point, new Vector2(gap.position.x, gap.position.z)) < _gapRadius)
                {
                    return true;
                }
            }
            foreach (var box in _gapBoxes)
            {
                if (box == null)
                {
                    continue;
                }
                // Seen from above: inside the box footprint (a little wider, up to the bridge rails).
                var world = new Vector3(point.x, box.bounds.center.y, point.y);
                var local = box.transform.InverseTransformPoint(world) - box.center;
                if (Mathf.Abs(local.x) < box.size.x * 0.5f + 0.08f && Mathf.Abs(local.z) < box.size.z * 0.5f)
                {
                    return true;
                }
            }
            return false;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Cross(b - a, p - a);
            var d2 = Cross(c - b, p - b);
            var d3 = Cross(a - c, p - c);
            return d1 >= 0f && d2 >= 0f && d3 >= 0f;
        }

        // One mesh asset per shape and part: Meshes/WorldMap/<scene>_<object>_<part>.asset (replaced on rebuild).
        private Mesh SaveMesh(Mesh mesh, string part)
        {
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/_Project/Meshes"))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project", "Meshes");
            }
            if (!UnityEditor.AssetDatabase.IsValidFolder(MeshFolder))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/Meshes", "WorldMap");
            }
            var fileName = $"{gameObject.scene.name}_{name}_{part}";
            var path = $"{MeshFolder}/{fileName}.asset";
            mesh.name = fileName;
            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                UnityEditor.EditorUtility.CopySerialized(mesh, existing);
                existing.name = fileName;
                UnityEditor.EditorUtility.SetDirty(existing);
                DestroyImmediate(mesh);
                return existing;
            }
            UnityEditor.AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private void OnValidate()
        {
            _outline = null;
        }

        private void OnDrawGizmosSelected()
        {
            var outline = BuildOutline();
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            for (var i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                Gizmos.DrawLine(new Vector3(a.x, _height + 0.1f, a.y), new Vector3(b.x, _height + 0.1f, b.y));
            }
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
            foreach (var gap in _gaps)
            {
                if (gap != null)
                {
                    Gizmos.DrawWireSphere(gap.position, _gapRadius);
                }
            }
        }
#endif
    }
}
