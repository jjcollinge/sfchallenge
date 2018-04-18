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
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order
            {
                UserId = "user1",
                Quantity = 100,
                Value = 100,
            };
            var bid = new Order
            {
                UserId = "user2",
                Quantity = 50,
                Value = 60,
            };

            (var match, var leftOver) = service.SplitAsk(bid, ask);
            Assert.True(ask.Value <= (match.Value + leftOver.Value));
            Assert.Equal((match.Quantity + leftOver.Quantity), ask.Quantity);
        }

        [Fact]
        public void ReturnTwoAsksThatSumsIsGreaterThanTheOriginalByTheSpreadOfTheBid()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order
            {
                UserId = "user1",
                Quantity = 100,
                Value = 100,
            };
            var bid = new Order
            {
                UserId = "user2",
                Quantity = 100,
                Value = 150,
            };

            var spread = bid.Value - ask.Value;
            (var match, var leftOver) = service.SplitAsk(bid, ask);
            Assert.Equal(ask.Value + spread, match.Value + leftOver.Value);
        }

        [Fact]
        public void ReturnAMatchAndNullWhenAskEqualsBid()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order
            {
                UserId = "user1",
                Quantity = 100,
                Value = 100,
            };
            var bid = new Order
            {
                UserId = "user2",
                Quantity = 100,
                Value = 100,
            };

            var spread = bid.Value - ask.Value;
            (var match, var leftOver) = service.SplitAsk(bid, ask);
            Assert.Null(leftOver);
            Assert.NotNull(match);
        }

        [Fact]
        public void ThrowIfValueIsZero()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order
            {
                UserId = "user1",
                Quantity = 100,
                Value = 100,
            };
            var bid = new Order
            {
                UserId = "user2",
                Quantity = 100,
                Value = 0,
            };

            Assert.Throws<InvalidOrderException>(() => service.SplitAsk(bid, ask));
        }

        [Fact]
        public void ThrowIfQuantityIsZero()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order
            {
                UserId = "user1",
                Quantity = 100,
                Value = 0,
            };
            var bid = new Order
            {
                UserId = "user2",
                Quantity = 100,
                Value = 100,
            };

            Assert.Throws<InvalidOrderException>(() => service.SplitAsk(bid, ask));
        }
    }
}
