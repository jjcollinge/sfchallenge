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
        private const int requestTimeout = 20;

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

            // This is a simple proxy middleware that will detect whether a service
            // is partitioned or not and forward the request accordingly. This is 
            // intended as the entry point for the load tests, please do not edit.
            app.Run(async (context) =>
            {
                // REQUIRED, DO NOT REMOVE.
                FraudCheck.Check();

                // If no path is provided, error.
                if (context.Request.Path == "/")
                {
                    context.Response.StatusCode = 502;
                    await context.Response.WriteAsync($"Invalid Input");
                    return;
                }

                // If the path matches the OrderBook api
                if (IsOrderBookServiceRequest(context))
                {
                    PartitionScheme partitioningScheme = await GetOrderBookParititoiningScheme();

                    var currency = GetAndRemoveCurrencyFromRequest(ref context);  // Expects paths of the form '/api/orders/bid/GBPUSD'

                    // If the OrderBook is a singleton
                    if (partitioningScheme == PartitionScheme.Singleton)
                    {
                        string forwardingUrl = ForwardingUrl(context, "OrderBook");
                        await ProxyRequestHelper(context, forwardingUrl);
                        
                        return;
                    }

                    // If the OrderBook is using named partitions
                    if (partitioningScheme == PartitionScheme.Named && currency != string.Empty)
                    {
                        string forwardingUrl = ForwardingUrl(context, "OrderBook");
                        var partitionedEndpoint = $"{forwardingUrl}?PartitionKey={currency}&PartitionKind=Named&Timeout={requestTimeout}";
                        await ProxyRequestHelper(context, partitionedEndpoint);
                        return;
                    }

                    throw new InvalidOperationException("OrderBook must use either singleton or named partition scheme");
                }

                // If the path matches the Fulfillment api
                if (IsFulfilmentServiceRequest(context))
                {
                    // The Fulfillment service is Int64Range partitioned by default
                    // so we don't handle the singleton case.
                    string forwardingUrl = ForwardingUrl(context, "Fulfillment");
                    var partitionedEndpoint = new Uri($"{forwardingUrl}?PartitionKey=1&PartitionKind=Int64Range&Timeout={requestTimeout}");

                    // All requests through the gateway will hit a single partition of the 
                    // fulfillment service. This is because we only use it to create 
                    // our test users.
                    using (var requestMessage = context.CreateProxyHttpRequest(partitionedEndpoint))
                    {
                        using (var responseMessage = await context.SendProxyHttpRequest(requestMessage))
                        {
                            await context.CopyProxyHttpResponse(responseMessage);
                            return;
                        }
                    }
                }

                // If the path matches the Logger api
                if (IsLoggerServiceRequest(context))
                {
                    string forwardingUrl = ForwardingUrl(context, "Logger");

                    // All requests through the gateway will hit a single partition of the 
                    // logger service. This is because we only use it to query the DB count.
                    var partitionedEndpoint = new Uri($"{forwardingUrl}?PartitionKey=1&PartitionKind=Int64Range&Timeout={requestTimeout}");
                    using (var requestMessage = context.CreateProxyHttpRequest(partitionedEndpoint))
                    {
                        using (var responseMessage = await context.SendProxyHttpRequest(requestMessage))
                        {
                            await context.CopyProxyHttpResponse(responseMessage);
                            return;
                        }
                    }
                }
                
                throw new InvalidOperationException("Unexpected request path. This gateway only matches for the OrderBook, Fulfillment and Logger apis.");
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

        private static async Task ProxyRequestHelperWithStringContent(HttpContext context, StringContent content, string forwardingUrl)
        {
            using (var requestMessage = context.CreateProxyHttpRequest(new Uri(forwardingUrl), content))
            {
                using (var responseMessage = await context.SendProxyHttpRequest(requestMessage))
                {
                    await context.CopyProxyHttpResponse(responseMessage);
                }
            }
        }

        private string GetAndRemoveCurrencyFromRequest(ref HttpContext context)
        {
            if (context.Request.Path.Value.Contains(CurrencyPairExtensions.GBPUSD_SYMBOL))
            {
                context.Request.Path = context.Request.Path.Value.Replace(CurrencyPairExtensions.GBPUSD_SYMBOL, "");
                return CurrencyPairExtensions.GBPUSD_SYMBOL;
            }
            if (context.Request.Path.Value.Contains(CurrencyPairExtensions.GBPEUR_SYMBOL))
            {
                context.Request.Path = context.Request.Path.Value.Replace(CurrencyPairExtensions.GBPEUR_SYMBOL, "");
                return CurrencyPairExtensions.GBPEUR_SYMBOL;
            }
            if (context.Request.Path.Value.Contains(CurrencyPairExtensions.USDGBP_SYMBOL))
            {
                context.Request.Path = context.Request.Path.Value.Replace(CurrencyPairExtensions.USDGBP_SYMBOL, "");
                return CurrencyPairExtensions.USDGBP_SYMBOL;
            }
            if (context.Request.Path.Value.Contains(CurrencyPairExtensions.USDEUR_SYMBOL))
            {
                context.Request.Path = context.Request.Path.Value.Replace(CurrencyPairExtensions.USDEUR_SYMBOL, "");
                return CurrencyPairExtensions.USDEUR_SYMBOL;
            }
            if (context.Request.Path.Value.Contains(CurrencyPairExtensions.EURGBP_SYMBOL))
            {
                context.Request.Path = context.Request.Path.Value.Replace(CurrencyPairExtensions.EURGBP_SYMBOL, "");
                return CurrencyPairExtensions.EURGBP_SYMBOL;
            }
            if (context.Request.Path.Value.Contains(CurrencyPairExtensions.EURUSD_SYMBOL))
            {
                context.Request.Path = context.Request.Path.Value.Replace(CurrencyPairExtensions.EURUSD_SYMBOL, "");
                return CurrencyPairExtensions.EURUSD_SYMBOL;
            }
            return CurrencyPairExtensions.GBPUSD_SYMBOL;
        }

        private static bool IsOrderBookServiceRequest(HttpContext context)
        {
            return context.Request.Path.Value.Contains("api/orders");
        }

        private static bool IsFulfilmentServiceRequest(HttpContext context)
        {
            return context.Request.Path.Value.Contains("/api/user") || context.Request.Path.Value.Contains("/api/trades");
        }

        private bool IsLoggerServiceRequest(HttpContext context)
        {
            return context.Request.Path.Value.Contains("api/logger");
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
            return endpoint.TrimEnd('/'); ;
        }
    }
    public static class Extensions
    {
        public static HttpRequestMessage CreateProxyHttpRequest(this HttpContext context, Uri uri, StringContent content = null)
        {
            var request = context.Request;

            var requestMessage = new HttpRequestMessage();
            var requestMethod = request.Method;
            if (!HttpMethods.IsGet(requestMethod) &&
                !HttpMethods.IsHead(requestMethod) &&
                !HttpMethods.IsDelete(requestMethod) &&
                !HttpMethods.IsTrace(requestMethod))
            {
                if (content == null)
                {
                    var streamContent = new StreamContent(request.Body);
                    requestMessage.Content = streamContent;
                }
                else
                {
                    requestMessage.Content = content;
                }
            }

            // Copy the request headers
            foreach (var header in request.Headers)
            {
                if (header.Key.ToLower().Contains("content-length") && content != null)
                {
                    continue;
                }
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


