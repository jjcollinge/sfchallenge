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
    public sealed class Trade : IEquatable<Trade>
    {
        public Trade(string id, Order ask, Order bid, Order settlement)
        {
            Id = id;
            Ask = ask;
            Bid = bid;
            Settlement = settlement;
        }

        [BsonElement(elementName: "tradeId")]
        [DataMember]
        public readonly string Id;

        [BsonElement(elementName: "ask")]
        [DataMember]
        public readonly Order Ask;

        [BsonElement(elementName: "bid")]
        [DataMember]
        public readonly Order Bid;

        [BsonElement(elementName: "settlement")]
        [DataMember]
        public readonly Order Settlement;

        public bool Equals(Trade other)
        {
            if (other == null) return false;
            return string.Equals(Id, other.Id) &&
                   Equals(Ask, other.Ask) &&
                   Equals(Bid, other.Bid) &&
                   Equals(Settlement, other.Settlement);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != obj.GetType()) return false;
            return Equals(obj as Trade);
        }

        public override int GetHashCode()
        {
            return new { Id, Ask, Bid, Settlement }.GetHashCode();
        }
    }
}
