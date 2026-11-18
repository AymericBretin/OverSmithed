using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// A bridge of the world map (KayKit hex pack "building_bridge_A"). Editing tool: after moving, turning or scaling
    /// the bridge, right-click the component > "Fit To Water". It puts the bridge on the ground, builds its invisible
    /// colliders (children of "Collision"), puts the two gap markers where it crosses the water (openings in the water
    /// walls of <see cref="MapShape"/>) and rebuilds those walls.
    /// The KayKit model is drawn for hex tiles: its deck is turned -30 degrees from the local Z axis, it is an arch
    /// (top 0.2 in the middle, 0 at the ends) and narrow (deck 0.22 between two low rails at +-0.19, model units).
    /// The colliders follow that: a flat middle, two ramps, two tall rails just outside the visible ones.
    /// Keep the scale uniform (same X, Y, Z): this diagonal model is distorted by a non-uniform scale.
    /// </summary>
    public class MapBridge : MonoBehaviour
    {
        // KayKit bridge_A, model units.
        private const float ModelAngle = -30f;
        private const float DeckTop = 0.2f;
        private const float FlatHalf = 0.4f;
        private const float EndT = 0.95f;
        private const float EndHeight = -0.03f;
        private const float RailX = 0.2f;
        private const float DeckAboveWater = 0.85f; // the deck is above the water up to here (|t|)

        [Tooltip("The water the bridge crosses (lake, stream).")]
        [SerializeField] private MapShape _water;
        [Tooltip("Land inside the water (an island): not water.")]
        [SerializeField] private List<MapShape> _landInWater = new();
        [Tooltip("Shapes whose walls must open under the bridge: rebuilt after fitting.")]
        [SerializeField] private List<MapShape> _shapesToRebuild = new();
        [SerializeField] private Transform _gapA;
        [SerializeField] private Transform _gapB;
        [Tooltip("Height (m) of the invisible rails above the deck.")]
        [SerializeField, Min(0.5f)] private float _railHeight = 1.5f;

        /// <summary>Walking direction of the bridge (world, flat).</summary>
        public Vector3 Axis
        {
            get
            {
                var axis = transform.rotation * Quaternion.Euler(0f, ModelAngle, 0f) * Vector3.forward;
                axis.y = 0f;
                return axis.normalized;
            }
        }

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
            var scale = transform.localScale;
            if (Mathf.Abs(scale.x - scale.y) > 0.01f || Mathf.Abs(scale.x - scale.z) > 0.01f)
            {
                Debug.LogWarning($"[MapBridge] {name} has a non-uniform scale {scale}: this diagonal model gets distorted " +
                                 "and the colliders won't match. Use the same X, Y and Z scale.", this);
            }
            UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Fit Bridge");

            // The arch ends at the ground.
            var position = transform.position;
            position.y = 0f;
            transform.position = position;

            // Old colliders on the root (first version of this tool) are replaced by the children of "Collision".
            foreach (var old in GetComponents<BoxCollider>())
            {
                DestroyImmediate(old);
            }
            var collision = Child(transform, "Collision");
            collision.localPosition = Vector3.zero;
            collision.localRotation = Quaternion.Euler(0f, ModelAngle, 0f);
            collision.localScale = Vector3.one;

            var thickness = 0.1f;
            Box(collision, "Deck", new Vector3(0f, DeckTop - thickness * 0.5f, 0f), Vector3.zero,
                new Vector3(RailX * 2f, thickness, FlatHalf * 2f), false);
            var rampRun = EndT - FlatHalf;
            var rampDrop = DeckTop - EndHeight;
            var rampLength = Mathf.Sqrt(rampRun * rampRun + rampDrop * rampDrop);
            var rampAngle = Mathf.Atan2(rampDrop, rampRun) * Mathf.Rad2Deg;
            var rampMidT = (FlatHalf + EndT) * 0.5f;
            var rampMidH = (DeckTop + EndHeight) * 0.5f - thickness * 0.5f;
            Box(collision, "RampA", new Vector3(0f, rampMidH, rampMidT), new Vector3(rampAngle, 0f, 0f),
                new Vector3(RailX * 2f, thickness, rampLength), false);
            Box(collision, "RampB", new Vector3(0f, rampMidH, -rampMidT), new Vector3(-rampAngle, 0f, 0f),
                new Vector3(RailX * 2f, thickness, rampLength), false);
            var railSize = new Vector3(0.04f, _railHeight / scale.x + DeckTop - EndHeight, EndT * 2f);
            var railY = EndHeight + railSize.y * 0.5f;
            Box(collision, "RailLeft", new Vector3(-RailX - 0.02f, railY, 0f), Vector3.zero, railSize, false);
            Box(collision, "RailRight", new Vector3(RailX + 0.02f, railY, 0f), Vector3.zero, railSize, false);
            // Trigger (blocks nothing): where the water walls must open.
            var footprint = Box(collision, "Footprint", new Vector3(0f, DeckTop * 0.5f, 0f), Vector3.zero,
                new Vector3(RailX * 2f, 0.1f, EndT * 2f), true);

            // Where the bridge crosses the water, along its real axis.
            var center = new Vector3(position.x, 0f, position.z);
            var axis = Axis;
            var half = EndT * scale.x;
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
            }
            else
            {
                var limit = DeckAboveWater * scale.x;
                if (first.Value < -limit || last > limit)
                {
                    Debug.LogWarning($"[MapBridge] The water under {name} is wider than its deck: an end is in the " +
                                     "water. Move it or make it bigger (uniform scale), then Fit To Water again.", this);
                }
                if (_gapA != null)
                {
                    _gapA.position = center + axis * first.Value;
                }
                if (_gapB != null)
                {
                    _gapB.position = center + axis * last;
                }
            }

            foreach (var shape in _shapesToRebuild)
            {
                if (shape != null)
                {
                    shape.EnsureGapBox(footprint);
                    shape.RebuildShape();
                }
            }
            UnityEditor.EditorUtility.SetDirty(gameObject);
        }

        private static Transform Child(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child == null)
            {
                child = new GameObject(childName).transform;
                child.SetParent(parent, false);
            }
            return child;
        }

        private static BoxCollider Box(Transform parent, string childName, Vector3 localPosition, Vector3 localEuler,
            Vector3 size, bool trigger)
        {
            var child = Child(parent, childName);
            child.localPosition = localPosition;
            child.localRotation = Quaternion.Euler(localEuler);
            child.localScale = Vector3.one;
            var box = child.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = child.gameObject.AddComponent<BoxCollider>();
            }
            box.center = Vector3.zero;
            box.size = size;
            box.isTrigger = trigger;
            return box;
        }

        private void OnDrawGizmosSelected()
        {
            var center = transform.position;
            var half = EndT * transform.localScale.x;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawLine(center - Axis * half + Vector3.up * 0.3f, center + Axis * half + Vector3.up * 0.3f);
            if (_gapA != null && _gapB != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
                Gizmos.DrawWireSphere(_gapA.position, 0.2f);
                Gizmos.DrawWireSphere(_gapB.position, 0.2f);
            }
        }
#endif
    }
}
