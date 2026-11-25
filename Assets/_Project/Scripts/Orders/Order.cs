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

        /// <summary>Network server time when the order came in (patience is counted from it, CR-5).</summary>
        public double CreatedAt;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref CreatedAt);
        }

        public bool Equals(Order other)
        {
            return Id == other.Id && ItemId == other.ItemId && CreatedAt.Equals(other.CreatedAt);
        }

        public override bool Equals(object obj) => obj is Order other && Equals(other);

        public override int GetHashCode() => Id;
    }
}
