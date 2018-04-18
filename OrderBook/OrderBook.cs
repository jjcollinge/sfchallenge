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

namespace OrderBook
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    public class OrderBook : StatefulService
    {
        public const string AskBookName = "books/asks";
        public const string BidBookName = "books/bids";
        private OrderSet asks;
        private OrderSet bids;
        private string fulfillmentEndpoint;
        private static readonly HttpClient client = new HttpClient();

        public OrderBook(StatefulServiceContext context)
            : base(context)
        {
            this.asks = new OrderSet(this.StateManager, AskBookName);
            this.bids = new OrderSet(this.StateManager, BidBookName);
        }

        public OrderBook(StatefulServiceContext context, IReliableStateManagerReplica reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        {
            this.asks = new OrderSet(reliableStateManagerReplica, AskBookName);
            this.bids = new OrderSet(reliableStateManagerReplica, BidBookName);
        }

        /// <summary>
        /// Adds a new ask
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task AddAskAsync(Order order)
        {
            IsValidOrder(order);
            await this.asks.AddOrderAsync(order);
        }

        /// <summary>
        /// Adds a new bid
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task AddBidAsync(Order order)
        {
            await this.bids.AddOrderAsync(order);
        }

        /// <summary>
        /// Gets all the current asks
        /// </summary>
        /// <returns></returns>
        public async Task<List<KeyValuePair<int, Queue<Order>>>> GetAsksAsync()
        {
            return await this.asks.GetOrdersAsync();
        }

        /// <summary>
        /// Gets all the current bids
        /// </summary>
        /// <returns></returns>
        public async Task<List<KeyValuePair<int, Queue<Order>>>> GetBidsAsync()
        {
            return await this.bids.GetOrdersAsync();
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
                    }))
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
            var configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            var dnsName = configurationPackage.Settings.Sections["FulfillmentConfig"].Parameters["ServiceDnsName"].Value;
            var port = configurationPackage.Settings.Sections["FulfillmentConfig"].Parameters["servicePort"].Value;

            fulfillmentEndpoint = $"http://{dnsName}:{port}";

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

#if DEBUG
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
#endif

                try
                {
                    var maxBid = await this.bids.PeekMaxOrderAsync();
                    if (maxBid == null)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, "No bids available");
                        continue;
                    }
                    var minAsk = await this.asks.PeekMinOrderAsync();
                    if (minAsk == null)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, "No asks available");
                        continue;
                    }
                    if ((maxBid.Value >= minAsk.Value) && (maxBid.Quantity <= minAsk.Quantity))
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Match made for {maxBid.Quantity} at price {maxBid.Value}");
                        (var matchingAsk, var leftOverAsk) = SplitAsk(maxBid, minAsk);
                        if (leftOverAsk != null)
                        {
                            await AddAskAsync(leftOverAsk);
                        }

                        using (var tx = this.StateManager.CreateTransaction())
                        {
                            await this.asks.ResolveOrderAsync(tx, matchingAsk);
                            await this.bids.ResolveOrderAsync(tx, maxBid);

                            var match = new TransferRequestModel
                            {
                                Ask = matchingAsk,
                                Bid = maxBid,
                            };
                            var content = new StringContent(JsonConvert.SerializeObject(match), Encoding.UTF8, "application/json");
                            try
                            {
                                var addTransferUri = $"{fulfillmentEndpoint}/api/transfers";
#if DEBUG
                                addTransferUri = $"http://localhost:{port}/api/transfers";
#endif
                                var res = await client.PostAsync(addTransferUri, content);
                                if (res.IsSuccessStatusCode)
                                {
                                    var transferId = await res.Content.ReadAsStringAsync();
                                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Created new transfer with id '{transferId}'");
                                    await tx.CommitAsync();
                                }
                                else if (res.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                                {
                                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Failed to contact the fulfillment service, is it running?");
                                    tx.Abort();
                                }
                                else
                                {
                                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Failed to create transfer, aborting transaction");
                                    tx.Abort();
                                }
                            }
                            catch (Exception ex)
                            {
                                ServiceEventSource.Current.ServiceMessage(this.Context, $"Exception thrown callling fulfillment service: {ex.Message}");
                                tx.Abort();
                                continue;
                            }
                        }
                    }
                }
                catch (InvalidOrderException ex)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Invalid order matched, this shouldn't happen");
                    throw ex;
                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Exception thrown evaluating transfer: {ex.Message}");
                    throw ex;
                }
            }
        }

        public (Order, Order) SplitAsk(Order bid, Order ask)
        {
            if (ask.Quantity == 0 || ask.Value == 0)
            {
                throw new InvalidOrderException("Ask quantity or value cannot be 0");
            }
            if (bid.Quantity == 0 || bid.Value == 0)
            {
                throw new InvalidOrderException("Bid quantity or value cannot be 0");
            }
            if (bid.Value == ask.Value && bid.Quantity == ask.Quantity)
            {
                return (ask, null);
            }
            
            var leftOverQuantity = ask.Quantity - bid.Quantity;
            var unitValue = (float)ask.Value / (float)ask.Quantity;
            var leftOverValue = unitValue * leftOverQuantity;
            var leftOverAsk = new Order
            {
                Quantity = leftOverQuantity,
                Value = (uint)Math.Floor(leftOverValue),
            };
            var match = new Order(ask.Id)
            {
                Quantity = bid.Quantity,
                Value = bid.Value,
            };
            return (match, leftOverAsk);
        }

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
    }
}
