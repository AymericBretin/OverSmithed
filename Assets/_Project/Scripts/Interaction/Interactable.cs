using System.Collections.Generic;
using UnityEngine;

namespace OverSmithed.Interaction
{
    /// <summary>
    /// Something a player can interact with (item, counter, crate, station...). Put it on the root of the object.
    /// It only knows where the object is and how to highlight it: what an interaction does is decided by the
    /// other scripts of the object (pick up / put down: GP-8).
    /// An interactable can hold others (<see cref="Attach"/>: an item on a counter): players aim at the holder,
    /// and the held ones light up with it.
    /// Purely local: nothing here is sent on the network. Each machine highlights what its own players face.
    /// </summary>
    [DisallowMultipleComponent]
    public class Interactable : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly List<Interactable> EnabledInteractables = new();

        /// <summary>Every enabled interactable on this machine.</summary>
        public static IReadOnlyList<Interactable> All => EnabledInteractables;

        // Domain reload is disabled on entering Play Mode: statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            EnabledInteractables.Clear();
        }

        [Tooltip("Optional: the volume players aim at. Empty = the collider on this object, or its position if none.")]
        [SerializeField] private Collider _area;
        [Tooltip("The colors of the object are multiplied by this while it is highlighted.")]
        [SerializeField] private float _highlightBrightness = 1.4f;

        private readonly List<Renderer> _highlightedRenderers = new();
        private readonly HashSet<Object> _highlighters = new();
        private readonly List<Interactable> _attached = new();
        private MaterialPropertyBlock _block;

        /// <summary>True while at least one local player aims at this object (or at its holder).</summary>
        public bool IsHighlighted => _highlighters.Count > 0;

        /// <summary>The interactable this one is attached to (for example the counter under an item), or null.</summary>
        public Interactable Holder { get; private set; }

        /// <summary>False while attached to a holder: players aim at the holder instead.</summary>
        public bool CanBeTargeted => Holder == null;

        private void Awake()
        {
            if (_area == null)
            {
                _area = GetComponent<Collider>();
            }
        }

        private void OnEnable()
        {
            EnabledInteractables.Add(this);
        }

        private void OnDisable()
        {
            EnabledInteractables.Remove(this);
            // Players drop a disabled target on their next frame; the highlight goes away right now.
            if (IsHighlighted)
            {
                _highlighters.Clear();
                ClearHighlight();
                SetAttachedHighlight(false);
            }
        }

        // A destroyed holder or held object (scene change, despawn) leaves no link behind.
        private void OnDestroy()
        {
            if (Holder != null)
            {
                Holder.Detach(this);
            }
            foreach (var held in _attached)
            {
                if (held != null)
                {
                    held.Holder = null;
                    held.RemoveHighlight(this);
                }
            }
            _attached.Clear();
        }

        /// <summary>
        /// <paramref name="held"/> now sits on this object (for example an item placed on a counter): it lights up
        /// with this object and is no longer a target on its own. Local only: call it on every machine
        /// (for example when the counter's networked content changes, GP-7).
        /// </summary>
        public void Attach(Interactable held)
        {
            if (held == null || held == this || held.Holder == this)
            {
                return;
            }
            if (held.Holder != null)
            {
                held.Holder.Detach(held);
            }
            held.Holder = this;
            _attached.Add(held);
            if (IsHighlighted)
            {
                held.AddHighlight(this);
            }
        }

        /// <summary><paramref name="held"/> leaves this object: it is a target on its own again.</summary>
        public void Detach(Interactable held)
        {
            if (held == null || held.Holder != this)
            {
                return;
            }
            held.Holder = null;
            _attached.Remove(held);
            held.RemoveHighlight(this);
        }

        /// <summary>Point of the object closest to <paramref name="point"/> (world space).</summary>
        public Vector3 ClosestPoint(Vector3 point)
        {
            return _area != null && _area.enabled ? _area.bounds.ClosestPoint(point) : transform.position;
        }

        /// <summary>Center of the object (world space).</summary>
        public Vector3 Center => _area != null && _area.enabled ? _area.bounds.center : transform.position;

        /// <summary>
        /// <paramref name="highlighter"/> (a local player) aims at this object. Calling it again does nothing.
        /// The object stays highlighted while at least one highlighter aims at it.
        /// </summary>
        public void AddHighlight(Object highlighter)
        {
            if (!isActiveAndEnabled || !_highlighters.Add(highlighter))
            {
                return;
            }
            if (_highlighters.Count == 1)
            {
                ApplyHighlight();
                SetAttachedHighlight(true);
            }
        }

        /// <summary><paramref name="highlighter"/> stops aiming at this object.</summary>
        public void RemoveHighlight(Object highlighter)
        {
            if (_highlighters.Remove(highlighter) && _highlighters.Count == 0)
            {
                ClearHighlight();
                SetAttachedHighlight(false);
            }
        }

        // The holder itself is the highlighter key of what it holds.
        private void SetAttachedHighlight(bool highlighted)
        {
            // A held item destroyed without Detach (despawned) compares equal to null.
            _attached.RemoveAll(held => held == null);
            foreach (var held in _attached)
            {
                if (highlighted)
                {
                    held.AddHighlight(this);
                }
                else
                {
                    held.RemoveHighlight(this);
                }
            }
        }

        /// <summary>Call after the visual of the object changed (for example an item turned into another one).</summary>
        public void RefreshHighlight()
        {
            if (IsHighlighted)
            {
                ClearHighlight();
                ApplyHighlight();
            }
        }

        // A per-renderer property block changes only this object, not the shared materials of every copy.
        private void ApplyHighlight()
        {
            _block ??= new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                // An interactable child (for example an item parented to a counter) has its own highlight: Attach it.
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    || renderer.GetComponentInParent<Interactable>() != this)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    var colorId = material == null ? -1
                        : material.HasProperty(BaseColorId) ? BaseColorId
                        : material.HasProperty(ColorId) ? ColorId : -1;
                    if (colorId == -1)
                    {
                        continue;
                    }

                    var color = material.GetColor(colorId);
                    var bright = color * _highlightBrightness;
                    bright.a = color.a;
                    _block.Clear();
                    _block.SetColor(colorId, bright);
                    renderer.SetPropertyBlock(_block, i);
                }
                _highlightedRenderers.Add(renderer);
            }
        }

        private void ClearHighlight()
        {
            foreach (var renderer in _highlightedRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                for (var i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    renderer.SetPropertyBlock(null, i);
                }
            }
            _highlightedRenderers.Clear();
        }
    }
}
