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

            var ask = new Order("user1", 100, 30, "buyer");

            try
            {
                await service.AddAskAsync(ask);
            }
            catch(NotImplementedException)
            {
                // Expected, see line 13.
            }

            var dictionary = await stateManager.TryGetAsync<IReliableDictionary<string, Order>>(OrderBook.AskBookName);
            var actual = (await dictionary.Value.TryGetValueAsync(new MockTransaction(stateManager, 1), ask.Id)).Value;
            Assert.Equal(actual, ask);
        }

        [Fact]
        public async Task ThrowIfInvalidUserIdInOrder()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("", 100, 30, string.Empty);
            await Assert.ThrowsAsync<InvalidOrderException>(() => service.AddAskAsync(ask));
        }

        [Fact]
        public async Task ThrowIfZeroQuantityOrder()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", 0, 30, string.Empty);
            await Assert.ThrowsAsync<InvalidOrderException>(() => service.AddAskAsync(ask));
        }

        [Fact]
        public async Task ThrowIfZeroValueOrder()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", 30, 0, string.Empty);
            await Assert.ThrowsAsync<InvalidOrderException>(() => service.AddAskAsync(ask));
        }
    }
}
