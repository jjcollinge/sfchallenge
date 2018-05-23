using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Common
{
    public enum CurrencyPair
    {
        GBPUSD,
        GBPEUR,
        USDGBP,
        USDEUR,
        EURGBP,
        EURUSD
    }

    public static class CurrencyPairExtensions
    {
        const string GBPUSD_SYMBOL = "GBP/USD";
        const string GBPEUR_SYMBOL = "GBP/EUR";
        const string USDGBP_SYMBOL = "USD/GBP";
        const string USDEUR_SYMBOL = "USD/EUR";
        const string EURGBP_SYMBOL = "EUR/GBP";
        const string EURUSD_SYMBOL = "EUR/USD";

        public static string ToFriendlyString(this CurrencyPair me)
        {
            switch (me)
            {
                case CurrencyPair.GBPUSD:
                    return GBPUSD_SYMBOL;
                case CurrencyPair.GBPEUR:
                    return GBPEUR_SYMBOL;
                case CurrencyPair.USDGBP:
                    return USDGBP_SYMBOL;
                case CurrencyPair.USDEUR:
                    return USDEUR_SYMBOL;
                case CurrencyPair.EURGBP:
                    return EURGBP_SYMBOL;
                case CurrencyPair.EURUSD:
                    return EURUSD_SYMBOL;
                default:
                    return GBPUSD_SYMBOL;
            }
        }

        public static CurrencyPair FromFriendlyString(string friendlyString)
        {
            switch (friendlyString)
            {
                case GBPUSD_SYMBOL:
                    return CurrencyPair.GBPUSD;
                case GBPEUR_SYMBOL:
                    return CurrencyPair.GBPEUR;
                case USDGBP_SYMBOL:
                    return CurrencyPair.USDGBP;
                case USDEUR_SYMBOL:
                    return CurrencyPair.USDEUR;
                case EURGBP_SYMBOL:
                    return CurrencyPair.EURGBP;
                case EURUSD_SYMBOL:
                    return CurrencyPair.EURUSD;
                default:
                    return CurrencyPair.GBPUSD;
            }
        }

        public static string GetBuyerWantCurrency(this CurrencyPair me)
        {
            return ToFriendlyString(me).Split('/')[0];
        }

        public static string GetSellerWantCurrency(this CurrencyPair me)
        {
            return ToFriendlyString(me).Split('/')[1];
        }
    }

    [BsonIgnoreExtraElements]
    [DataContract]
    public class Order : IComparable<Order>, IComparer<Order>, IEquatable<Order>
    {
        [JsonConstructor]
        public Order(string id, string userId, CurrencyPair pair, uint amount, double price, DateTime timestamp)
        {
            Id = id;
            UserId = userId;
            Pair = pair;
            Amount = amount;
            Price = price;
            if (timestamp == default(DateTime))
            {
                timestamp = DateTime.UtcNow;
            }
            Timestamp = timestamp;
        }

        public Order(string id, string userId, CurrencyPair pair, uint amount, double price)
        {
            Id = id;
            UserId = userId;
            Pair = pair;
            Amount = amount;
            Price = price;
            Timestamp = DateTime.UtcNow;
        }

        public Order(string userId, CurrencyPair pair, uint amount, double price)
        {
            Id = Guid.NewGuid().ToString();
            UserId = userId;
            Pair = pair;
            Amount = amount;
            Price = price;
            Timestamp = DateTime.UtcNow;
        }

        public Order(CurrencyPair pair, uint amount, double price)
        {
            Id = Guid.NewGuid().ToString();
            UserId = Id;
            Pair = pair;
            Amount = amount;
            Price = price;
            Timestamp = DateTime.UtcNow;
        }

        [BsonElement(elementName: "orderId")]
        [DataMember]
        public readonly string Id;

        [BsonElement(elementName: "userId")]

        [DataMember]
        public readonly string UserId;

        [BsonElement(elementName: "currencyPair")]
        [DataMember]
        [JsonConverter(typeof(StringEnumConverter))]
        public readonly CurrencyPair Pair;

        [BsonElement(elementName: "amount")]
        [DataMember]
        public readonly uint Amount;

        [BsonElement(elementName: "price")]
        [DataMember]
        public readonly double Price;

        [BsonElement(elementName: "timestamp")]
        [DataMember]
        public readonly DateTime Timestamp;

        #region Compare and Equals Methods
        public int Compare(Order x, Order y)
        {
            var xSize = x.Amount * x.Price;
            var ySize = y.Amount * y.Price;
            if (xSize > ySize)
            {
                return 1;
            }
            if (xSize == ySize)
            {
                return DateTime.Compare(x.Timestamp, y.Timestamp) * -1;
            }
            return -1;
        }

        public int CompareTo(Order other)
        {
            var xSize = Amount * Price;
            var ySize = other.Amount * other.Price;
            if (xSize > ySize)
            {
                return 1;
            }
            if (xSize == ySize)
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
                Price == other.Price &&
                Amount == other.Amount &&
                Pair == other.Pair &&
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
            return new { Id, Pair, Amount, Price, Timestamp, UserId }.GetHashCode();
        }
        #endregion
    }
}
