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

            ConfigurationSection orderBookConfig = CreateConfigurationSection("OrderBookConfig");
            configSections.Add(orderBookConfig);

            ConfigurationProperty maxAsksParam = CreateConfigurationSectionParameters("MaxAsksPending", "200");
            orderBookConfig.Parameters.Add(maxAsksParam);

            ConfigurationProperty maxBidsPending = CreateConfigurationSectionParameters("MaxBidsPending", "200");
            orderBookConfig.Parameters.Add(maxBidsPending);

            ConfigurationProperty appInsightsKey = CreateConfigurationSectionParameters("Admin_AppInsights_InstrumentationKey", "");
            orderBookConfig.Parameters.Add(appInsightsKey);

            ConfigurationProperty teamName = CreateConfigurationSectionParameters("TeamName", "");
            orderBookConfig.Parameters.Add(teamName);

            ConfigurationSection clusterConfig = CreateConfigurationSection("ClusterConfig");
            configSections.Add(clusterConfig);

            ConfigurationProperty reverseProxyPort = CreateConfigurationSectionParameters("ReverseProxy_Port", "19081");
            clusterConfig.Parameters.Add(reverseProxyPort);

            //ConfigurationSection configSection2 = CreateConfigurationSection("CosmosDB");
            //configSections.Add(clusterConfig);

            //ConfigurationProperty cosmosDBConnectionString = CreateConfigurationSectionParameters("ConnectionString", "");
            //clusterConfig.Parameters.Add(cosmosDBConnectionString);

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
