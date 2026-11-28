using System;
using System.Collections.Generic;
using OverSmithed.Counters;
using OverSmithed.Items;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.Crafting
{
    /// <summary>Where a station is on the way to catching fire.</summary>
    public enum OverheatPhase : byte
    {
        /// <summary>Nothing to fear (or the ready item was just made: the safe time runs).</summary>
        Safe = 0,
        /// <summary>The ready item is about to burn: the warning icon blinks.</summary>
        Warning = 1,
        /// <summary>The item burnt and the station is on fire: it refuses items and keeps its own.</summary>
        OnFire = 2,
    }

    /// <summary>
    /// Makes a station (the furnace) overheat: a ready item left on it (hot ingot) burns after a while. First a safe
    /// time, then a warning (blinking icon), then the item becomes the burnt item and the station catches fire. A
    /// station on fire refuses items and keeps its burnt item until the fire is put out (<see cref="Extinguish"/>,
    /// the water bucket of CR-14). Taking the ready item off before the end resets everything.
    /// Host rules: the host counts the time (game time, so a local pause stops it). Network: one small
    /// <see cref="OverheatState"/> (phase + when it started), so every machine shows the warning and the fire at the
    /// same time; looks are done by <see cref="FurnaceFireEffects"/>.
    /// </summary>
    [RequireComponent(typeof(Station))]
    public class StationOverheat : NetworkBehaviour
    {
        private struct OverheatState : INetworkSerializable, IEquatable<OverheatState>
        {
            public OverheatPhase Phase;
            public double Since;          // network server time when this phase started
            public float WarningDuration; // seconds, so clients can time the blinking

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Phase);
                serializer.SerializeValue(ref Since);
                serializer.SerializeValue(ref WarningDuration);
            }

            public bool Equals(OverheatState other)
            {
                return Phase == other.Phase && Since.Equals(other.Since) && WarningDuration.Equals(other.WarningDuration);
            }
        }

        [Tooltip("Items that burn when they are left on this station (furnace: the hot ingot).")]
        [SerializeField] private List<ItemDefinition> _burnableItems = new();
        [Tooltip("What a burnable item becomes when it burns.")]
        [SerializeField] private ItemDefinition _burntItem;
        [Tooltip("Seconds a ready item can stay on the station before the warning starts.")]
        [SerializeField, Min(0f)] private float _safeTime = 5f;
        [Tooltip("Seconds of warning (blinking icon) before the item burns and the station catches fire.")]
        [SerializeField, Min(0.1f)] private float _warningTime = 8f;

        private readonly NetworkVariable<OverheatState> _state = new();
        private Station _station;
        // Host: game time the ready item has spent on the station.
        private float _hostTimer;

        /// <summary>Every machine: the phase changed (warning started, fire started, fire put out...).</summary>
        public event Action<StationOverheat, OverheatPhase, OverheatPhase> PhaseChanged;

        public Station Station => _station;

        /// <summary>Every machine: the current phase (Safe when not spawned).</summary>
        public OverheatPhase Phase => IsSpawned ? _state.Value.Phase : OverheatPhase.Safe;

        public bool IsOnFire => Phase == OverheatPhase.OnFire;

        public bool IsWarning => Phase == OverheatPhase.Warning;

        public float SafeTime => _safeTime;

        public float WarningTime => _warningTime;

        /// <summary>Every machine: how far the warning is, 0 (just started) .. 1 (burns now). 0 outside the warning.</summary>
        public float WarningProgress
        {
            get
            {
                var state = _state.Value;
                if (!IsSpawned || state.Phase != OverheatPhase.Warning || state.WarningDuration <= 0f
                    || NetworkManager == null)
                {
                    return 0f;
                }
                return Mathf.Clamp01((float)((NetworkManager.ServerTime.Time - state.Since) / state.WarningDuration));
            }
        }

        private void Awake()
        {
            _station = GetComponent<Station>();
            _station.ItemChanged += HandleItemChanged;
        }

        public override void OnDestroy()
        {
            if (_station != null)
            {
                _station.ItemChanged -= HandleItemChanged;
            }
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += HandleStateChanged;
            _hostTimer = 0f;
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateChanged;
            _hostTimer = 0f;
        }

        // ---------- Host ----------

        /// <summary>Host: puts the fire out (water bucket, CR-14). The burnt item stays. False if not on fire.</summary>
        public bool Extinguish()
        {
            if (!IsServer || !IsSpawned || !IsOnFire)
            {
                return false;
            }
            _hostTimer = 0f;
            WriteState(OverheatPhase.Safe);
            Debug.Log($"[Overheat] '{name}': the fire is out.", this);
            return true;
        }

        /// <summary>Host (debug): burns the item lying here now and sets the station on fire.</summary>
        public bool IgniteNow()
        {
            if (!IsServer || !IsSpawned || IsOnFire)
            {
                return false;
            }
            Ignite();
            return true;
        }

        // Every machine: an item was put on the station or taken off it: the time starts again.
        private void HandleItemChanged(Counter counter)
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            _hostTimer = 0f;
            if (Phase == OverheatPhase.Warning)
            {
                WriteState(OverheatPhase.Safe);
            }
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || IsOnFire)
            {
                return;
            }
            var item = _station.Item;
            var burning = item != null && item.Definition != null && _burnableItems.Contains(item.Definition)
                && !_station.IsWorking;
            if (!burning)
            {
                _hostTimer = 0f;
                if (Phase == OverheatPhase.Warning)
                {
                    WriteState(OverheatPhase.Safe);
                }
                return;
            }

            _hostTimer += Time.deltaTime;
            if (_hostTimer >= _safeTime + _warningTime)
            {
                Ignite();
            }
            else if (_hostTimer >= _safeTime && Phase == OverheatPhase.Safe)
            {
                WriteState(OverheatPhase.Warning);
                Debug.Log($"[Overheat] '{name}': {item.Definition.DisplayName} is about to burn!", this);
            }
        }

        private void Ignite()
        {
            var item = _station.Item;
            if (item != null && _burntItem != null)
            {
                item.SetDefinition(_burntItem);
            }
            _hostTimer = 0f;
            WriteState(OverheatPhase.OnFire);
            Debug.Log($"[Overheat] '{name}' is on fire!", this);
        }

        private void WriteState(OverheatPhase phase)
        {
            _state.Value = new OverheatState
            {
                Phase = phase,
                Since = NetworkManager.ServerTime.Time,
                WarningDuration = _warningTime,
            };
        }

        // ---------- Every machine ----------

        private void HandleStateChanged(OverheatState previous, OverheatState current)
        {
            if (previous.Phase != current.Phase)
            {
                PhaseChanged?.Invoke(this, previous.Phase, current.Phase);
            }
        }

        private void OnValidate()
        {
            if (_burntItem == null)
            {
                Debug.LogWarning($"[Overheat] '{name}': choose the burnt item.", this);
            }
        }
    }
}
