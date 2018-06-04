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

            ConfigurationSection configSection = CreateConfigurationSection("ClusterConfig");
            configSections.Add(configSection);

            ConfigurationProperty reverseProxyPort = CreateConfigurationSectionParameters("ReverseProxy_Port", "19081");
            configSection.Parameters.Add(reverseProxyPort);

            ConfigurationProperty maxTradesPending = CreateConfigurationSectionParameters("MaxTradesPending", "10");
            configSection.Parameters.Add(maxTradesPending);

            ConfigurationProperty appInsightsKey = CreateConfigurationSectionParameters("Admin_AppInsights_InstrumentationKey", "");
            configSection.Parameters.Add(appInsightsKey);

            ConfigurationProperty teamName = CreateConfigurationSectionParameters("TeamName", "");
            configSection.Parameters.Add(teamName);


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
