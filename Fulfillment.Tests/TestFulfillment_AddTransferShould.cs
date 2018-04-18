using Common;
using Microsoft.ServiceFabric.Data.Collections;
using ServiceFabric.Mocks;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fulfillment.Tests
{
    public class TestFulfillment_AddTransferShould
    {
        [Fact]
        public async Task AddValidTransferToQueue()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);

            var ask = new Order
            {
                Quantity = 10,
                Value = 10,
                UserId = "user1",
            };
            var bid = new Order
            {
                Quantity = 10,
                Value = 10,
                UserId = "user2",
            };
            var request = new TransferRequestModel
            {
                Ask = ask,
                Bid = bid,
            };

            var transferId = await service.AddTransferAsync(request);
            var expected = new Transfer(transferId, ask, bid);

            var queue = await stateManager.TryGetAsync<IReliableConcurrentQueue<Transfer>>(Fulfillment.TransferQueueName);
            var actual = (await queue.Value.TryDequeueAsync(new MockTransaction(stateManager, 1))).Value;
            Assert.True(expected.Equals(actual));
        }

        [Fact]
        public async Task ThrowIfAskQuantityIsLowerThanBidQuantity()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);

            var ask = new Order
            {
                Quantity = 10,
                Value = 10,
                UserId = "user1",
            };
            var bid = new Order
            {
                Quantity = 100,
                Value = 10,
                UserId = "user2",
            };
            var request = new TransferRequestModel
            {
                Ask = ask,
                Bid = bid,
            };

            Assert.ThrowsAsync<InvalidTransferRequestException>(() => service.AddTransferAsync(request));
        }

        [Fact]
        public async Task ThrowIfAskValueIsHigherThanBidValue()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);

            var ask = new Order
            {
                Quantity = 100,
                Value = 10,
                UserId = "user1",
            };
            var bid = new Order
            {
                Quantity = 100,
                Value = 40,
                UserId = "user2",
            };
            var request = new TransferRequestModel
            {
                Ask = ask,
                Bid = bid,
            };

            Assert.ThrowsAsync<InvalidTransferRequestException>(() => service.AddTransferAsync(request));
        }
    }
}
