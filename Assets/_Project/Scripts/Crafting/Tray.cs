using System.Collections.Generic;
using OverSmithed.Interaction;
using OverSmithed.Items;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// A forge tray (CR-11), like a plate in Overcooked. It is an item (carried, put on counters, dropped; never
    /// thrown) with two extras:
    /// - parts can be put on a clean single tray; a part that can no longer make any Tray recipe with the others is
    ///   refused; when the parts match a recipe they become its output, on the tray, with a pop;
    /// - clean empty trays (or worn ones) stack: one networked object shows the whole pile (<see cref="Count"/>),
    ///   and taking from a pile creates a new single tray.
    /// The host decides everything; the parts (item ids) and the pile size travel as network data, and every machine
    /// builds the visuals itself under the item's visual (so they follow the hands and light up with the tray).
    /// </summary>
    [RequireComponent(typeof(NetworkItem))]
    public class Tray : NetworkBehaviour
    {
        [SerializeField] private TrayConfig _config;
        [Tooltip("Height (m) above the tray's pivot where the parts lie (the top of the tray).")]
        [SerializeField] private float _contentsHeight = 0.06f;
        [Tooltip("Height (m) of one tray in a pile.")]
        [SerializeField] private float _stackStep = 0.07f;
        [Tooltip("Distance (m) between two parts lying side by side on the tray (along its width).")]
        [SerializeField] private float _contentsSpacing = 0.2f;
        [Header("Pop when the parts become the final item")]
        [SerializeField] private float _popScale = 1.3f;
        [SerializeField, Min(0.01f)] private float _popTime = 0.3f;
        [Tooltip("Optional burst of 3D sparks where the final item appears.")]
        [SerializeField] private ParticleSystem _fuseBurst;

        private NetworkList<int> _contents;
        private readonly NetworkVariable<int> _count = new(1);

        private NetworkItem _item;
        private Transform _extras;
        private Transform _contentsRoot;
        private bool _dirty;
        private ItemDefinition _shownDefinition;
        private float _popLeft;
        private readonly List<ItemDefinition> _scratch = new();

        /// <summary>Number of trays in this pile (1 = a single tray).</summary>
        public int Count => _count.Value;

        /// <summary>True when nothing lies on the tray.</summary>
        public bool IsEmpty => _contents.Count == 0;

        public bool IsClean => _config != null && _item != null && _item.Definition == _config.CleanTray;
        public bool IsWorn => _config != null && _item != null && _item.Definition == _config.WornTray;

        /// <summary>
        /// Every machine: the only item lying on the tray (for example the finished sword to deliver, CR-12), or null
        /// if the tray is empty or holds several parts.
        /// </summary>
        public ItemDefinition SingleContent => _contents.Count == 1 && _config != null && _config.Items != null
            ? _config.Items.Get(_contents[0]) : null;

        private void Awake()
        {
            _item = GetComponent<NetworkItem>();
            // NetworkList must be created before the object is spawned.
            _contents = new NetworkList<int>();
        }

        public override void OnNetworkSpawn()
        {
            _contents.OnListChanged += HandleContentsChanged;
            _count.OnValueChanged += HandleCountChanged;
            _dirty = true;
        }

        public override void OnNetworkDespawn()
        {
            _contents.OnListChanged -= HandleContentsChanged;
            _count.OnValueChanged -= HandleCountChanged;
        }

        // ---------- Host rules ----------

        /// <summary>
        /// Host: true if <paramref name="incoming"/> can go onto this tray: a part that still fits a recipe with the
        /// parts already here (single clean tray only), or a tray of the same kind to pile up (both empty).
        /// </summary>
        public bool CanAbsorb(NetworkItem incoming)
        {
            if (incoming == null || incoming == _item || !incoming.IsSpawned || _config == null)
            {
                return false;
            }
            if (incoming.TryGetComponent<Tray>(out var other))
            {
                return other.IsEmpty && IsEmpty && incoming.Definition == _item.Definition;
            }
            return CanAdd(incoming.Definition);
        }

        /// <summary>Host: true if this part can be put on the tray now.</summary>
        public bool CanAdd(ItemDefinition part)
        {
            if (part == null || !IsClean || Count != 1 || _config.Recipes == null)
            {
                return false;
            }
            FillContents(_scratch);
            _scratch.Add(part);
            return _config.Recipes.CanStillMake(StationKind.Tray, _scratch);
        }

        /// <summary>
        /// Host: puts <paramref name="incoming"/> onto this tray (a part, or a pile of trays) and removes it from the
        /// network. The parts become the final item when they match a recipe. False if it can't go here.
        /// </summary>
        public bool Absorb(NetworkItem incoming)
        {
            if (!IsServer || !IsSpawned || !CanAbsorb(incoming))
            {
                return false;
            }
            if (incoming.TryGetComponent<Tray>(out var other))
            {
                _count.Value += other.Count;
            }
            else
            {
                _contents.Add(incoming.Definition.Id);
            }
            incoming.NetworkObject.Despawn();
            TryFuse();
            return true;
        }

        /// <summary>
        /// Host: takes the top tray off this pile and returns it as a new single tray (created on the pile, so it
        /// flies from there to the hands). Null if this is a single tray.
        /// </summary>
        public NetworkItem TakeTop()
        {
            if (!IsServer || !IsSpawned || Count <= 1 || _config == null || _config.TrayPrefab == null)
            {
                return null;
            }
            var top = transform.position + transform.up * (_stackStep * (Count - 1));
            _count.Value -= 1;
            return NetworkItem.Create(_config.TrayPrefab, _item.Definition, top, transform.rotation);
        }

        /// <summary>Host: sets the pile size (for example the starting pile of a level).</summary>
        public void SetCount(int count)
        {
            if (IsServer && IsSpawned)
            {
                _count.Value = Mathf.Max(1, count);
            }
        }

        /// <summary>Host: throws away what lies on the tray (the trash bin keeps the tray). False if it was empty.</summary>
        public bool ClearContents()
        {
            if (!IsServer || !IsSpawned || IsEmpty)
            {
                return false;
            }
            _contents.Clear();
            return true;
        }

        private void TryFuse()
        {
            if (_contents.Count < 2 || _config.Recipes == null)
            {
                return;
            }
            FillContents(_scratch);
            if (!_config.Recipes.TryFind(StationKind.Tray, _scratch, out var recipe) || recipe.Output == null)
            {
                return;
            }
            _contents.Clear();
            _contents.Add(recipe.Output.Id);
            FusedRpc(recipe.Output.Id);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FusedRpc(int outputId)
        {
            var output = _config != null && _config.Items != null ? _config.Items.Get(outputId) : null;
            Debug.Log($"[Tray] The parts became {(output != null ? output.DisplayName : $"item {outputId}")}.", this);
            _popLeft = _popTime;
            if (_fuseBurst != null)
            {
                var height = _contentsHeight + _stackStep * (Count - 1);
                Instantiate(_fuseBurst, transform.position + transform.up * height, Quaternion.identity);
            }
        }

        private void FillContents(List<ItemDefinition> into)
        {
            into.Clear();
            if (_config == null || _config.Items == null)
            {
                return;
            }
            foreach (var id in _contents)
            {
                var part = _config.Items.Get(id);
                if (part != null)
                {
                    into.Add(part);
                }
            }
        }

        // ---------- Looks (every machine) ----------

        private void HandleContentsChanged(NetworkListEvent<int> change) => _dirty = true;
        private void HandleCountChanged(int previous, int current) => _dirty = true;

        private void LateUpdate()
        {
            if (!IsSpawned)
            {
                return;
            }
            // The tray's own visual is made by NetworkItem: rebuild the extras when it changes too (worn <-> clean).
            if (_dirty || _shownDefinition != _item.Definition)
            {
                Rebuild();
            }
            if (_contentsRoot != null && _popLeft > 0f)
            {
                _popLeft = Mathf.Max(0f, _popLeft - Time.deltaTime);
                var t = 1f - _popLeft / _popTime;
                // Up fast, then back with a small wobble.
                var scale = 1f + (_popScale - 1f) * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.5f);
                _contentsRoot.localScale = Vector3.one * scale;
            }
        }

        private void Rebuild()
        {
            _dirty = false;
            _shownDefinition = _item.Definition;
            if (_extras != null)
            {
                Destroy(_extras.gameObject);
            }
            var parent = _item.VisualRoot != null ? _item.VisualRoot : transform;
            _extras = new GameObject("TrayExtras").transform;
            _extras.SetParent(parent, false);

            // The pile: copies of the tray's model above the first one (made by NetworkItem).
            var trayModel = _shownDefinition != null ? _shownDefinition.Prefab : null;
            if (trayModel != null)
            {
                for (var i = 1; i < Count; i++)
                {
                    var copy = Instantiate(trayModel, _extras, false);
                    copy.transform.localPosition = Vector3.up * (_stackStep * i);
                }
            }

            // The parts, side by side across the tray (centered), on top of the pile.
            _contentsRoot = new GameObject("Contents").transform;
            _contentsRoot.SetParent(_extras, false);
            _contentsRoot.localPosition = Vector3.up * (_contentsHeight + _stackStep * (Count - 1));
            FillContents(_scratch);
            for (var i = 0; i < _scratch.Count; i++)
            {
                if (_scratch[i].Prefab == null)
                {
                    continue;
                }
                var part = Instantiate(_scratch[i].Prefab, _contentsRoot, false);
                part.transform.localPosition = Vector3.forward * ((i - (_scratch.Count - 1) * 0.5f) * _contentsSpacing);
            }

            if (TryGetComponent<Interactable>(out var interactable))
            {
                interactable.RefreshHighlight();
            }
        }
    }
}
