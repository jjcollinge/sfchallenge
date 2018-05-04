using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Common
{
    /// <summary>
    /// Represents a pair of matched
    /// orders. This will be used to
    /// exchange the goods and value
    /// between each party.
    /// </summary>
    [BsonIgnoreExtraElements]
    [DataContract]
    public sealed class Transfer : IEquatable<Transfer>
    {
        public Transfer(string id, Order ask, Order bid)
        {
            Id = id;
            Ask = ask;
            Bid = bid;
        }

        [BsonElement(elementName: "transferId")]
        [DataMember]
        public readonly string Id;
        [BsonElement(elementName: "ask")]
        [DataMember]
        public readonly Order Ask;
        [BsonElement(elementName: "bid")]
        [DataMember]
        public readonly Order Bid;

        public bool Equals(Transfer other)
        {
            if (other == null) return false;
            return string.Equals(Id, other.Id) &&
                   Equals(Ask, other.Ask) &&
                   Equals(Bid, other.Bid);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != obj.GetType()) return false;
            return Equals(obj as Transfer);
        }

        public override int GetHashCode()
        {
            return new { Id, Ask, Bid }.GetHashCode();
        }
    }
}
