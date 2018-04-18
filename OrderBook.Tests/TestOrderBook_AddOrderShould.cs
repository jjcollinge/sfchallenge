using Common;
using Microsoft.ServiceFabric.Data.Collections;
using ServiceFabric.Mocks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace OrderBook.Tests
{
    public class TestOrderBook_AddOrderShould
    {
        // IReliableCollection Notifications are not implemented in
        // mocks so expected 'NotImplementedExceptions' to be thrown.

        [Fact]
        public async Task AddValidAskToDictionary()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order
            {
                UserId = "user1",
                Quantity = 100,
                Value = 30,
            };

            try
            {
                await service.AddAskAsync(ask);
            }
            catch(NotImplementedException)
            {
                // Expected, see line 13.
            }

            var dictionary = await stateManager.TryGetAsync<IReliableDictionary<int, Queue<Order>>>(OrderBook.AskBookName);
            var queue = (await dictionary.Value.TryGetValueAsync(new MockTransaction(stateManager, 1), (int)ask.Value)).Value;
            var actual = queue.Dequeue();
            Assert.Equal(actual, ask);
        }

        [Fact]
        public async Task ThrowIfInvalidUserIdInOrder()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order
            {
                UserId = "",
                Quantity = 100,
                Value = 30,
            };

            await Assert.ThrowsAsync<InvalidOrderException>(() => service.AddAskAsync(ask));
        }

        [Fact]
        public async Task ThrowIfZeroQuantityOrder()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order
            {
                UserId = "user1",
                Quantity = 0,
                Value = 30,
            };

            await Assert.ThrowsAsync<InvalidOrderException>(() => service.AddAskAsync(ask));
        }

        [Fact]
        public async Task ThrowIfZeroValueOrder()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order
            {
                UserId = "user1",
                Quantity = 30,
                Value = 0,
            };

            await Assert.ThrowsAsync<InvalidOrderException>(() => service.AddAskAsync(ask));
        }
    }
}
