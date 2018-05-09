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
using Common;
using System.Net.Http;
using Newtonsoft.Json;
using System.Text;
using UserStore.Interface;

namespace Fulfillment
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    public class Fulfillment : StatefulService
    {
        public const string TradeQueueName = "trades";
        private TradeQueue Trades;
        private readonly UserStoreClient Users;
        private static readonly HttpClient client = new HttpClient();
        private string reverseProxyPort;
        private int maxPendingTrades;
        private Random rand = new Random();
        private AutoResetEvent tradeReceivedEvent = new AutoResetEvent(false);

        public Fulfillment(StatefulServiceContext context)
            : base(context)
        {
            Init();
            this.Trades = new TradeQueue(this.StateManager, TradeQueueName);
            this.Users = new UserStoreClient();
        }

        public Fulfillment(StatefulServiceContext context, IReliableStateManagerReplica reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        {
            Init();
            this.Trades = new TradeQueue(this.StateManager, TradeQueueName);
            this.Users = new UserStoreClient();
        }

        private void Init()
        {
            // Get configuration from our PackageRoot/Config/Setting.xml file
            var configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            reverseProxyPort = configurationPackage.Settings.Sections["ClusterConfig"].Parameters["ReverseProxy_Port"].Value;
            maxPendingTrades = int.Parse(configurationPackage.Settings.Sections["ClusterConfig"].Parameters["MaxTradesPending"].Value);
        }

        /// <summary>
        /// Checks whether the provided trade
        /// meets the validity requirements. If it
        /// does, adds it the trade queue.
        /// </summary>
        /// <param name="trade"></param>
        /// <returns></returns>
        public async Task<string> AddTradeAsync(TradeRequestModel tradeRequest)
        {
            Validation.ThrowIfNotValidTradeRequest(tradeRequest);
            var pendingTrades = await Trades.CountAsync();
            if (pendingTrades > maxPendingTrades)
            {
                ServiceEventSource.Current.ServiceMaxPendingLimitHit();
                throw new MaxPendingTradesExceededException(pendingTrades);
            }
            var tradeId = await this.Trades.EnqueueAsync(tradeRequest, CancellationToken.None);
            tradeReceivedEvent.Set();
            return tradeId;
        }

        /// <summary>
        /// Gets the count of the number of trades currently
        /// in the trade queue
        /// </summary>
        /// <returns></returns>
        public async Task<long> GetTradesCountAsync()
        {
            return await this.Trades.CountAsync();
        }

        /// <summary>
        /// Adds a new user to the user store.
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public async Task<string> AddUserAsync(UserRequestModel userRequest)
        {
            Validation.ThrowIfNotValidUserRequest(userRequest);
            var userId = await this.Users.AddUserAsync(userRequest);
            return userId;
        }

        /// <summary>
        /// Gets a specific user from the user store.
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<User> GetUserAsync(string userId)
        {
            return await this.Users.GetUserAsync(userId);
        }

        /// <summary>
        /// Gets all users from the user store.
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<User>> GetUsersAsync()
        {
            return await this.Users.GetUsersAsync();
        }

        /// <summary>
        /// Deletes a specific user from the user store.
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<bool> DeleteUserAsync(string userId)
        {
            return await this.Users.DeleteUserAsync(userId);
        }

        /// <summary>
        /// Attempts to re-order an ask
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private async Task ReOrderAskAsync(Order order)
        {
            try
            {

                var content = new StringContent(JsonConvert.SerializeObject(order), Encoding.UTF8, "application/json");
                await client.PostAsync($"http://localhost:{reverseProxyPort}/Exchange/OrderBook/api/orders/ask", content);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ServiceMessage(Context, ex.ToString());
            }
        }

        /// <summary>
        /// Attempts to re-order a bid
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private async Task ReOrderBidAsync(Order order)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(order), Encoding.UTF8, "application/json");
                await client.PostAsync($"http://localhost:{reverseProxyPort}/Exchange/OrderBook/api/orders/bid", content); //TODO: Handle errors
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ServiceMessage(Context, ex.ToString());
            }

        }

        /// <summary>
        /// Sends trade to logger for external
        /// persistence
        /// </summary>
        /// <param name="trade"></param>
        /// <returns></returns>
        private async Task<bool> LogAsync(Trade trade)
        {
            var randomParitionId = NextInt64(rand);
            var content = new StringContent(JsonConvert.SerializeObject(trade), Encoding.UTF8, "application/json");
            var res = await client.PostAsync($"http://localhost:{reverseProxyPort}/Exchange/Logger/api/logger&PartitionKey={randomParitionId.ToString()}&PartitionKind=Int64Range", content); //TODO: Handle errors
            return res.IsSuccessStatusCode;
        }

        public static Int64 NextInt64(Random rnd)
        {
            var buffer = new byte[sizeof(Int64)];
            rnd.NextBytes(buffer);
            return BitConverter.ToInt64(buffer, 0);
        }

        /// <summary>
        /// Attempts to trade value and goods between
        /// the buyer and seller. We pass our existing
        /// transaction into the update to allow it to
        /// be commited atomically.
        /// </summary>
        /// <param name="tx"></param>
        /// <param name="trade"></param>
        /// <param name="seller"></param>
        /// <param name="buyer"></param>
        /// <returns></returns>
        private async Task<bool> ExecuteTradeAsync(ITransaction tx, Trade trade, User seller, User buyer)
        {
            var buyerTrades = buyer.TradeIds.ToList();
            buyerTrades.Add(trade.Id);
            buyer = new User(buyer.Id,
                            buyer.Username,
                            buyer.Quantity + trade.Bid.Quantity,
                            buyer.Balance - (trade.Bid.Value * trade.Bid.Quantity),
                            buyerTrades);

            var sellerTrades = seller.TradeIds.ToList();
            sellerTrades.Add(trade.Id);
            seller = new User(seller.Id,
                                seller.Username,
                                seller.Quantity - trade.Bid.Quantity,
                                seller.Balance + (trade.Bid.Value * trade.Bid.Quantity),
                                sellerTrades);

            var buyerUpdated = await Users.UpdateUserAsync(buyer);
            var sellerUpdated = await Users.UpdateUserAsync(seller);
            var logged = await LogAsync(trade);

            return (buyerUpdated && sellerUpdated && logged);
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Throttle loop:
                // This limit cannot be removed or you will fail an audit.
                if (await Trades.CountAsync() < 1)
                {
                    tradeReceivedEvent.WaitOne(TimeSpan.FromSeconds(5));
                }

                using (var tx = this.StateManager.CreateTransaction())
                {
                    Trade trade = null;
                    trade = await this.Trades.DequeueAsync(tx, cancellationToken);

                    if (trade != null)
                    {
                        // Get the buyer and seller associated with the trade
                        var seller = await Users.GetUserAsync(trade.Ask.UserId);
                        var buyer = await Users.GetUserAsync(trade.Bid.UserId);

                        // If the trade is invalid, we'll throw an exception
                        // and remove it from the queue. If there is a transient
                        // failure, we'll abort and put it back on the queue.
                        try
                        {
                            Validation.ThrowIfNotValidTrade(trade, seller, buyer);
                        }
                        catch (BadBuyerException ex)
                        {
                            await tx.CommitAsync();
                            ServiceEventSource.Current.Message($"Dropping bad trade. Reason: {ex.Message}");

                            // Buyer was invalid, Seller's ask may
                            // still be valid.
                            if (trade != null)
                            {
                                await ReOrderAskAsync(trade.Ask);
                                ServiceEventSource.Current.Message($"Reordering ask {trade.Ask.Id} for new match");
                            }
                            continue;
                        }
                        catch (BadSellerException ex)
                        {
                            await tx.CommitAsync();
                            ServiceEventSource.Current.Message($"Dropping bad trade. Reason: {ex.Message}");

                            // Seller was invalid, Buyer's bid may
                            // still be valid.
                            if (trade != null)
                            {
                                ServiceEventSource.Current.Message($"Reordering bid {trade.Bid.Id} for new match");
                                await ReOrderBidAsync(trade.Bid);
                            }
                            continue;
                        }

                        var applied = await ExecuteTradeAsync(tx, trade, seller, buyer);
                        // Applied indicates both buyer and seller have been
                        // successfully updated and the trade has been logged.
                        // We can now commit the transaction to release our lock
                        // on the queue.
                        if (applied)
                        {
                            await tx.CommitAsync();
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"trade {trade.Id} completed");
                        }
                        // Atleast one of the buyer or seller failed to update
                        // or we failed to log the trade.
                        // We will abort the transaction and assume a transient
                        // failure.
                        else
                        {
                            tx.Abort();
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"trade {trade.Id} aborted");
                        }
                    }
                }
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
                                            .AddSingleton<IReliableStateManager>(this.StateManager)
                                            .AddSingleton<Fulfillment>(this))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    //.UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseUniqueServiceUrl)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }
    }
}
