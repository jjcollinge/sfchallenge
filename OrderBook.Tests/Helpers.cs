using ServiceFabric.Mocks;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Text;
using static ServiceFabric.Mocks.MockConfigurationPackage;

namespace OrderBook.Tests
{
    public class Helpers
    {
        public static StatefulServiceContext GetMockContext()
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

            ConfigurationProperty orderBookDnsName = CreateConfigurationSectionParameters("Fulfillment_DnsName", "localhost");
            configSection.Parameters.Add(orderBookDnsName);

            ConfigurationProperty orderBookPort = CreateConfigurationSectionParameters("Fulfillment_Port", "8080");
            configSection.Parameters.Add(orderBookPort);

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
