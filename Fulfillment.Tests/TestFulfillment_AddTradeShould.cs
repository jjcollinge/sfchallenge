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
            var ask = new Order(10, 10, "user1");
            var bid = new Order(10, 10, "user2");
            var request = new TradeRequestModel
            {
                Ask = ask,
                Bid = bid,
            };

            var tradeId = await service.AddTradeAsync(request);
            var expected = new Trade(tradeId, ask, bid);

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
            var ask = new Order(10, 10, "user1");
            var bid = new Order(10, 10, "user2");
            var request = new TradeRequestModel
            {
                Ask = ask,
                Bid = bid,
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

            var ask = new Order(10, 10, "user1");
            var bid = new Order(10, 100, "user2");
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

            var ask = new Order(60, 100, "user1");
            var bid = new Order(40, 100, "user2");
            var request = new TradeRequestModel
            {
                Ask = ask,
                Bid = bid,
            };

            await Assert.ThrowsAsync<InvalidTradeRequestException>(() => service.AddTradeAsync(request));
        }
    }
}
