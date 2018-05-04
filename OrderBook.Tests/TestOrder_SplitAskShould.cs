using Common;
using ServiceFabric.Mocks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace OrderBook.Tests
{
    public class TestOrder_SplitAskShould
    {
        [Fact]
        public void ReturnTwoAsksThatSumsIsEqualToOrGreaterThanOriginal()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var askQuantity = 100;
            var askValue = 100;
            var ask = new Order("user1", askValue, askQuantity, string.Empty);
            var bid = new Order("user2", askValue + 10, askQuantity/2, string.Empty);
            (var match, var leftOver) = service.SplitAsk(bid, ask);
            Assert.True(match.Value == bid.Value);
            Assert.True(match.Quantity == bid.Quantity);
            Assert.True(leftOver.Quantity == (ask.Quantity - bid.Quantity));
            Assert.True(leftOver.Value == ask.Value);
        }

        [Fact]
        public void ReturnTwoAsksThatSumsIsGreaterThanTheOriginalByTheSpreadOfTheBid()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var askQuantity = 100;
            var askValue = 100;
            var ask = new Order("user1", askValue, askQuantity, string.Empty);
            var bid = new Order("user2", 100, 150, string.Empty);

            var spread = bid.Value - askValue;
            (var match, var leftOver) = service.SplitAsk(bid, ask);
            Assert.Equal(askValue + spread, match.Value);
        }

        [Fact]
        public void ReturnAMatchAndNullWhenAskEqualsBid()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", 100, 100, string.Empty);
            var bid = new Order("user2", 100, 100, string.Empty);

            var spread = bid.Value - ask.Value;
            (var match, var leftOver) = service.SplitAsk(bid, ask);
            Assert.Null(leftOver);
            Assert.NotNull(match);
        }

        [Fact]
        public void ThrowIfValueIsZero()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", 100, 100, string.Empty);
            var bid = new Order("user2", 100, 0, string.Empty);
            Assert.Throws<InvalidBidException>(() => service.SplitAsk(bid, ask));
        }

        [Fact]
        public void ThrowIfQuantityIsZero()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", 100, 0, string.Empty);
            var bid = new Order("user2", 100, 100, string.Empty);
            Assert.Throws<InvalidAskException>(() => service.SplitAsk(bid, ask));
        }
    }
}
