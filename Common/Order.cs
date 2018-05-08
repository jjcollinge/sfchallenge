using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Common
{
    [BsonIgnoreExtraElements]
    [DataContract]
    public class Order : IComparable<Order>, IComparer<Order>, IEquatable<Order>
    {
        [JsonConstructor]
        public Order(string id, uint value, uint quantity, string userId, DateTime timestamp)
        {
            Id = id;
            Value = value;
            Quantity = quantity;
            UserId = userId;
            if (timestamp == default(DateTime))
            {
                timestamp = DateTime.UtcNow;
            }
            Timestamp = timestamp;
        }

        public Order(string id, uint value, uint quantity, string userId)
        {
            Id = id;
            Value = value;
            Quantity = quantity;
            UserId = userId;
            Timestamp = DateTime.UtcNow;
        }

        public Order(string id, int value, int quantity, string userId)
        {
            Id = id;
            Value = (uint)value;
            Quantity = (uint)quantity;
            UserId = userId;
            Timestamp = DateTime.UtcNow;
        }

        public Order(uint value, uint quantity, string userId)
        {
            Id = Guid.NewGuid().ToString();
            Value = value;
            Quantity = quantity;
            UserId = userId;
            Timestamp = DateTime.UtcNow;
        }

        public Order(int value, int quantity, string userId)
        {
            Id = Guid.NewGuid().ToString();
            Value = (uint)value;
            Quantity = (uint)quantity;
            UserId = userId;
            Timestamp = DateTime.UtcNow;
        }

        [BsonElement(elementName: "orderId")]
        [DataMember]
        public readonly string Id;
        [BsonElement(elementName: "userId")]
        [DataMember]
        public readonly string UserId;
        [BsonElement(elementName: "value")]
        [DataMember]
        public readonly uint Value;
        [BsonElement(elementName: "quantity")]
        [DataMember]
        public readonly uint Quantity;
        [BsonElement(elementName: "timestamp")]
        [DataMember]
        public readonly DateTime Timestamp;

        #region Compare and Equals Methods
        public int Compare(Order x, Order y)
        {
            if (x.Value > y.Value)
            {
                return 1;
            }
            if (x.Value == y.Value)
            {
                return DateTime.Compare(x.Timestamp, y.Timestamp) * -1;
            }
            return -1;
        }

        public int CompareTo(Order other)
        {
            if (this.Value > other.Value)
            {
                return 1;
            }
            if (this.Value == other.Value)
            {
                return DateTime.Compare(Timestamp, other.Timestamp) * -1;
            }
            return -1;
        }

        public bool Equals(Order other)
        {
            if (other == null) return false;
            return string.Equals(Id, other.Id) &&
                string.Equals(UserId, other.UserId) &&
                Quantity == other.Quantity &&
                Value == other.Value &&
                Timestamp == other.Timestamp;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != obj.GetType()) return false;
            return Equals(obj as Order);
        }

        public override int GetHashCode()
        {
            return new { Id, Value, Quantity, Timestamp, UserId }.GetHashCode();
        }
        #endregion
    }
}
