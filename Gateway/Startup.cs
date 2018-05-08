using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway
{
    public class Startup
    {
        private const string ForwarderForHeader = "X-Forwarded-Host";
        private const string ItemTypeHeader = "x-item-type";


        public void ConfigureServices(IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Here is a simple middleware to simulate checking for fraudulent requests (CPU intensive)
            // and then route requests to a partition.
            app.Run(async (context) =>
            {
                //Complete a CPU intensive fraud check. 
                FraudCheck.Check();

                string forwardingUrl = ForwardingUrl(context);

                // If requests have a header of 'x-item-type' then redirect them to the correct partition 
                // using the 'Named' partition scheme in Service Fabric and it's Reverse proxy.
                // https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-reverseproxy
                var exists = context.Request.Headers.TryGetValue(ItemTypeHeader, out var itemType);
                if (exists)
                {
                    var partitionedEndpoint = $"{forwardingUrl}&PartitionKey={itemType.ToString()}&PartitionKind=Named";
                    context.Response.StatusCode = 307;
                    context.Response.Headers.Add("Location", partitionedEndpoint);
                    await context.Response.WriteAsync($"Redirect issused with partitioning enabled. Endpoint: {partitionedEndpoint}");
                }
                else
                {
                    context.Response.StatusCode = 307;
                    context.Response.Headers.Add("Location", forwardingUrl);
                    await context.Response.WriteAsync($"Redirect issused without partitioning. Endpoint: {forwardingUrl}");
                }
            });
        }

        private static string ForwardingUrl(HttpContext context)
        {
            var forwarderForExists = context.Request.Headers.TryGetValue(ForwarderForHeader, out var forwarderFor);

            var sourceHost = context.Request.Host.Value;
            if (forwarderForExists)
            {
                sourceHost = forwarderFor;
            }
            string applicationInstanceName = Gateway.StaticContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "");
            string serviceName = "OrderBook";
            var endpoint = $"http://{sourceHost}/{applicationInstanceName}/{serviceName}{context.Request.Path.Value}";
            if (context.Request.QueryString.HasValue)
            {
                endpoint += context.Request.QueryString.Value;
            }

            return endpoint;
        }
    }
}
