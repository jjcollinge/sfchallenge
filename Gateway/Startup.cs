using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Gateway
{
    public class Startup
    {
        private const string ForwarderForHeader = "X-Forwarded-Host";
        public static HttpClient Client = new HttpClient();

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
                if (context.Request.Path == "/")
                {
                    context.Response.StatusCode = 502;
                    await context.Response.WriteAsync($"Invalid Input");
                    return;
                }

                //Complete a CPU intensive fraud check. 
                FraudCheck.Check();


                if (IsOrderBookServiceRequest(context))
                {
                    PartitionScheme partitioningScheme = await GetOrderBookParititoiningScheme();

                    var bodyStream = new StreamReader(context.Request.Body);
                    bodyStream.BaseStream.Seek(0, SeekOrigin.Begin);
                    var bodyText = bodyStream.ReadToEnd();
                    var order = JsonConvert.DeserializeObject<Order>(bodyText);
                    
                    if (partitioningScheme == PartitionScheme.Singleton)
                    {
                        //Handle bid and ask requests without parition
                        string forwardingUrl = ForwardingUrl(context, "OrderBook");
                        await ProxyRequestHelper(context, forwardingUrl);
                        return;
                    }

                    // If requests have a header of 'x-item-type' then redirect them to the correct partition 
                    // using the 'Named' partition scheme in Service Fabric and it's Reverse proxy.
                    // https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-reverseproxy
                    var itemTypeExists = true;
                    var itemType = "";
                    if (partitioningScheme == PartitionScheme.Named && itemTypeExists)
                    {
                        //Handle bid and ask requests with parition
                        string forwardingUrl = ForwardingUrl(context, "OrderBook");
                        var partitionedEndpoint = $"{forwardingUrl}?PartitionKey={itemType.ToString()}&PartitionKind=Named";
                        await ProxyRequestHelper(context, partitionedEndpoint);
                        return;
                    }

                    throw new InvalidOperationException("Expected either Named of singleton partitioning scheme for the orderbook");
                }

                if (IsFulfilmentServiceRequest(context))
                {
                    //Handle user or trade requests with/without parition
                    string forwardingUrl = ForwardingUrl(context, "Fulfillment");

                    var partitionedEndpoint = new Uri($"{forwardingUrl}?PartitionKey=1&PartitionKind=Int64Range");
                    using (var requestMessage = context.CreateProxyHttpRequest(partitionedEndpoint))
                    {
                        using (var responseMessage = await context.SendProxyHttpRequest(requestMessage))
                        {
                            await context.CopyProxyHttpResponse(responseMessage);
                            return;
                        }
                    }
                }

                throw new InvalidOperationException("Unexpected request body. This gateway only proxies for Fulfillment or OrderBook services");

            });
        }

        private static async Task<System.Fabric.Description.PartitionScheme> GetOrderBookParititoiningScheme()
        {
            FabricClient client = new FabricClient();
            var serviceDesc = await client.ServiceManager.GetServiceDescriptionAsync(new Uri($"{Gateway.StaticContext.CodePackageActivationContext.ApplicationName}/OrderBook"));
            var partitioningScheme = serviceDesc.PartitionSchemeDescription.Scheme;
            return partitioningScheme;
        }

        private static async Task ProxyRequestHelper(HttpContext context, string forwardingUrl)
        {
            using (var requestMessage = context.CreateProxyHttpRequest(new Uri(forwardingUrl)))
            {
                using (var responseMessage = await context.SendProxyHttpRequest(requestMessage))
                {
                    await context.CopyProxyHttpResponse(responseMessage);
                }
            }
        }

        private static bool IsOrderBookServiceRequest(HttpContext context)
        {
            return context.Request.Path.Value.Contains("api/orders");
        }

        private static bool IsFulfilmentServiceRequest(HttpContext context)
        {
            return context.Request.Path.Value.Contains("/api/user") || context.Request.Path.Value.Contains("/api/trades");
        }

        private static string ForwardingUrl(HttpContext context, string serviceName)
        {
            var forwarderForExists = context.Request.Headers.TryGetValue(ForwarderForHeader, out var forwarderFor);

            var sourceHost = context.Request.Host.Value;
            if (forwarderForExists)
            {
                sourceHost = forwarderFor;
            }
            string applicationInstanceName = Gateway.StaticContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "");
            var endpoint = $"http://{sourceHost}/{applicationInstanceName}/{serviceName}{context.Request.Path.Value}";
            if (context.Request.QueryString.HasValue)
            {
                endpoint += context.Request.QueryString.Value;
            }

            return endpoint;
        }
    }
    public static class Extensions
    {

        public static HttpRequestMessage CreateProxyHttpRequest(this HttpContext context, Uri uri)
        {
            var request = context.Request;

            var requestMessage = new HttpRequestMessage();
            var requestMethod = request.Method;
            if (!HttpMethods.IsGet(requestMethod) &&
                !HttpMethods.IsHead(requestMethod) &&
                !HttpMethods.IsDelete(requestMethod) &&
                !HttpMethods.IsTrace(requestMethod))
            {
                var streamContent = new StreamContent(request.Body);
                requestMessage.Content = streamContent;
            }

            // Copy the request headers
            foreach (var header in request.Headers)
            {
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
                {
                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            requestMessage.Headers.Host = uri.Authority;
            requestMessage.RequestUri = uri;
            requestMessage.Method = new HttpMethod(request.Method);

            return requestMessage;
        }

        public static Task<HttpResponseMessage> SendProxyHttpRequest(this HttpContext context, HttpRequestMessage requestMessage)
        {
            if (requestMessage == null)
            {
                throw new ArgumentNullException(nameof(requestMessage));
            }

            return Startup.Client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        }

        private const int StreamCopyBufferSize = 81920;

        public static async Task CopyProxyHttpResponse(this HttpContext context, HttpResponseMessage responseMessage)
        {
            if (responseMessage == null)
            {
                throw new ArgumentNullException(nameof(responseMessage));
            }

            var response = context.Response;

            response.StatusCode = (int)responseMessage.StatusCode;
            foreach (var header in responseMessage.Headers)
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in responseMessage.Content.Headers)
            {
                response.Headers[header.Key] = header.Value.ToArray();
            }

            // SendAsync removes chunking from the response. This removes the header so it doesn't expect a chunked response.
            response.Headers.Remove("transfer-encoding");

            using (var responseStream = await responseMessage.Content.ReadAsStreamAsync())
            {
                await responseStream.CopyToAsync(response.Body, StreamCopyBufferSize, context.RequestAborted);
            }
        }
    }
}


