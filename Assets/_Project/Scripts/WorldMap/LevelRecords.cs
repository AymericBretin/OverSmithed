using System;
using OverSmithed.Core;
using Unity.Netcode;
using UnityEngine;

namespace OverSmithed.WorldMap
{
    /// <summary>
    /// The best stars and coins of every level on the map, for the level popup (UI-12). The save belongs to the HOST
    /// (<see cref="GameProgress"/>, its PlayerPrefs): when the map opens, the host copies its records into a
    /// NetworkList, and every machine reads that copy. A client never reads its own save.
    /// In-scene NetworkObject of the WorldMap scene.
    /// </summary>
    public class LevelRecords : NetworkBehaviour
    {
        /// <summary>One level's best result.</summary>
        public struct Record : INetworkSerializable, IEquatable<Record>
        {
            public int LevelNumber;
            public bool Played;
            public int Stars;
            public int Coins;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref LevelNumber);
                serializer.SerializeValue(ref Played);
                serializer.SerializeValue(ref Stars);
                serializer.SerializeValue(ref Coins);
            }

            public bool Equals(Record other)
            {
                return LevelNumber == other.LevelNumber && Played == other.Played && Stars == other.Stars
                    && Coins == other.Coins;
            }

            public override bool Equals(object obj) => obj is Record other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(LevelNumber, Played, Stars, Coins);
        }

        private NetworkList<Record> _records;

        public static LevelRecords Current { get; private set; }

        /// <summary>Every machine: the records arrived or changed.</summary>
        public static event Action Changed;

        // Domain reload is off: statics must be reset by hand when Play Mode starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            Changed = null;
        }

        private void Awake()
        {
            // NetworkList must be created before the object is spawned.
            _records = new NetworkList<Record>();
        }

        public override void OnNetworkSpawn()
        {
            Current = this;
            _records.OnListChanged += HandleListChanged;
            if (IsServer)
            {
                HostRefresh();
            }
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            _records.OnListChanged -= HandleListChanged;
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>
        /// Every machine: the best result of a level. False if the level was never finished (or the records have not
        /// arrived yet).
        /// </summary>
        public bool TryGet(int levelNumber, out int stars, out int coins)
        {
            if (IsSpawned)
            {
                foreach (var record in _records)
                {
                    if (record.LevelNumber == levelNumber && record.Played)
                    {
                        stars = record.Stars;
                        coins = record.Coins;
                        return true;
                    }
                }
            }
            stars = 0;
            coins = 0;
            return false;
        }

        /// <summary>Host: copies the host's save for every level circle of the map.</summary>
        public void HostRefresh()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            _records.Clear();
            foreach (var node in LevelNode.All)
            {
                if (node.LevelNumber <= 0)
                {
                    continue;
                }
                var played = GameProgress.TryGetBest(node.LevelNumber, out var stars, out var coins);
                _records.Add(new Record { LevelNumber = node.LevelNumber, Played = played, Stars = stars, Coins = coins });
            }
        }

        private void HandleListChanged(NetworkListEvent<Record> change)
        {
            Changed?.Invoke();
        }
    }
}
