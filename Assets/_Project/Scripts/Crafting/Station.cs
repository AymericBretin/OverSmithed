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
    /// Hand work comes in two ways (<see cref="HandWorkInput"/>): HOLD Use (grindstone) or TAP Use (anvil, workbench:
    /// each press is one strike, the recipe says how many strikes make the item; the host counts them).
    /// Network: only a small <see cref="WorkState"/> travels, written when the work starts, pauses or ends (and on each
    /// strike); every machine computes the current progress from it with the shared network clock, so the bar is
    /// smooth everywhere.
    /// </summary>
    public class Station : Counter
    {
        /// <summary>How a hand-work station is worked.</summary>
        public enum HandWorkInput : byte
        {
            /// <summary>Hold Use: the work runs while it is held (grindstone).</summary>
            Hold = 0,
            /// <summary>Tap Use: each press is one strike (anvil, workbench).</summary>
            Tap = 1,
        }

        /// <summary>What the host last said about the work. Progress at that moment + when + is it running.</summary>
        private struct WorkState : INetworkSerializable, IEquatable<WorkState>
        {
            public float Progress;
            public double ServerTime;
            public float Duration;
            public bool Running;
            public NetworkObjectReference Worker;
            // Tap stations: strikes ever made on this station (wraps around). A change = new strike(s) to show.
            public ushort Strikes;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Progress);
                serializer.SerializeValue(ref ServerTime);
                serializer.SerializeValue(ref Duration);
                serializer.SerializeValue(ref Running);
                serializer.SerializeValue(ref Worker);
                serializer.SerializeValue(ref Strikes);
            }

            public bool Equals(WorkState other)
            {
                return Progress.Equals(other.Progress) && ServerTime.Equals(other.ServerTime)
                    && Duration.Equals(other.Duration) && Running == other.Running && Worker.Equals(other.Worker)
                    && Strikes == other.Strikes;
            }
        }

        [Header("Station")]
        [SerializeField] private StationKind _kind = StationKind.None;
        [SerializeField] private RecipeDatabase _recipes;
        [Tooltip("On: the work starts by itself when an item with a recipe is put here (furnace). Off: the work is " +
                 "started and paused by code (anvil: hold Use).")]
        [SerializeField] private bool _autoStart = true;
        [Tooltip("Only one player may work this station at a time (anvil). Others must wait until the worker stops.")]
        [SerializeField] private bool _singleWorker;
        [Tooltip("Hand work only (auto start off): hold Use (grindstone) or tap Use, one strike per press (anvil, " +
                 "workbench; the recipe gives the number of strikes).")]
        [SerializeField] private HandWorkInput _handInput = HandWorkInput.Hold;
        [Tooltip("Tap: seconds the worker keeps the station (and the tool in hand) after a strike. Others wait.")]
        [SerializeField, Min(0.1f)] private float _tapLinger = 0.5f;
        [Tooltip("Tap: shortest time (seconds) between two strikes of one player that count (stops macros).")]
        [SerializeField, Min(0f)] private float _minStrikeInterval = 0.08f;

        private readonly NetworkVariable<WorkState> _work = new();
        // Host: players holding Use on this station (hand work only).
        private readonly HashSet<PlayerWork> _workers = new();
        private readonly List<PlayerWork> _gone = new();

        // Host: the real progress, counted with the game time (a local pause stops it).
        private float _hostProgress;
        private PlayerWork _activeWorker;
        // Host, tap stations: strikes on the current item, total strikes, the last striker and when (game time).
        private int _itemStrikes;
        private ushort _strikeCount;
        private float _lastStrikeTime = float.MinValue;
        // Every machine, tap stations: local time of the last strike seen.
        private float _lastStrikeSeen = float.MinValue;
        // Optional: a station that can overheat (furnace). On fire = refuses items and keeps its own.
        private StationOverheat _overheat;

        /// <summary>Every machine: the work just finished (the item became the recipe's output).</summary>
        public event Action<Station> WorkCompleted;

        /// <summary>Every machine, tap stations: new strike(s) were made (the count, 1 or more if they came together).</summary>
        public event Action<Station, int> Struck;

        public StationKind Kind => _kind;

        /// <summary>True for a station worked by holding Use (no auto start).</summary>
        public bool NeedsHandWork => !_autoStart;

        /// <summary>True for a hand-work station worked by tapping Use (one strike per press).</summary>
        public bool UsesTaps => NeedsHandWork && _handInput == HandWorkInput.Tap;

        /// <summary>The overheat part of this station (furnace), or null.</summary>
        public StationOverheat Overheat => _overheat;

        /// <summary>Every machine: true while the station is on fire (see <see cref="StationOverheat"/>).</summary>
        public bool IsOnFire => _overheat != null && _overheat.IsOnFire;

        /// <summary>A station on fire keeps its (burnt) item until the fire is put out.</summary>
        public override bool AllowsTake => !IsOnFire;

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

        /// <summary>
        /// The player actually doing the work, replicated atomically with the progress and running flag. Tap stations:
        /// the last striker, for a short time after the strike.
        /// </summary>
        public PlayerWork ActiveWorker => IsSpawned && (IsWorking || IsTapWorked)
            && _work.Value.Worker.TryGet(out var worker, NetworkManager) ? worker.GetComponent<PlayerWork>() : null;

        // Every machine: a strike was seen a moment ago.
        private bool IsTapWorked => UsesTaps && Time.time - _lastStrikeSeen < _tapLinger;

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
            _overheat = GetComponent<StationOverheat>();
            ItemChanged += HandleItemChanged;
        }

        public override void OnDestroy()
        {
            ItemChanged -= HandleItemChanged;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _work.OnValueChanged += HandleWorkChanged;
        }

        protected override bool Accepts(NetworkItem item)
        {
            if (IsOnFire)
            {
                return false;
            }
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
            if (!IsServer || !IsSpawned || worker == null || !NeedsHandWork || UsesTaps)
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
            _work.OnValueChanged -= HandleWorkChanged;
            _workers.Clear();
            _activeWorker = null;
            _hostProgress = 0f;
            _itemStrikes = 0;
            _lastStrikeTime = float.MinValue;
            _lastStrikeSeen = float.MinValue;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Host, tap stations: one press of Use by <paramref name="worker"/> (checked: in reach, empty hands, able to
        /// act, the station's single worker). Adds one strike; the last one finishes the item. False if refused.
        /// </summary>
        public bool TryStrike(PlayerWork worker)
        {
            if (!IsServer || !IsSpawned || !UsesTaps || worker == null || !worker.CanStrikeAt(this))
            {
                return false;
            }
            var recipe = CurrentRecipe;
            if (recipe == null)
            {
                return false;
            }
            var now = Time.time;
            var busy = _activeWorker != null && _activeWorker != worker && _activeWorker.IsSpawned
                && _activeWorker.CanStrikeAt(this) && now - _lastStrikeTime < _tapLinger;
            if (_singleWorker && busy)
            {
                return false;
            }
            if (worker == _activeWorker && now - _lastStrikeTime < _minStrikeInterval)
            {
                return false;
            }
            _activeWorker = worker;
            _lastStrikeTime = now;
            _strikeCount++;
            _itemStrikes++;
            var needed = recipe.Strikes;
            _hostProgress = Mathf.Clamp01(_itemStrikes / (float)needed);
            if (_itemStrikes >= needed)
            {
                Complete(recipe);
            }
            else
            {
                WriteState(false, 0f);
            }
            return true;
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
            _itemStrikes = 0;
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
            if (NeedsHandWork && !UsesTaps)
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
            _itemStrikes = 0;
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
                Worker = new NetworkObjectReference((running || UsesTaps) && _activeWorker != null
                    ? _activeWorker.NetworkObject : null),
                Strikes = _strikeCount,
            };
        }

        // Every machine (the host too): new strikes in the state = show them.
        private void HandleWorkChanged(WorkState previous, WorkState current)
        {
            var count = (ushort)(current.Strikes - previous.Strikes);
            if (count == 0 || count > 1000)
            {
                return;
            }
            _lastStrikeSeen = Time.time;
            Struck?.Invoke(this, count);
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
