using Common;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;

namespace Common
{
    /// <summary>
    /// Represents a user in the system.
    /// This class is immutable to avoid
    /// data corruption when working with
    /// reliable collections.
    /// </summary>
    [DataContract]
    public sealed class User : IEquatable<User>
    {
        public User(string id, string username, uint quantity, uint balance, IEnumerable<string> transferIds)
        {
            Id = id;
            Username = username;
            Quantity = quantity;
            Balance = balance;
            TransferIds = (transferIds == null) ? ImmutableList<string>.Empty : transferIds.ToImmutableList();
        }

        [OnDeserialized]
        private void OnDeserialize(StreamingContext context)
        {
            TransferIds = TransferIds.ToImmutableList();
        }

        [DataMember]
        public readonly string Id;

        [DataMember]
        public readonly string Username;

        [DataMember]
        public readonly uint Quantity;

        [DataMember]
        public readonly uint Balance;

        [DataMember]
        public IEnumerable<String> TransferIds { get; private set; }

        public bool Equals(User other)
        {
            if (other == null) return false;
            return string.Equals(Id, other.Id) &&
                   string.Equals(Username, Username) &&
                   Quantity == other.Quantity &&
                   Balance == other.Balance &&
                   Equals(TransferIds, other.TransferIds);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != obj.GetType()) return false;
            return Equals(obj as User);
        }

        public override int GetHashCode()
        {
            return new { Id, Username, Quantity, Balance }.GetHashCode();
        }
    }
}