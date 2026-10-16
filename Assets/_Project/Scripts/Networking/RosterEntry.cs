using System;
using Unity.Collections;
using Unity.Netcode;

namespace OverSmithed.Networking
{
    /// <summary>
    /// One player of the session: the machine that owns it (<see cref="ClientId"/>), the input slot on that
    /// machine (<see cref="LocalIndex"/>) and the player number shown to everyone (1..4).
    /// (ClientId, LocalIndex) is unique across the session.
    /// </summary>
    public struct RosterEntry : INetworkSerializable, IEquatable<RosterEntry>
    {
        public ulong ClientId;
        public byte LocalIndex;
        public byte PlayerNumber;
        public FixedString32Bytes DeviceLabel;

        public bool Matches(ulong clientId, byte localIndex)
        {
            return ClientId == clientId && LocalIndex == localIndex;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref LocalIndex);
            serializer.SerializeValue(ref PlayerNumber);
            serializer.SerializeValue(ref DeviceLabel);
        }

        public bool Equals(RosterEntry other)
        {
            return ClientId == other.ClientId
                   && LocalIndex == other.LocalIndex
                   && PlayerNumber == other.PlayerNumber
                   && DeviceLabel.Equals(other.DeviceLabel);
        }

        public override bool Equals(object obj)
        {
            return obj is RosterEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ClientId, LocalIndex, PlayerNumber);
        }
    }
}
