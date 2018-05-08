using ServiceFabric.Mocks;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Text;
using static ServiceFabric.Mocks.MockConfigurationPackage;

namespace Fulfillment.Tests
{
    public class Helpers
    {
        public static StatefulServiceContext GetMockContext()
        {
            //build ConfigurationSectionCollection
            var configSections = new ConfigurationSectionCollection();

            //Build ConfigurationSettings
            var configSettings = CreateConfigurationSettings(configSections);

            ConfigurationSection configSection = CreateConfigurationSection("FulfillmentConfig");
            configSections.Add(configSection);

            ConfigurationProperty orderBookDnsName = CreateConfigurationSectionParameters("OrderBook_DnsName", "localhost");
            configSection.Parameters.Add(orderBookDnsName);

            ConfigurationProperty orderBookPort = CreateConfigurationSectionParameters("OrderBook_Port", "8080");
            configSection.Parameters.Add(orderBookPort);

            ConfigurationProperty loggerDnsName = CreateConfigurationSectionParameters("Logger_DnsName", "localhost");
            configSection.Parameters.Add(loggerDnsName);

            ConfigurationProperty loggerPort = CreateConfigurationSectionParameters("Logger_Port", "9000");
            configSection.Parameters.Add(loggerPort);

            ConfigurationProperty maxTradesPending = CreateConfigurationSectionParameters("MaxTradesPending", "10");
            configSection.Parameters.Add(maxTradesPending);

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
