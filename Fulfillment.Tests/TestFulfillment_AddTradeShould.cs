using Common;
using Microsoft.ServiceFabric.Data.Collections;
using ServiceFabric.Mocks;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static ServiceFabric.Mocks.MockConfigurationPackage;

namespace Fulfillment.Tests
{
    public class TestFulfillment_AddTradeShould
    {
        [Fact]
        public async Task AddValidTradeToQueue()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);
            var ask = new Order("user1", CurrencyPair.GBPUSD, 10, 10);
            var bid = new Order("user2", CurrencyPair.GBPUSD, 10, 10);
            var settlement = new Order(ask.Pair, bid.Amount, ask.Price);
            var request = new TradeRequestModel
            {
                Ask = ask,
                Bid = bid,
                Settlement = settlement
            };

            var tradeId = await service.AddTradeAsync(request);
            var expected = new Trade(tradeId, ask, bid, settlement);

            var queue = await stateManager.TryGetAsync<IReliableConcurrentQueue<Trade>>(Fulfillment.TradeQueueName);
            var actual = (await queue.Value.TryDequeueAsync(new MockTransaction(stateManager, 1))).Value;
            Assert.True(expected.Equals(actual));
        }

        [Fact]
        public async Task AddTooManyTradesToQueue()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);
            var ask = new Order("user1", CurrencyPair.GBPUSD, 10, 10);
            var bid = new Order("user2", CurrencyPair.GBPUSD, 10, 10);
            var settlement = new Order(ask.Pair, bid.Amount, ask.Price);
            var request = new TradeRequestModel
            {
                Ask = ask,
                Bid = bid,
                Settlement = settlement
            };
            await Assert.ThrowsAsync<MaxPendingTradesExceededException>(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    await service.AddTradeAsync(request);
                }
            });
        }

        [Fact]
        public async Task ThrowIfAskQuantityIsLowerThanBidQuantity()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);

            var ask = new Order("user1", CurrencyPair.GBPUSD, 5, 10);
            var bid = new Order("user2", CurrencyPair.GBPUSD, 100, 10);
            var request = new TradeRequestModel
            {
                Ask = ask,
                Bid = bid,
            };

            await Assert.ThrowsAsync<InvalidTradeRequestException>(() => service.AddTradeAsync(request));
        }

        [Fact]
        public async Task ThrowIfAskValueIsHigherThanBidValue()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);

            var ask = new Order("user1", CurrencyPair.GBPUSD, 40, 150);
            var bid = new Order("user2", CurrencyPair.GBPUSD, 40, 100);
            var settlement = new Order(ask.Pair, bid.Amount, ask.Price);
            var request = new TradeRequestModel
            {
                Ask = ask,
                Bid = bid,
                Settlement = settlement
            };

            await Assert.ThrowsAsync<InvalidTradeRequestException>(() => service.AddTradeAsync(request));
        }
    }
}
