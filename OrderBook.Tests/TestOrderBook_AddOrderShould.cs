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

            var context = GetMockContext();

            var service = new OrderBook(context, stateManager);

            var ask = new Order("user1", 100, 30, "buyer");

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

            var context = GetMockContext();

            var service = new OrderBook(context, stateManager);

            await Assert.ThrowsAsync<MaxOrdersExceededException>(async () =>
            {
                try
                {
                    for (int i = 0; i < 300; i++)
                    {
                        var ask = new Order(Guid.NewGuid().ToString(), 100, 30, "buyer");
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


        private static StatefulServiceContext GetMockContext()
        {
            //build ConfigurationSectionCollection
            var configSections = new ConfigurationSectionCollection();

            //Build ConfigurationSettings
            var configSettings = CreateConfigurationSettings(configSections);

            //add one ConfigurationSection
            ConfigurationSection configSection = CreateConfigurationSection("OrderBookConfig");
            configSections.Add(configSection);

            //add one Parameters entry
            ConfigurationProperty maxAsksParam = CreateConfigurationSectionParameters("MaxAsksPending", "200");
            configSection.Parameters.Add(maxAsksParam);

            ConfigurationProperty maxBidsPending = CreateConfigurationSectionParameters("MaxBidsPending", "200");
            configSection.Parameters.Add(maxBidsPending);

            //Build ConfigurationPackage
            ConfigurationPackage configPackage = CreateConfigurationPackage(configSettings);
            var context = new MockCodePackageActivationContext(
                "fabric:/MockApp",
                "MockAppType",
                "Code",
                "1.0.0.0",
                Guid.NewGuid().ToString(),
                @"C:\logDirectory",
                @"C:\tempDirectory",
                @"C:\workDirectory",
                "ServiceManifestName",
                "1.0.0.0")
            {
                ConfigurationPackage = configPackage
            };

            return MockStatefulServiceContextFactory.Create(context, "barry", new Uri("fabric:/barry/norman"), Guid.NewGuid(), 1);
        }
    }
}
