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

            var askAmount = 100;
            var askPrice = 100;
            var ask = new Order("user1", CurrencyPair.GBPUSD, (uint)askAmount, askPrice);
            var bid = new Order("user2", CurrencyPair.GBPUSD, (uint)askAmount/2, askPrice + 10);
            (var match, var leftOver) = service.SettleTrade(bid, ask);
            Assert.NotNull(match);
            Assert.NotNull(leftOver);
            Assert.True(match.Price == bid.Price);
            Assert.True(match.Amount == bid.Amount);
            Assert.True(leftOver.Amount == (ask.Amount - bid.Amount));
            Assert.True(leftOver.Price == ask.Price);
        }

        [Fact]
        public void ReturnTwoAsksThatSumsIsGreaterThanTheOriginalByTheSpreadOfTheBid()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var askAmount = 100;
            var askPrice = 100;
            var ask = new Order("user1", CurrencyPair.GBPUSD, (uint)askAmount, askPrice);
            var bid = new Order("user2", CurrencyPair.GBPUSD, 100, 150);

            var spread = bid.Price - askPrice;
            (var match, var leftOver) = service.SettleTrade(bid, ask);
            Assert.Equal(askPrice + spread, match.Price);
        }

        [Fact]
        public void ReturnAMatchAndNullWhenAskEqualsBid()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", CurrencyPair.GBPUSD, 100,100);
            var bid = new Order("user2", CurrencyPair.GBPUSD, 100,100);

            var spread = bid.Price - ask.Price;
            (var match, var leftOver) = service.SettleTrade(bid, ask);
            Assert.Null(leftOver);
            Assert.NotNull(match);
        }

        [Fact]
        public void ThrowIfPriceIsZero()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", CurrencyPair.GBPUSD, 100, 100);
            var bid = new Order("user2", CurrencyPair.GBPUSD, 100, 0);
            Assert.Throws<InvalidBidException>(() => service.SettleTrade(bid, ask));
        }

        [Fact]
        public void ThrowIfAmountIsZero()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", CurrencyPair.GBPUSD, 100, 0);
            var bid = new Order("user2", CurrencyPair.GBPUSD, 100, 100);
            Assert.Throws<InvalidAskException>(() => service.SettleTrade(bid, ask));
        }
    }
}
