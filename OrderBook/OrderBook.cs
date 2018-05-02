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

        // HTTP details for communicating with Fulfillment service
        private static readonly HttpClient client = new HttpClient();
        private string fulfillmentEndpoint;

        public ConfigurationPackage configPackage { get; private set; }

        public OrderBook(StatefulServiceContext context)
            : base(context)
        {
            Init();
            this.asks = new OrderSet(this.StateManager, AskBookName);
            this.bids = new OrderSet(this.StateManager, BidBookName);

        }

        // This constructor is used during unit testing by setting a mock
        // IReliableStateManagerReplica
        public OrderBook(StatefulServiceContext context, IReliableStateManagerReplica reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        {
            Init();
            this.asks = new OrderSet(reliableStateManagerReplica, AskBookName);
            this.bids = new OrderSet(reliableStateManagerReplica, BidBookName);
        }

        private void Init()
        {
            configPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
        }

        /// <summary>
        /// Adds a new ask
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task<string> AddAskAsync(Order order)
        {
            IsValidOrder(order);
            var currentAsks = await asks.CountAsync();

            // You have an SLA with management to not allow orders when a backlog of more than 200 are pending
            // Changing this value with fail a system audit. Other approaches much be used to scale.
            var maxPendingAsks = int.Parse(configPackage.Settings.Sections["OrderBookConfig"].Parameters["MaxAsksPending"].Value);
            if (currentAsks > maxPendingAsks)
            {
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
            IsValidOrder(order);
            var currentBids = await this.bids.CountAsync();

            // You have an SLA with management to not allow orders when a backlog of more than 200 are pending
            // Changing this value with fail a system audit. Other approaches much be used to scale.
            var maxPendingBids = int.Parse(configPackage.Settings.Sections["OrderBookConfig"].Parameters["MaxBidsPending"].Value);

            if (currentBids > maxPendingBids)
            {
                throw new MaxOrdersExceededException(currentBids);
            }

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
        /// Checks whether a given order meets
        /// the validity criteria. Throws
        /// InvalidOrderException if it doesn't.
        /// </summary>
        /// <param name="order"></param>
        private void IsValidOrder(Order order)
        {
            if (string.IsNullOrWhiteSpace(order.Id))
            {
                throw new InvalidOrderException("Order Id cannot be null, empty or contain whitespace");
            }
            if (string.IsNullOrWhiteSpace(order.UserId))
            {
                throw new InvalidOrderException("Order cannot have a null or invalid user id");
            }
            if (order.Quantity == 0)
            {
                throw new InvalidOrderException("Order cannot have 0 quantity");
            }
            if (order.Value == 0)
            {
                throw new InvalidOrderException("Order cannot have 0 value");
            }
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
                    new KestrelCommunicationListener(serviceContext, "ServiceEndpoint", (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        return new WebHostBuilder()
                                    .UseKestrel()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<StatefulServiceContext>(serviceContext)
                                            .AddSingleton<OrderBook>(this))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    //.UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseUniqueServiceUrl)
                                    .UseUrls(url)
                                    .Build();
                    }),
                    listenOnSecondary: true)
            };
        }

        /// <summary>
        /// RunAsync is called by the Service Fabric runtime once the service is ready
        /// to begin processing.
        /// We use it select the maximum bid and minimum ask. We then
        /// pair these in a match and hand them over to the fulfilment
        /// service to process the transfer of goods.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // Get configuration from our setting.xml file
            var configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            var dnsName = configurationPackage.Settings.Sections["OrderBookConfig"].Parameters["Fulfillment_DnsName"].Value;
            var port = configurationPackage.Settings.Sections["OrderBookConfig"].Parameters["Fulfillment_Port"].Value;

            fulfillmentEndpoint = $"http://{dnsName}:{port}";

            client.DefaultRequestHeaders
                  .Accept
                  .Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var maxAttempts = 5;
            var attempts = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Throttle loop: This limit
                // cannot be removed or you will fail an Audit. 
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                // Get the maximum bid and minimum ask
                var maxBid = this.bids.GetMaxOrder();
                var minAsk = this.asks.GetMinOrder();

                // Check if match
                if (IsMatch(maxBid, minAsk))
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"New match: order {maxBid.Id} and order {minAsk.Id}");
                    Order match = null;
                    try
                    {
                        // In this exchange, the transfer is fulfilled at
                        // the value the buyer is willing to pay. Therefore,
                        // the seller may get more value than they were 
                        // willing to accept.

                        // We split the ask incase the seller had a bigger
                        // quantity of goods than the buyer wished to bid for.
                        // The cost per unit is kept consistent between the
                        // original ask and any left over asks.
                        (var matchingAsk, var leftOverAsk) = SplitAsk(maxBid, minAsk);
                        if (leftOverAsk != null)
                        {
                            // Add the left over ask as a new order
                            await AddAskAsync(leftOverAsk);
                        }
                        match = matchingAsk;
                    }
                    catch (InvalidAskException)
                    {
                        // The ask did not meet our validation criteria.
                        // The bid may still be valid so we'll leave it.
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Invalid Asks generated from {minAsk.Id}, dropping.");
                        await this.asks.RemoveAsync(minAsk);
                        continue;
                    }
                    catch (InvalidBidException)
                    {
                        // The bid did not meet our validation criteria.
                        // The ask may still be valid so we'll leave it.
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Invalid Bids generated from {maxBid.Id}, dropping.");
                        await this.bids.RemoveAsync(maxBid);
                        continue;
                    }

                    // Build a transfer containing the matched
                    // ask and bid.
                    var transfer = new TransferRequestModel
                    {
                        Ask = match,
                        Bid = maxBid,
                    };
                    // Send the transfer to our fulfillment
                    // service for fulfillment.
                    var content = new StringContent(JsonConvert.SerializeObject(transfer), Encoding.UTF8, "application/json");
                    HttpResponseMessage res;
                    try
                    {
                        res = await client.PostAsync($"{fulfillmentEndpoint}/api/transfers", content);
                    }
                    catch (Exception ex)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Error sending transfer to fulfillment service. Error: '{ex.Message}'. Aborting match", ex);
                        continue;
                    }
                    // If the response is successful, assume
                    // transfer is safe to remove.
                    if (res.IsSuccessStatusCode)
                    {
                        using (var tx = this.StateManager.CreateTransaction())
                        {
                            var removedAsk = await this.asks.RemoveAsync(tx, minAsk);
                            var removedBid = await this.bids.RemoveAsync(tx, maxBid);
                            if (removedAsk && removedBid)
                            {
                                // Committing our transaction will remove both the ask and the bid
                                // from our orders.
                                ServiceEventSource.Current.ServiceMessage(this.Context, $"Removed Ask {minAsk.Id} and Bid {maxBid.Id}");
                                await tx.CommitAsync();

                                var transferId = await res.Content.ReadAsStringAsync();
                                ServiceEventSource.Current.ServiceMessage(this.Context, $"Created new transfer with id '{transferId}'");
                            }
                            else
                            {
                                // Abort the transaction to ensure both the ask and the bid
                                // stay in our orders.
                                ServiceEventSource.Current.ServiceMessage(this.Context, $"Failed to remove orders, so requeuing");
                                tx.Abort();
                                continue;
                            }
                        }
                    }
                    else
                    {
                        // Exception calling fulfillment service, likely a transient error
                        // so we'll back off and try again shortly.
                        attempts++;
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Attempt to send transfer to Fulfillment service #{attempts}: Response code {res.StatusCode}");

                        if (attempts >= maxAttempts)
                        {
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Attempts limit {maxAttempts} exceeded, terminating with error.");
                            throw new Exception("OrderBook cannot contact Fulfillment service.");
                        }
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    }
                }
            }
        }
    }
}
