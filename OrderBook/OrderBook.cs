using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Data.Notifications;
using System.Net.Http;
using Common;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using System.Net.Http.Headers;
using System.Fabric.Description;
using System.Net;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.ServiceFabric;

namespace OrderBook
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    public class OrderBook : StatefulService
    {
        // Names used as key to identify reliable dictionary in the state manager
        public const string AskBookName = "books/asks";
        public const string BidBookName = "books/bids";

        // Order sets for both asks (sales) and bids (buys)
        private OrderSet asks;
        private OrderSet bids;

        private static readonly HttpClient client = new HttpClient();
        private string reverseProxyPort;

        private int maxPendingAsks;
        private int maxPendingBids;
        private const int backOffDurationInSec = 2;

        public OrderBook(StatefulServiceContext context)
            : base(context)
        {
            Init();
            this.asks = new OrderSet(this.StateManager, AskBookName);
            this.bids = new OrderSet(this.StateManager, BidBookName);
        }

        // This constructor is used during unit testing by setting a mock IReliableStateManagerReplica
        public OrderBook(StatefulServiceContext context, IReliableStateManagerReplica reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        {
            Init();
            this.asks = new OrderSet(reliableStateManagerReplica, AskBookName);
            this.bids = new OrderSet(reliableStateManagerReplica, BidBookName);
        }

        private void Init()
        {
            // Get configuration from our PackageRoot/Config/Setting.xml file
            var configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            reverseProxyPort = configurationPackage.Settings.Sections["ClusterConfig"].Parameters["ReverseProxy_Port"].Value;

            maxPendingAsks = int.Parse(configurationPackage.Settings.Sections["OrderBookConfig"].Parameters["MaxAsksPending"].Value);
            maxPendingBids = int.Parse(configurationPackage.Settings.Sections["OrderBookConfig"].Parameters["MaxAsksPending"].Value);
        }

        /// <summary>
        /// Adds a new ask
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task<string> AddAskAsync(Order order)
        {
            Validation.ThrowIfNotValidOrder(order);
            var currentAsks = await asks.CountAsync();

            // You have an SLA with management to not allow orders when a backlog of more than 200 are pending
            // Changing this value with fail a system audit. Other approaches much be used to scale.
            if (currentAsks > maxPendingAsks)
            {
                ServiceEventSource.Current.ServiceMaxPendingLimitHit();
                throw new MaxOrdersExceededException(currentAsks);
            }

            await this.asks.AddOrderAsync(order);
            return order.Id;
        }

        /// <summary>
        /// Adds a new bid
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task<string> AddBidAsync(Order order)
        {
            Validation.ThrowIfNotValidOrder(order);
            var currentBids = await this.bids.CountAsync();

            // You have an SLA with management to not allow orders when a backlog of more than 200 are pending
            // Changing this value with fail a system audit. Other approaches much be used to scale.
            if (currentBids > maxPendingBids)
            {
                ServiceEventSource.Current.ServiceMaxPendingLimitHit();
                throw new MaxOrdersExceededException(currentBids);
            }
            // Changes will fail an audit ^

            await this.bids.AddOrderAsync(order);
            return order.Id;
        }

        /// <summary>
        /// Gets all the current asks
        /// </summary>
        /// <returns></returns>
        public async Task<List<KeyValuePair<string, Order>>> GetAsksAsync()
        {
            return await this.asks.GetOrdersAsync();
        }

        /// <summary>
        /// Gets all the current bids
        /// </summary>
        /// <returns></returns>
        public async Task<List<KeyValuePair<string, Order>>> GetBidsAsync()
        {
            return await this.bids.GetOrdersAsync();
        }

        /// <summary>
        /// Returns current count of asks
        /// </summary>
        /// <returns></returns>
        public async Task<long> CountAskAsync()
        {
            return await this.asks.CountAsync();
        }

        /// <summary>
        /// Returns current count of bids
        /// </summary>
        /// <returns></returns>
        public async Task<long> CountBidAsync()
        {
            return await this.bids.CountAsync();
        }

        /// <summary>
        /// Drops all asks and bids
        /// </summary>
        /// <returns></returns>
        public async Task ClearAllOrders()
        {
            await this.bids.ClearAsync();
            await this.asks.ClearAsync();
        }

        /// <summary>
        /// Checks whether a bid and a ask
        /// are a match.
        /// </summary>
        /// <param name="bid"></param>
        /// <param name="ask"></param>
        /// <returns></returns>
        public static bool IsMatch(Order bid, Order ask)
        {
            if (bid == null || ask == null)
            {
                return false;
            }
            return (bid.Value >= ask.Value) && (bid.Quantity <= ask.Quantity);
        }

        /// <summary>
        /// Creates an ask that satisfies
        /// the bid and and additional ask
        /// for any left over quanitity.
        /// </summary>
        /// <param name="bid"></param>
        /// <param name="ask"></param>
        /// <returns></returns>
        public (Order, Order) SplitAsk(Order bid, Order ask)
        {
            if (ask.Quantity == 0 || ask.Value == 0)
            {
                throw new InvalidAskException("Ask quantity or value cannot be 0");
            }
            if (bid.Quantity == 0 || bid.Value == 0)
            {
                throw new InvalidBidException("Bid quantity or value cannot be 0");
            }
            Order leftOverOrder = null;
            if (ask.Quantity != bid.Quantity)
            {
                var leftOverQuantity = (ask.Quantity - bid.Quantity);
                var leftOverValue = ask.Value;
                leftOverOrder = new Order(leftOverValue, leftOverQuantity, ask.UserId);
            }
            var matchOrder = new Order(ask.Id, bid.Value, bid.Quantity, ask.UserId);
            return (matchOrder, leftOverOrder);
        }

        /// <summary>
        /// RunAsync is called by the Service Fabric runtime once the service is ready
        /// to begin processing.
        /// We use it select the maximum bid and minimum ask. We then
        /// pair these in a match and hand them over to the fulfilment
        /// service to process the trade of goods.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            client.DefaultRequestHeaders
                  .Accept
                  .Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var rand = new Random();

            Order maxBid = null;
            Order minAsk = null;
            Order match = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // If the book can't make any matches then sleep for 300ms
                    // this prevents high idle CPU utilisation when there are no orders to process
                    if (await bids.CountAsync() < 1 || await asks.CountAsync() < 1)
                    {
                        await Task.Delay(500, cancellationToken);
                    }

                    // Get the maximum bid and minimum ask
                    maxBid = this.bids.GetMaxOrder();
                    minAsk = this.asks.GetMinOrder();

                    if (IsMatch(maxBid, minAsk))
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"New match: bid {maxBid.Id} and ask {minAsk.Id}");

                        try
                        {
                            // We split the ask incase the seller had a bigger
                            // quantity of goods than the buyer wished to bid for.
                            // The cost per unit is kept consistent between the
                            // original ask and any left over asks.
                            (var matchingAsk, var leftOverAsk) = SplitAsk(maxBid, minAsk);
                            if (leftOverAsk != null)
                            {
                                try
                                {
                                    await AddAskAsync(leftOverAsk); // Add the left over ask as a new order
                                }
                                catch (FabricNotPrimaryException ex)
                                {
                                    ServiceEventSource.Current.ServiceException(this.Context, "Failed to add left over ask as fabric is not primary", ex);
                                    return;
                                }
                                catch (FabricException ex)
                                {
                                    ServiceEventSource.Current.ServiceException(this.Context, "Failed to add left over ask as fabric exception throw", ex);
                                    await BackOff(cancellationToken);
                                    continue;
                                }
                                catch (MaxOrdersExceededException ex)
                                {
                                    ServiceEventSource.Current.ServiceException(this.Context, "Failed to add left over ask as max orders exceeded", ex);
                                    await BackOff(cancellationToken);
                                    continue;
                                }
                            }
                            match = matchingAsk;
                        }
                        catch (InvalidAskException)
                        {
                            // The ask did not meet our validation criteria.
                            // The bid may still be valid so we'll leave it.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Dropping invalid Asks generated from.");
                            await this.asks.RemoveAsync(minAsk);
                            continue;
                        }
                        catch (InvalidBidException)
                        {
                            // The bid did not meet our validation criteria.
                            // The ask may still be valid so we'll leave it.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Droping invalid Bids generated from.");
                            await this.bids.RemoveAsync(maxBid);
                            continue;

                        }

                        var trade = new TradeRequestModel
                        {
                            Ask = match,
                            Bid = maxBid,
                        };

                        // Send the trade request to our fulfillment service to complete.
                        var content = new StringContent(JsonConvert.SerializeObject(trade), Encoding.UTF8, "application/json");
                        HttpResponseMessage res = null;
                        try
                        {
                            var randomParitionId = NextInt64(rand);
                            res = await client.PostAsync($"http://localhost:{reverseProxyPort}/Exchange/Fulfillment/api/trades?PartitionKey={randomParitionId.ToString()}&PartitionKind=Int64Range", content, cancellationToken);
                        }
                        catch (HttpRequestException ex)
                        {
                            // Exception thrown when attempting to make HTTP POST to fulfillment API.
                            // Possibly a DNS, network connectivity or timeout issue.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Error sending trade to fulfillment service, error: '{ex.Message}'");
                            await BackOff(cancellationToken);
                            continue;
                        }
                        catch (TaskCanceledException)
                        {
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Request to fulfillment service timed out, backing off and retrying.");
                            await BackOff(cancellationToken);
                            continue;
                        }

                        if (res?.StatusCode == HttpStatusCode.BadRequest)
                        {
                            await LogBadRequest(res);
                            using (var tx = this.StateManager.CreateTransaction())
                            {
                                await this.asks.RemoveAsync(tx, minAsk);
                                await this.bids.RemoveAsync(tx, maxBid);
                                await tx.CommitAsync();
                            }
                            continue;
                        }
                        if (res?.StatusCode == HttpStatusCode.Gone)
                        {
                            await LogBadRequest(res);
                            continue;
                        }
                        if (res?.StatusCode == (HttpStatusCode)429)
                        {
                            await LogBadRequest(res);
                            await BackOff(cancellationToken);
                            continue;
                        }
                        if (res?.IsSuccessStatusCode == true)
                        {
                            // If the response is successful, assume orders are safe to remove.
                            using (var tx = this.StateManager.CreateTransaction())
                            {
                                try
                                {
                                    var removedAsk = await this.asks.RemoveAsync(tx, minAsk);
                                    var removedBid = await this.bids.RemoveAsync(tx, maxBid);
                                    if (removedAsk && removedBid)
                                    {
                                        // Committing our transaction will remove both the ask and the bid from our orders.
                                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Removed Ask {minAsk.Id} and Bid {maxBid.Id}");
                                        await tx.CommitAsync();

                                        var tradeId = await res.Content.ReadAsStringAsync();
                                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Created new trade with id '{tradeId}'");
                                    }
                                    else
                                    {
                                        // Abort the transaction to ensure both the ask and the bid stay in our orders.
                                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Failed to remove orders, so requeuing");
                                        tx.Abort();
                                        continue;
                                    }
                                }
                                catch (InvalidOperationException)
                                {
                                    tx.Abort();
                                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Invalid operation performed, aborting and will retry");
                                    await BackOff(cancellationToken);
                                    continue;
                                }
                                catch (FabricNotReadableException)
                                {
                                    tx.Abort();
                                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Fabric is not currently readable, aborting and will retry");
                                    await BackOff(cancellationToken);
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            await LogBadRequest(res);
                            await BackOff(cancellationToken);
                            continue;
                        }
                    }
                }
                catch (FabricNotReadableException)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Fabric is not currently readable, aborting and will retry");
                    await BackOff(cancellationToken);
                    continue;
                }
                catch (InvalidOperationException)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Invalid operation performed, aborting and will retry");
                    await BackOff(cancellationToken);
                    continue;
                }
                catch (FabricNotPrimaryException)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Fabric cannot perform write as it is not the primary replica");
                    return;
                }
            }
        }

        private static async Task BackOff(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message($"OrderBook is backing off for {backOffDurationInSec} seconds");
            await Task.Delay(TimeSpan.FromSeconds(backOffDurationInSec), cancellationToken);
        }

        private async Task LogBadRequest(HttpResponseMessage res)
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, $"Error, fulfillment service '{res.StatusCode}': {await res.Content.ReadAsStringAsync()}");
        }

        public static Int64 NextInt64(Random rnd)
        {
            var buffer = new byte[sizeof(Int64)];
            rnd.NextBytes(buffer);
            return BitConverter.ToInt64(buffer, 0);
        }


        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new ServiceReplicaListener[]
            {
                new ServiceReplicaListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        return new WebHostBuilder()
                                    .UseKestrel()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<HttpClient>(new HttpClient())
                                            .AddSingleton<StatefulServiceContext>(serviceContext)
                                            .AddSingleton<OrderBook>(this)
                                            .AddSingleton<ITelemetryInitializer>((serviceProvider) => FabricTelemetryInitializerExtension.CreateFabricTelemetryInitializer(serviceContext)))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseApplicationInsights()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseReverseProxyIntegration)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }
    }
}
