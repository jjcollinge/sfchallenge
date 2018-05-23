using Common;
using Microsoft.ServiceFabric.Data.Collections;
using ServiceFabric.Mocks;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Threading.Tasks;
using Xunit;
using static ServiceFabric.Mocks.MockConfigurationPackage;

namespace OrderBook.Tests
{
    public class TestOrderBook_AddOrderShould
    {
        // IReliableCollection Notifications are not implemented in
        // mocks so expected 'NotImplementedExceptions' to be thrown.

        [Fact]
        public async Task AddValidAskToDictionary()
        {
            var stateManager = new MockReliableStateManager();
            var context = Helpers.GetMockContext();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", "buyer", CurrencyPair.GBPUSD, 100, 30);

            try
            {
                await service.AddAskAsync(ask);
            }
            catch (NotImplementedException)
            {
                // Expected, see line 13.
            }

            var dictionary = await stateManager.TryGetAsync<IReliableDictionary<string, Order>>(OrderBook.AskBookName);
            var actual = (await dictionary.Value.TryGetValueAsync(new MockTransaction(stateManager, 1), ask.Id)).Value;
            Assert.Equal(actual, ask);
        }

        [Fact]
        public async Task AddAskToFullDictionary_ExpectMaxOrderExceededException()
        {
            var stateManager = new MockReliableStateManager();
            var context = Helpers.GetMockContext();
            var service = new OrderBook(context, stateManager);

            await Assert.ThrowsAsync<MaxOrdersExceededException>(async () =>
            {
                try
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        var ask = new Order("buyer", CurrencyPair.GBPUSD, 100, 30);
                        await service.AddAskAsync(ask);
                    }

                }
                catch (NotImplementedException)
                {
                    // Expected, see line 13.
                }
            });
        }

        [Fact]
        public async Task ThrowIfInvalidUserIdInOrder()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("", CurrencyPair.GBPUSD, 100, 30);
            await Assert.ThrowsAsync<InvalidOrderException>(() => service.AddAskAsync(ask));
        }

        [Fact]
        public async Task ThrowIfZeroAmountOrder()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", CurrencyPair.GBPUSD, 0, 30);
            await Assert.ThrowsAsync<InvalidOrderException>(() => service.AddAskAsync(ask));
        }

        [Fact]
        public async Task ThrowIfZeroPriceOrder()
        {
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", CurrencyPair.GBPUSD, 30, 0);
            await Assert.ThrowsAsync<InvalidOrderException>(() => service.AddAskAsync(ask));
        }

   }
}
