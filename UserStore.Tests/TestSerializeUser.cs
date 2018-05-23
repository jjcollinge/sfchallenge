using Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Xunit;

namespace UserStore.Tests
{
    public class TestSerializeUser
    {
        [Fact]
        public void Serialize_Deserialize()
        {
            var currencyAmounts = new Dictionary<string, double>();
            currencyAmounts.Add(CurrencyPair.GBPUSD.GetBuyerWantCurrency(), 100);
            var user = new User("42", "Anders", currencyAmounts, new List<string>()
                {
                    "t1"
                });

            var serializer = new DataContractSerializer(typeof(User));
            User userDeserialized;

            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, user);
                stream.Position = 0;
                userDeserialized = (User)serializer.ReadObject(stream);
            }

            Assert.Equal(user.Username, userDeserialized.Username);
            Assert.Equal(user.LatestTrades.Count(), userDeserialized.LatestTrades.Count());
        }

        [Fact]
        public void Serialize_Deserialize_List()
        {
            var currencyAmounts = new Dictionary<string, double>();
            currencyAmounts.Add(CurrencyPair.GBPUSD.GetBuyerWantCurrency(), 21);

            var currencyAmounts1 = new Dictionary<string, double>();
            currencyAmounts1.Add(CurrencyPair.GBPUSD.GetBuyerWantCurrency(), 455);
            var users = new List<User>()
            {
                new User("42", "Anders", currencyAmounts, new List<string>()
                {
                    "t1"
                }),
                new User("43", "Joni", currencyAmounts1, null),
            };

            var serializer = new DataContractSerializer(typeof(IEnumerable<User>));
            IEnumerable<User> usersDeserialized;

            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, users);
                stream.Position = 0;
                usersDeserialized = (IEnumerable<User>)serializer.ReadObject(stream);
            }

            Assert.Equal(users.Count(), usersDeserialized.Count());
        }
    }
}