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
        private Metrics MetricsLog;
        private int maxPendingAsks;
        private int maxPendingBids;
        private const int backOffDurationInSec = 2;

        public string PartitionName { get; private set; }

        public OrderBook(StatefulServiceContext context)
            : base(context)
        {
            Init(context);
            this.asks = new OrderSet(this.StateManager, AskBookName);
            this.bids = new OrderSet(this.StateManager, BidBookName);
        }

        // This constructor is used during unit testing by setting a mock IReliableStateManagerReplica
        public OrderBook(StatefulServiceContext context, IReliableStateManagerReplica reliableStateManagerReplica, Order ask = null, Order bid = null, int maxPendingAsks = 10, int maxPendingBids = 10)
            : base(context, reliableStateManagerReplica)
        {
            this.maxPendingAsks = maxPendingAsks;
            this.maxPendingBids = maxPendingBids;
            this.asks = new OrderSet(reliableStateManagerReplica, AskBookName);
            if (ask != null)
            {
                this.asks.SecondaryIndex = this.asks.SecondaryIndex.Add(ask);
            }
            this.bids = new OrderSet(reliableStateManagerReplica, BidBookName);
            if (bid != null)
            {
                this.bids.SecondaryIndex = this.bids.SecondaryIndex.Add(bid);
            }
        }

        /// <summary>
        /// Init setups in any configuration values
        /// </summary>
        private void Init(ServiceContext context)
        {
            using (var fabricClient = new FabricClient())
            {
                var partitionList = fabricClient?.QueryManager?.GetPartitionListAsync(context.ServiceName).Result;
                foreach (var partition in partitionList)
                {
                    if (partition.PartitionInformation.Id == context.PartitionId)
                    {
                        if (partition.PartitionInformation.Kind == ServicePartitionKind.Named)
                        {
                            var namedPartitionInfo = partition.PartitionInformation as NamedPartitionInformation;
                            PartitionName = namedPartitionInfo.Name;
                            break;
                        }
                        if (partition.PartitionInformation.Kind == ServicePartitionKind.Singleton)
                        {
                            PartitionName = CurrencyPairExtensions.GBPUSD_SYMBOL;
                            break;
                        }
                    }
                }
            }

            // Get configuration from our PackageRoot/Config/Setting.xml file
            var configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            reverseProxyPort = configurationPackage.Settings.Sections["ClusterConfig"].Parameters["ReverseProxy_Port"].Value;

            maxPendingAsks = int.Parse(configurationPackage.Settings.Sections["OrderBookConfig"].Parameters["MaxAsksPending"].Value);
            maxPendingBids = int.Parse(configurationPackage.Settings.Sections["OrderBookConfig"].Parameters["MaxAsksPending"].Value);

            // Metrics used to compare team performance and reliability against each other
            var metricsInstrumentationKey = configurationPackage.Settings.Sections["OrderBookConfig"].Parameters["Admin_AppInsights_InstrumentationKey"].Value;
            var teamName = configurationPackage.Settings.Sections["OrderBookConfig"].Parameters["TeamName"].Value;
            this.MetricsLog = new Metrics(metricsInstrumentationKey, teamName);
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

            // REQUIRED, DO NOT REMOVE.         
            if (currentAsks > maxPendingAsks)
            {
                ServiceEventSource.Current.ServiceMaxPendingLimitHit();
                throw new MaxOrdersExceededException(currentAsks);
            }

            await this.asks.AddOrderAsync(order);

            MetricsLog?.AskCreated(order);

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
            var currentBids = await bids.CountAsync();

            // REQUIRED, DO NOT REMOVE.         
            if (currentBids > maxPendingBids)
            {
                ServiceEventSource.Current.ServiceMaxPendingLimitHit();
                throw new MaxOrdersExceededException(currentBids);
            }

            await this.bids.AddOrderAsync(order);

            MetricsLog?.BidCreated(order);

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
            return (bid.Pair == ask.Pair) && (bid.Price >= ask.Price) && (bid.Amount <= ask.Amount);
        }

        /// <summary>
        /// Creates an ask that satisfies
        /// the bid and and additional ask
        /// for any left over quanitity.
        /// </summary>
        /// <param name="bid"></param>
        /// <param name="ask"></param>
        /// <returns></returns>
        public (Order, Order) SettleTrade(Order bid, Order ask)
        {
            if (ask.Price == 0 || ask.Amount == 0)
            {
                throw new InvalidAskException("Ask quantity or value cannot be 0");
            }
            if (bid.Price == 0 || bid.Amount == 0)
            {
                throw new InvalidBidException("Bid quantity or value cannot be 0");
            }
            Order leftOverAsk = null;
            if (ask.Amount != bid.Amount)
            {
                var amountRemaining = (ask.Amount - bid.Amount);
                leftOverAsk = new Order(ask.UserId, ask.Pair, amountRemaining, ask.Price);
            }
            var settlement = new Order(ask.Id, ask.UserId, bid.Pair, bid.Amount, bid.Price);
            return (settlement, leftOverAsk);
        }

        /// <summary>
        /// RunAsync is called by the Service Fabric runtime once the service is ready
        /// to begin processing.
        /// We use it select the maximum bid and minimum ask. We then
        /// match these in a trade and hand them over to the fulfillment
        /// service to execute the exchange.
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
            Order leftOverAsk = null;
            Order settlement = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // If the book can't make any matches then sleep to
                    // prevent high idle CPU utilization when there are
                    // no orders to process
                    if (await bids.CountAsync() < 1 || await asks.CountAsync() < 1)
                    {
                        await Task.Delay(500, cancellationToken);
                    }

                    // Get the maximum bid and minimum ask from our secondary index.
                    maxBid = this.bids.GetMaxOrder();
                    minAsk = this.asks.GetMinOrder();

                    if (maxBid == null || minAsk == null)
                    {
                        continue;
                    }

                    // Enforce TTL: Remove unmatched bids/asks after 5mins. 
                    var hasBidTimedout = maxBid.Timestamp.AddMinutes(5) < DateTime.UtcNow;
                    var hasAskTimedout = minAsk.Timestamp.AddMinutes(5) < DateTime.UtcNow;
                    if (hasBidTimedout)
                    {
                        await this.bids.RemoveAsync(maxBid);
                    }

                    if (hasAskTimedout)
                    {
                        await this.asks.RemoveAsync(minAsk);
                    }
                    if (hasBidTimedout || hasAskTimedout)
                    {
                        continue;
                    }

                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Checking for new match");

                    if (IsMatch(maxBid, minAsk))
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"New match made between Bid {maxBid.Id} and Ask {minAsk.Id}");
                        MetricsLog?.OrderMatched(maxBid, minAsk);

                        try
                        {
                            // We split the ask incase the seller had a bigger
                            // amount of currency than the buyer wished to buy.
                            // The cost per unit is kept consistent between the
                            // original ask and any left over asks.
                            (var settledOrder, var leftOver) = SettleTrade(maxBid, minAsk);
                            settlement = settledOrder;
                            leftOverAsk = leftOver;
                        }
                        catch (InvalidAskException)
                        {
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Dropping invalid Asks");
                            await this.asks.RemoveAsync(minAsk);
                            await this.bids.RemoveAsync(maxBid); // In a real system we would leave the bid
                            continue;
                        }
                        catch (InvalidBidException)
                        {
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Droping invalid Bids");
                            await this.bids.RemoveAsync(maxBid);
                            await this.asks.RemoveAsync(minAsk); // In a real system we would leave the ask
                            continue;
                        }

                        if (leftOverAsk != null)
                        {
                            try
                            {
                                await AddAskAsync(leftOverAsk); // Add the left over ask as a new order
                            }
                            catch (FabricNotPrimaryException ex)
                            {
                                // If the fabric is not primary we should
                                // return control back to the platform.
                                ServiceEventSource.Current.ServiceException(this.Context, "Failed to add left over ask as fabric is not primary", ex);
                                return;
                            }
                            catch (FabricNotReadableException)
                            {
                                // Fabric is not in a readable state. This
                                // is a transient error and should be retried.
                                ServiceEventSource.Current.ServiceMessage(this.Context, $"Fabric is not currently readable, aborting and will retry");
                                await BackOff(cancellationToken);
                                continue;
                            }
                            catch (FabricException ex)
                            {
                                ServiceEventSource.Current.ServiceException(this.Context, "Failed to add left over ask as fabric exception throw", ex);

                                if (IsTransientError(ex.ErrorCode))
                                {
                                    // Transient error, we can backoff and retry
                                    await BackOff(cancellationToken);
                                    continue;
                                }
                                // Non transient error, re-throw
                                throw ex;
                            }
                            catch (MaxOrdersExceededException ex)
                            {
                                // If we have hit the maximum number of
                                // orders - drop the left over ask to
                                // avoid deadlock. The seller will have
                                // to resubmit the ask manually.
                                ServiceEventSource.Current.ServiceException(this.Context, "Failed to add left over ask as max orders exceeded", ex);
                            }
                        }

                        var trade = new TradeRequestModel
                        {
                            Ask = minAsk,               // Original ask order
                            Bid = maxBid,               // Original bid order
                            Settlement = settlement,    // Settled order
                        };

                        // Send the trade request to our fulfillment service to complete.
                        var content = new StringContent(JsonConvert.SerializeObject(trade), Encoding.UTF8, "application/json");
                        HttpResponseMessage res = null;
                        try
                        {
                            var randomPartitionId = NextInt64(rand); // Send to any partition - it doesn't matter.
                            res = await client.PostAsync($"http://localhost:{reverseProxyPort}/Exchange/Fulfillment/api/trades?PartitionKey={randomPartitionId.ToString()}&PartitionKind=Int64Range", content, cancellationToken);
                        }
                        catch (HttpRequestException ex)
                        {
                            // Exception thrown when attempting to make HTTP POST to fulfillment API.
                            // Possibly a DNS, network connectivity or timeout issue. We'll treat it
                            // as transient, back off and retry.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"HTTP error sending trade to fulfillment service, error: '{ex.Message}'");
                            await BackOff(cancellationToken);
                            continue;
                        }
                        catch (TimeoutException ex)
                        {
                            // Call to Fulfillment service timed out, likely because it is currently down or
                            // under extreme load
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Call to fulfillment service timed out with error: '{ex.Message}'");
                            await BackOff(cancellationToken);
                            continue;
                        }
                        catch (TaskCanceledException ex)
                        {
                            // Task has been cancelled, assume SF want's to close us.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Request to fulfillment service got cancelled");

                            var wasCancelled = ex.CancellationToken.IsCancellationRequested;
                            if (wasCancelled)
                            {
                                return;
                            }
                            else
                            {
                                await BackOff(cancellationToken);
                                continue;
                            }
                        }

                        // If the response from the Fulfillment API was not 2xx
                        if (res?.StatusCode == HttpStatusCode.BadRequest)
                        {
                            // Invalid request - fail the orders and 
                            // remove them from our order book.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Error resposne from fulfillment service '{res.StatusCode}'");

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
                            // Transient error indicating the service
                            // has moved and needs to be re-resolved.
                            // Retry.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Error resposne from fulfillment service '{res.StatusCode}'");

                            continue;
                        }
                        if (res?.StatusCode == (HttpStatusCode)429)
                        {
                            // 429 is a custom error that indicates
                            // that the service is under heavy load.
                            // Back off and retry.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Error resposne from fulfillment service '{res.StatusCode}'");

                            await BackOff(cancellationToken);
                            continue;
                        }

                        if (res?.IsSuccessStatusCode == true)
                        {
                            using (var tx = this.StateManager.CreateTransaction())
                            {

                                // If the response is successful, assume orders are safe to remove.
                                var removedAsk = await this.asks.RemoveAsync(tx, minAsk);
                                var removedBid = await this.bids.RemoveAsync(tx, maxBid);
                                if (removedAsk && removedBid)
                                {
                                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Removed Ask {minAsk.Id} and Bid {maxBid.Id}");
                                    await tx.CommitAsync(); // Committing our transaction will remove both the ask and the bid from our orders.

                                    var tradeId = await res.Content.ReadAsStringAsync();
                                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Created new trade with id '{tradeId}'");
                                }
                                else
                                {
                                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Failed to remove orders, so requeuing");
                                    tx.Abort(); // Abort the transaction to ensure both the ask and the bid stay in our orders.
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            // Unhandled error condition.
                            // Log it, back off and retry. 
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Error resposne from fulfillment service '{res.StatusCode}'");

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

        /// <summary>
        /// Checks whether a specific fabric error code
        /// can be considered transient.
        /// </summary>
        /// <param name="errorCode"></param>
        /// <returns></returns>
        private bool IsTransientError(FabricErrorCode errorCode)
        {
            switch (errorCode)
            {
                case FabricErrorCode.GatewayNotReachable:
                    return true;
                case FabricErrorCode.ServiceTooBusy:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// BackOff will delay execution for n seconds
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task BackOff(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message($"OrderBook is backing off for {backOffDurationInSec} seconds");
            await Task.Delay(TimeSpan.FromSeconds(backOffDurationInSec), cancellationToken);
        }

        /// <summary>
        /// Generates a psuedo random Int64
        /// </summary>
        /// <param name="rnd"></param>
        /// <returns></returns>
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
                                            .AddSingleton<OrderBook>(this))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseReverseProxyIntegration)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }
    }
}
