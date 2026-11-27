using System;
using System.Collections.Generic;
using OverSmithed.Counters;
using OverSmithed.Players;
using OverSmithed.Items;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>
    /// A counter that turns the item on it into another one (ore into a hot ingot...). It only takes items that have a
    /// recipe for its <see cref="StationKind"/>; when the work is done the host changes the item in place
    /// (<see cref="NetworkItem.SetDefinition"/>). Taking the item off before the end resets the work.
    /// Shared base of every station: the furnace works by itself (<see cref="_autoStart"/>); a station worked by hand
    /// (grindstone, anvil: hold Use) turns auto start off: it works while at least one player holds Use in front of
    /// it with empty hands (<see cref="PlayerWork"/> tells the host), and pauses otherwise (the progress stays).
    /// Network: only a small <see cref="WorkState"/> travels, written when the work starts, pauses or ends; every
    /// machine computes the current progress from it with the shared network clock, so the bar is smooth everywhere.
    /// </summary>
    public class Station : Counter
    {
        /// <summary>What the host last said about the work. Progress at that moment + when + is it running.</summary>
        private struct WorkState : INetworkSerializable, IEquatable<WorkState>
        {
            public float Progress;
            public double ServerTime;
            public float Duration;
            public bool Running;
            public NetworkObjectReference Worker;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Progress);
                serializer.SerializeValue(ref ServerTime);
                serializer.SerializeValue(ref Duration);
                serializer.SerializeValue(ref Running);
                serializer.SerializeValue(ref Worker);
            }

            public bool Equals(WorkState other)
            {
                return Progress.Equals(other.Progress) && ServerTime.Equals(other.ServerTime)
                    && Duration.Equals(other.Duration) && Running == other.Running && Worker.Equals(other.Worker);
            }
        }

        [Header("Station")]
        [SerializeField] private StationKind _kind = StationKind.None;
        [SerializeField] private RecipeDatabase _recipes;
        [Tooltip("On: the work starts by itself when an item with a recipe is put here (furnace). Off: the work is " +
                 "started and paused by code (anvil: hold Use).")]
        [SerializeField] private bool _autoStart = true;
        [Tooltip("Only one player may hold Use on this station at a time (anvil). Other players must release and retry.")]
        [SerializeField] private bool _singleWorker;

        private readonly NetworkVariable<WorkState> _work = new();
        // Host: players holding Use on this station (hand work only).
        private readonly HashSet<PlayerWork> _workers = new();
        private readonly List<PlayerWork> _gone = new();

        // Host: the real progress, counted with the game time (a local pause stops it).
        private float _hostProgress;
        private PlayerWork _activeWorker;

        /// <summary>Every machine: the work just finished (the item became the recipe's output).</summary>
        public event Action<Station> WorkCompleted;

        public StationKind Kind => _kind;

        /// <summary>True for a station worked by holding Use (no auto start).</summary>
        public bool NeedsHandWork => !_autoStart;

        /// <summary>Every machine: the recipe for the item on the station, or null.</summary>
        public RecipeDefinition CurrentRecipe
        {
            get
            {
                var item = Item;
                if (item == null || _recipes == null || item.Definition == null)
                {
                    return null;
                }
                return _recipes.TryFind(_kind, item.Definition, out var recipe) ? recipe : null;
            }
        }

        /// <summary>Every machine: true while the work is going on.</summary>
        public bool IsWorking => _work.Value.Running;

        /// <summary>The player actually doing the work, replicated atomically with the progress and running flag.</summary>
        public PlayerWork ActiveWorker => IsSpawned && IsWorking
            && _work.Value.Worker.TryGet(out var worker, NetworkManager) ? worker.GetComponent<PlayerWork>() : null;

        /// <summary>Elapsed work time, using the same synchronized clock as the progress bar.</summary>
        public float WorkSeconds
        {
            get
            {
                var state = _work.Value;
                var elapsed = state.Running && NetworkManager != null
                    ? (float)(NetworkManager.ServerTime.Time - state.ServerTime) : 0f;
                // Visuals on the host use the same clock interpolation as remote observers, not the previous
                // frame's authoritative counter (which can lag after a long rendering frame).
                return Mathf.Clamp(state.Progress * state.Duration + elapsed, 0f, state.Duration);
            }
        }

        /// <summary>Every machine: work done on the current item, 0..1.</summary>
        public float Progress
        {
            get
            {
                if (IsServer)
                {
                    return _hostProgress;
                }
                var state = _work.Value;
                if (!state.Running || state.Duration <= 0f || NetworkManager == null)
                {
                    return state.Progress;
                }
                var elapsed = NetworkManager.ServerTime.Time - state.ServerTime;
                return Mathf.Clamp01(state.Progress + (float)(elapsed / state.Duration));
            }
        }

        protected override void Awake()
        {
            base.Awake();
            ItemChanged += HandleItemChanged;
        }

        public override void OnDestroy()
        {
            ItemChanged -= HandleItemChanged;
            base.OnDestroy();
        }

        protected override bool Accepts(NetworkItem item)
        {
            // A pile of trays never goes on a station: one tray at a time.
            if (item.TryGetComponent<Tray>(out var tray) && tray.Count > 1)
            {
                return false;
            }
            return item.Definition != null && _recipes != null && _recipes.TryFind(_kind, item.Definition, out _);
        }

        // Nothing goes onto the item of a station (no piling trays on the grindstone).
        protected override bool AcceptsOntoItem => false;

        /// <summary>Host: registers/releases a worker. False when the station cannot accept that player.</summary>
        public bool SetWorker(PlayerWork worker, bool working)
        {
            if (!IsServer || !IsSpawned || worker == null || !NeedsHandWork)
            {
                return false;
            }
            if (working)
            {
                if (!worker.CanWorkAt(this))
                {
                    return false;
                }
                if (_singleWorker)
                {
                    // Release stale claims before admitting someone else (left, fell, disconnected, or took an item).
                    _workers.RemoveWhere(candidate => candidate == null || !candidate.CanWorkAt(this));
                    if (_workers.Count > 0 && !_workers.Contains(worker))
                    {
                        return false;
                    }
                }
                _workers.Add(worker);
            }
            else
            {
                _workers.Remove(worker);
            }
            return true;
        }

        public override void OnNetworkDespawn()
        {
            _workers.Clear();
            _activeWorker = null;
            _hostProgress = 0f;
            base.OnNetworkDespawn();
        }

        // ---------- Host ----------

        /// <summary>Host: starts (or goes on with) the work on the current item. Nothing if it has no recipe.</summary>
        protected void StartWork()
        {
            var recipe = CurrentRecipe;
            if (!IsServer || recipe == null || IsWorking)
            {
                return;
            }
            if (recipe.WorkTime <= 0f)
            {
                Complete(recipe);
                return;
            }
            WriteState(true, recipe.WorkTime);
        }

        /// <summary>Host: stops the work where it is (the progress stays).</summary>
        protected void PauseWork()
        {
            if (IsServer && IsWorking)
            {
                WriteState(false, _work.Value.Duration);
            }
        }

        // Every machine: an item was put on the station or taken off it.
        private void HandleItemChanged(Counter counter)
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            // A new item (or none) always starts from zero.
            _hostProgress = 0f;
            WriteState(false, 0f);
            if (_autoStart)
            {
                StartWork();
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            if (NeedsHandWork)
            {
                UpdateHandWork();
            }
            if (!IsWorking)
            {
                return;
            }
            var recipe = CurrentRecipe;
            if (recipe == null)
            {
                _hostProgress = 0f;
                WriteState(false, 0f);
                return;
            }
            _hostProgress = Mathf.Min(1f, _hostProgress + Time.deltaTime / _work.Value.Duration);
            if (_hostProgress >= 1f)
            {
                Complete(recipe);
            }
        }

        // Host: a hand work station runs while someone valid holds Use on it (in reach, empty hands, can act).
        private void UpdateHandWork()
        {
            PlayerWork active = null;
            _gone.Clear();
            foreach (var worker in _workers)
            {
                if (worker == null || !worker.IsSpawned)
                {
                    _gone.Add(worker);
                }
                else if (worker.CanWorkAt(this))
                {
                    if (active == null || worker == _activeWorker)
                    {
                        active = worker;
                    }
                }
            }
            foreach (var worker in _gone)
            {
                _workers.Remove(worker);
            }
            var changed = active != _activeWorker;
            _activeWorker = active;
            if (active != null && !IsWorking && CurrentRecipe != null)
            {
                StartWork();
            }
            else if (active == null && IsWorking)
            {
                PauseWork();
            }
            else if (changed && IsWorking)
            {
                WriteState(true, _work.Value.Duration);
            }
        }

        private void Complete(RecipeDefinition recipe)
        {
            var item = Item;
            _hostProgress = 0f;
            WriteState(false, 0f);
            if (item == null)
            {
                return;
            }
            item.SetDefinition(recipe.Output);
            CompletedRpc();
            // The output may itself have a recipe here (a chain of steps on the same station).
            if (_autoStart)
            {
                StartWork();
            }
        }

        private void WriteState(bool running, float duration)
        {
            _work.Value = new WorkState
            {
                Progress = _hostProgress,
                ServerTime = NetworkManager.ServerTime.Time,
                Duration = duration,
                Running = running,
                Worker = new NetworkObjectReference(running && _activeWorker != null ? _activeWorker.NetworkObject : null),
            };
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CompletedRpc()
        {
            WorkCompleted?.Invoke(this);
        }

        private void OnValidate()
        {
            if (_kind == StationKind.None)
            {
                Debug.LogWarning($"[Station] '{name}': choose a station kind.", this);
            }
        }
    }
}
