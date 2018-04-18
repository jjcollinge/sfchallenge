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
    /// The class is intentionally immutable
    /// to avoid data corruption when working
    /// with reliable collections.
    /// </summary>
    [DataContract]
    public sealed class User : IEquatable<User>
    {
        public User(string id, string username, UInt32 quantity, UInt32 balance, IEnumerable<Transfer> transfers)
        {
            Id = id;
            Username = username;
            Quantity = quantity;
            Balance = balance;
            Transfers = (transfers == null) ? ImmutableList<Transfer>.Empty : transfers.ToImmutableList();
        }

        [OnDeserialized]
        private void OnDeserialize(StreamingContext context)
        {
            Transfers = Transfers.ToImmutableList();
        }

        [DataMember]
        public readonly string Id;

        [DataMember]
        public readonly string Username;

        [DataMember]
        public readonly UInt32 Quantity;

        [DataMember]
        public readonly UInt32 Balance;

        [DataMember]
        public IEnumerable<Transfer> Transfers { get; private set; }

        public bool Equals(User other)
        {
            if (other == null) return false;
            return string.Equals(Id, other.Id) &&
                   string.Equals(Username, Username) &&
                   Quantity == other.Quantity &&
                   Balance == other.Balance &&
                   Equals(Transfers, other.Transfers);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != obj.GetType()) return false;
            return Equals(obj as User);
        }
    }
}
