using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// A bridge of the world map (KayKit bridge model, long side = local Z). Editing tool: after moving, turning or
    /// scaling the bridge, right-click the component > "Fit To Water". It finds where the bridge crosses the water,
    /// puts the two gap markers there (openings in the water walls of <see cref="MapShape"/>), fits the invisible
    /// rails to that crossing, puts the deck 10 cm above the ground and rebuilds the water walls.
    /// It warns when an end of the bridge is still in the water (the walker could not get off).
    /// </summary>
    public class MapBridge : MonoBehaviour
    {
        // KayKit bridge_A: deck top at local y 0.2, half length 0.96, rails at local x +-0.45.
        private const float DeckTop = 0.2f;
        private const float HalfLength = 0.96f;
        private const float RailX = 0.45f;

        [Tooltip("The water the bridge crosses (lake, stream).")]
        [SerializeField] private MapShape _water;
        [Tooltip("Land inside the water (an island): not water.")]
        [SerializeField] private List<MapShape> _landInWater = new();
        [Tooltip("Shapes whose walls use the gap markers: rebuilt after fitting.")]
        [SerializeField] private List<MapShape> _shapesToRebuild = new();
        [SerializeField] private Transform _gapA;
        [SerializeField] private Transform _gapB;
        [SerializeField] private BoxCollider _walkway;
        [SerializeField] private BoxCollider _railLeft;
        [SerializeField] private BoxCollider _railRight;
        [Tooltip("The rails go this far (m) past the water on each side.")]
        [SerializeField, Min(0f)] private float _railMargin = 0.4f;
        [Tooltip("Height of the deck above the ground (a small step the walker can climb).")]
        [SerializeField, Min(0f)] private float _deckAboveGround = 0.1f;

        private bool IsWater(Vector3 point)
        {
            if (_water == null || !_water.Contains(point))
            {
                return false;
            }
            foreach (var land in _landInWater)
            {
                if (land != null && land.Contains(point))
                {
                    return false;
                }
            }
            return true;
        }

#if UNITY_EDITOR
        [ContextMenu("Fit To Water")]
        public void FitToWater()
        {
            UnityEditor.Undo.RecordObjects(new Object[] { transform, _gapA, _gapB, _walkway, _railLeft, _railRight }, "Fit Bridge");
            var scale = transform.localScale.x;
            var position = transform.position;
            position.y = _deckAboveGround - DeckTop * scale;
            transform.position = position;

            // Walk along the bridge axis: first and last point in the water.
            var center = new Vector3(position.x, 0f, position.z);
            var axis = transform.forward;
            axis.y = 0f;
            axis.Normalize();
            var half = HalfLength * scale;
            float? first = null;
            float last = 0f;
            for (var t = -half; t <= half; t += 0.05f)
            {
                if (IsWater(center + axis * t))
                {
                    first ??= t;
                    last = t;
                }
            }
            if (first == null)
            {
                Debug.LogWarning($"[MapBridge] {name} does not cross any water.", this);
                return;
            }
            if (first.Value <= -half + 0.1f || last >= half - 0.1f)
            {
                Debug.LogWarning($"[MapBridge] An end of {name} is in the water: move it or make it longer, then Fit To Water again.", this);
            }
            _gapA.position = center + axis * first.Value;
            _gapB.position = center + axis * last;

            _walkway.center = new Vector3(0f, DeckTop - 0.05f, 0f);
            _walkway.size = new Vector3(RailX * 2f - 0.15f, 0.1f, HalfLength * 2f);
            var railCenter = (first.Value + last) * 0.5f / scale;
            var railLength = Mathf.Min(HalfLength * 2f, (last - first.Value + _railMargin * 2f) / scale);
            foreach (var (rail, side) in new[] { (_railLeft, -1f), (_railRight, 1f) })
            {
                rail.center = new Vector3(side * RailX, DeckTop + 0.75f / scale, railCenter);
                rail.size = new Vector3(0.08f, 1.5f / scale, railLength);
            }
            foreach (var shape in _shapesToRebuild)
            {
                if (shape != null)
                {
                    shape.RebuildShape();
                }
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private void OnDrawGizmosSelected()
        {
            if (_gapA != null && _gapB != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
                Gizmos.DrawLine(_gapA.position + Vector3.up * 0.3f, _gapB.position + Vector3.up * 0.3f);
                Gizmos.DrawWireSphere(_gapA.position, 0.2f);
                Gizmos.DrawWireSphere(_gapB.position, 0.2f);
            }
        }
#endif
    }
}
