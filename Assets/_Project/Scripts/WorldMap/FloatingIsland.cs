using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// A floating island of the world map (decor, above the Floating Islands level): grass on top, a rock cone under it.
    /// Editing tool: set the size and the seed, then right-click the component > "Rebuild Island" (the mesh is saved in
    /// Assets/_Project/Meshes/WorldMap/). Put the decor of the island (trees, house...) as children: they float with it.
    /// In game it bobs slowly up and down (local to each machine, no network data: it is only decor).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class FloatingIsland : MonoBehaviour
    {
        private const string MeshFolder = "Assets/_Project/Meshes/WorldMap";

        [Header("Shape")]
        [SerializeField, Min(0.3f)] private float _radius = 2.5f;
        [Tooltip("Height of the rock cone under the grass.")]
        [SerializeField, Min(0.2f)] private float _depth = 2.5f;
        [Tooltip("Height of the grass edge around the top.")]
        [SerializeField, Min(0f)] private float _grassEdge = 0.25f;
        [SerializeField, Range(6, 32)] private int _sides = 14;
        [Tooltip("0 = round, higher = more irregular outline and rocks.")]
        [SerializeField, Range(0f, 0.5f)] private float _roughness = 0.22f;
        [SerializeField] private int _seed = 1;

        [Header("Floating (in game)")]
        [SerializeField, Min(0f)] private float _bobHeight = 0.12f;
        [SerializeField, Min(0.5f)] private float _bobPeriod = 4f;

        private Vector3 _basePosition;
        private float _phase;

        private void Awake()
        {
            _basePosition = transform.localPosition;
            // Each island starts at another point of the wave, so they don't move together.
            _phase = (_seed * 0.618f) % 1f * Mathf.PI * 2f;
        }

        private void Update()
        {
            var offset = Mathf.Sin(Time.time * Mathf.PI * 2f / _bobPeriod + _phase) * _bobHeight;
            transform.localPosition = _basePosition + Vector3.up * offset;
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Island")]
        public void RebuildIsland()
        {
            var random = new System.Random(_seed);
            float Noise() => (float)random.NextDouble() * 2f - 1f;

            // Irregular outline: random radius per side, smoothed with its neighbours.
            var raw = new float[_sides];
            for (var i = 0; i < _sides; i++)
            {
                raw[i] = 1f + Noise() * _roughness;
            }
            var radii = new float[_sides];
            for (var i = 0; i < _sides; i++)
            {
                radii[i] = _radius * (raw[(i - 1 + _sides) % _sides] + 2f * raw[i] + raw[(i + 1) % _sides]) / 4f;
            }
            Vector3 Ring(int i, float scale, float y) =>
                new(Mathf.Cos(i * Mathf.PI * 2f / _sides) * radii[i] * scale, y,
                    Mathf.Sin(i * Mathf.PI * 2f / _sides) * radii[i] * scale);

            var vertices = new List<Vector3>();
            var grass = new List<int>();
            var rock = new List<int>();
            // Flat shading (low-poly look like KayKit): every triangle has its own 3 vertices.
            void Tri(List<int> list, Vector3 a, Vector3 b, Vector3 c)
            {
                list.Add(vertices.Count);
                vertices.Add(a);
                list.Add(vertices.Count);
                vertices.Add(b);
                list.Add(vertices.Count);
                vertices.Add(c);
            }
            void Quad(List<int> list, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(list, a, b, c);
                Tri(list, a, c, d);
            }

            // Top: a very low dome of grass.
            var top = Vector3.up * _radius * 0.06f;
            for (var i = 0; i < _sides; i++)
            {
                var j = (i + 1) % _sides;
                Tri(grass, top, Ring(j, 1f, 0f), Ring(i, 1f, 0f));
            }
            // Grass edge, then rock rings going down and in, to a tip.
            const int layers = 4;
            var previousScale = 1f;
            var previousY = -_grassEdge;
            for (var i = 0; i < _sides; i++)
            {
                var j = (i + 1) % _sides;
                Quad(grass, Ring(i, 1f, 0f), Ring(j, 1f, 0f), Ring(j, 1f, -_grassEdge), Ring(i, 1f, -_grassEdge));
            }
            var layerJitter = new float[layers + 1, _sides];
            for (var k = 1; k <= layers; k++)
            {
                for (var i = 0; i < _sides; i++)
                {
                    layerJitter[k, i] = 1f + Noise() * _roughness * 0.6f;
                }
            }
            for (var k = 1; k <= layers; k++)
            {
                var t = k / (float)layers;
                var scale = Mathf.Pow(1f - t, 0.75f) * 0.92f + 0.08f;
                var y = -_grassEdge - _depth * t;
                for (var i = 0; i < _sides; i++)
                {
                    var j = (i + 1) % _sides;
                    var a = Ring(i, previousScale * (k == 1 ? 0.97f : layerJitter[k - 1, i]), previousY);
                    var b = Ring(j, previousScale * (k == 1 ? 0.97f : layerJitter[k - 1, j]), previousY);
                    var c = Ring(j, scale * layerJitter[k, j], y);
                    var d = Ring(i, scale * layerJitter[k, i], y);
                    Quad(rock, a, b, c, d);
                }
                previousScale = scale;
                previousY = y;
            }
            var tip = new Vector3(Noise() * _radius * 0.1f, -_grassEdge - _depth * 1.12f, Noise() * _radius * 0.1f);
            for (var i = 0; i < _sides; i++)
            {
                var j = (i + 1) % _sides;
                Tri(rock, Ring(i, previousScale * layerJitter[layers, i], previousY),
                    Ring(j, previousScale * layerJitter[layers, j], previousY), tip);
            }

            var mesh = new Mesh { subMeshCount = 2 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(grass, 0);
            mesh.SetTriangles(rock, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = SaveMesh(mesh);
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private Mesh SaveMesh(Mesh mesh)
        {
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/_Project/Meshes"))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project", "Meshes");
            }
            if (!UnityEditor.AssetDatabase.IsValidFolder(MeshFolder))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/Meshes", "WorldMap");
            }
            var fileName = $"{gameObject.scene.name}_{name}";
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
#endif
    }
}
