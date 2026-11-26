using System;
using Unity.Netcode;

namespace OverSmithed.Orders
{
    /// <summary>
    /// One customer order, as it travels on the network (an entry of <see cref="OrderManager"/>'s list). Only ids:
    /// every machine finds the item in its own <see cref="Items.ItemDatabase"/>.
    /// </summary>
    public struct Order : INetworkSerializable, IEquatable<Order>
    {
        /// <summary>Unique in the shift, grows with each new order (a smaller id = an older order).</summary>
        public int Id;

        /// <summary>The ordered item (<see cref="Items.ItemDefinition.Id"/>).</summary>
        public int ItemId;

        /// <summary>Network server time when the order came in.</summary>
        public double CreatedAt;

        /// <summary>Seconds of game time allowed for this order.</summary>
        public float PatienceDuration;

        /// <summary>Deadline on OrderManager's game clock (which stops when play stops).</summary>
        public double ExpiresAt;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref CreatedAt);
            serializer.SerializeValue(ref PatienceDuration);
            serializer.SerializeValue(ref ExpiresAt);
        }

        public bool Equals(Order other)
        {
            return Id == other.Id && ItemId == other.ItemId && CreatedAt.Equals(other.CreatedAt)
                && PatienceDuration.Equals(other.PatienceDuration) && ExpiresAt.Equals(other.ExpiresAt);
        }

        public override bool Equals(object obj) => obj is Order other && Equals(other);

        public override int GetHashCode() => Id;
    }
}
