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
        private const int tradeMax = 100;

        public User(string id, string username, IEnumerable<KeyValuePair<string, double>> currencyAmounts, IEnumerable<string> trades)
        {
            Id = id;
            Username = username;
            CurrencyAmounts = (currencyAmounts == null) ? ImmutableDictionary<string, double>.Empty : ImmutableDictionary.CreateRange<string, double>(currencyAmounts);
            LatestTrades = (trades == null) ? ImmutableQueue<string>.Empty : ImmutableQueue.CreateRange<string>(trades);
        }

        [OnSerializing()]
        private void OnSerializing(StreamingContext context)
        {
            _LatestTrades = LatestTrades.ToArray();
            _CurrencyAmounts = CurrencyAmounts.ToDictionary(x => x.Key, x => x.Value);
        }

        [OnDeserialized()]
        private void OnDeserialized(StreamingContext context)
        {
            LatestTrades = (_LatestTrades == null) ? ImmutableQueue<string>.Empty : ImmutableQueue.CreateRange<string>(_LatestTrades);
            CurrencyAmounts = (_CurrencyAmounts == null) ? ImmutableDictionary<string, double>.Empty : ImmutableDictionary<string, double>.Empty.AddRange(_CurrencyAmounts);
        }

        [DataMember]
        public readonly string Id;

        [DataMember]
        public readonly string Username;

        [DataMember(Name = "CurrencyAmounts")]
        private Dictionary<string, double> _CurrencyAmounts;

        [DataMember(Name = "LatestTrades")]
        private string[] _LatestTrades;

        public ImmutableDictionary<string, double> CurrencyAmounts { get; private set; }

        public ImmutableQueue<string> LatestTrades { get; private set; }

        public void UpdateCurrencyAmount(string currency, double amount)
        {
            var mutable = CurrencyAmounts.ToDictionary(x => x.Key, x => x.Value);
            mutable[currency] += amount;
            CurrencyAmounts = mutable.ToImmutableDictionary<string, double>();
        }

        public User AddTrade(string tradeId)
        {
            if (LatestTrades.Count() <= tradeMax)
            {
                return new User(Id, Username, CurrencyAmounts, ((IImmutableQueue<string>)LatestTrades).Enqueue(tradeId));
            }
            else
            {
                var queue = ((IImmutableQueue<string>)LatestTrades).Dequeue();
                var newQueue = queue.Enqueue(tradeId);
                return new User(Id, Username, CurrencyAmounts, newQueue);
            }
        }

        public bool Equals(User other)
        {
            if (other == null) return false;
            return string.Equals(Id, other.Id) &&
                   string.Equals(Username, Username) &&
                   CurrencyAmounts == other.CurrencyAmounts &&
                   Equals(LatestTrades, other.LatestTrades);
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
            return new { Id, Username, CurrencyAmounts }.GetHashCode();
        }
    }
}