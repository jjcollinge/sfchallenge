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
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.ServiceFabric;
using Microsoft.ApplicationInsights.ServiceFabric.Module;

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
        private const int backOffDurationInSec = 2;
        private Metrics MetricsLog;

        public Fulfillment(StatefulServiceContext context)
            : base(context)
        {
            Init();
            this.Trades = new TradeQueue(this.StateManager, TradeQueueName);
            this.Users = new UserStoreClient();
        }

        // This constructor is used during unit testing by setting a mock IReliableStateManagerReplica
        public Fulfillment(StatefulServiceContext context, IReliableStateManagerReplica reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        {
            Init();
            this.Trades = new TradeQueue(this.StateManager, TradeQueueName);
            this.Users = new UserStoreClient();

        }

        /// <summary>
        /// Init setups in any configuration values
        /// </summary>
        private void Init()
        {
            // Get configuration from our PackageRoot/Config/Setting.xml file
            var configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            reverseProxyPort = configurationPackage.Settings.Sections["ClusterConfig"].Parameters["ReverseProxy_Port"].Value;
            maxPendingTrades = int.Parse(configurationPackage.Settings.Sections["ClusterConfig"].Parameters["MaxTradesPending"].Value);

            // Metrics used to compare team performance and reliability against each other
            var metricsInstrumentationKey = configurationPackage.Settings.Sections["ClusterConfig"].Parameters["Metrics_AppInsights_InstrumentationKey"].Value;
            var teamName = configurationPackage.Settings.Sections["ClusterConfig"].Parameters["TeamName"].Value;
            this.MetricsLog = new Metrics(metricsInstrumentationKey, teamName);
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
        /// Updates a user in the user store.
        /// </summary>
        /// <param name="userRequest"></param>
        /// <returns></returns>
        public async Task<bool> UpdateUserAsync(UserRequestModel userRequest)
        {
            Validation.ThrowIfNotValidUserRequest(userRequest);
            var success = await this.Users.UpdateUserAsync(userRequest);
            return success;
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
        /// Sends trade to logger for external
        /// persistence
        /// </summary>
        /// <param name="trade"></param>
        /// <returns></returns>
        private async Task AddTradeToLogAsync(Trade trade)
        {
            var randomParitionId = NextInt64(rand);
            var content = new StringContent(JsonConvert.SerializeObject(trade), Encoding.UTF8, "application/json");
            HttpResponseMessage res;
            try
            {
                res = await client.PostAsync($"http://localhost:{reverseProxyPort}/Exchange/Logger/api/logger?PartitionKey={randomParitionId.ToString()}&PartitionKind=Int64Range", content);
                if (!res.IsSuccessStatusCode)
                {
                    throw new TradeNotLoggedException();
                }
            }
            catch (HttpRequestException ex)
            {
                // This will force a retry
                ServiceEventSource.Current.ServiceMessage(this.Context, $"Error sending trade to logger service, error: {ex.Message}");
                throw new TradeNotLoggedException();
            }
        }

        /// <summary>
        /// Generates a random int 64
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
        private async Task<bool> executeTradeAsync(Trade trade, User seller, User buyer)
        {
            // This is not atomic - trades may be applied to one or both user
            // and then the program could crash. Rather than worry about
            // distributed transactions here, duplication is checked during
            // validation.
            var newBuyer = UpdateBuyer(trade, buyer);
            var newSeller = UpdateSeller(trade, seller);
            var buyerIsUpdated = await Users.UpdateUserAsync(newBuyer);
            var sellerIsUpdated = await Users.UpdateUserAsync(newSeller);
            return (buyerIsUpdated && sellerIsUpdated);
        }

        private static User UpdateSeller(Trade trade, User seller)
        {
            var newSeller = seller.AddTrade(trade.Id);
            newSeller.UpdateCurrencyAmount(trade.Settlement.Pair.GetBuyerWantCurrency(), trade.Settlement.Amount * -1.0);
            newSeller.UpdateCurrencyAmount(trade.Settlement.Pair.GetSellerWantCurrency(), (trade.Settlement.Amount * trade.Settlement.Price));
            newSeller = new User(newSeller.Id,
                                newSeller.Username,
                                newSeller.CurrencyAmounts,
                                newSeller.LatestTrades);
            return newSeller;
        }

        private static User UpdateBuyer(Trade trade, User buyer)
        {
            var newBuyer = buyer.AddTrade(trade.Id);
            newBuyer.UpdateCurrencyAmount(trade.Settlement.Pair.GetBuyerWantCurrency(), trade.Settlement.Amount);
            newBuyer.UpdateCurrencyAmount(trade.Settlement.Pair.GetSellerWantCurrency(), (trade.Settlement.Amount * trade.Settlement.Price) * -1.0);
            newBuyer = new User(newBuyer.Id,
                            newBuyer.Username,
                            newBuyer.CurrencyAmounts,
                            newBuyer.LatestTrades);
            return newBuyer;
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Throttle loop:
                    // This limit cannot be removed or you will fail an audit.
                    if (await Trades.CountAsync() < 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                }
                catch (FabricNotReadableException)
                {
                    // Fabric is not yet readable - this is a transient exception
                    // Backing off temporarily before retrying
                    await BackOff(cancellationToken);
                    continue;
                }

                using (var tx = this.StateManager.CreateTransaction())
                {
                    try
                    {
                        var trade = await this.Trades.DequeueAsync(tx, cancellationToken);

                        if (trade != null)
                        {
                            // Get the buyer and seller associated with the trade
                            var seller = await Users.GetUserAsync(trade.Ask.UserId);
                            var buyer = await Users.GetUserAsync(trade.Bid.UserId);

                            bool duplicateOrder = false;

                            // If the trade is invalid, we'll throw an exception
                            // and remove it from the queue. If there is a transient
                            // failure, we'll abort and put it back on the queue.
                            try
                            {
                                Validation.ThrowIfNotValidTrade(trade, seller, buyer);
                            }
                            catch (BadBuyerException ex)
                            {
                                ServiceEventSource.Current.Message($"Dropping bad trade with reason: {ex.Message}");

                                await tx.CommitAsync(); // Removes invalid orders from queue (in a real system we would re-add the valid ask)
                                continue;
                            }
                            catch (BadSellerException ex)
                            {
                                ServiceEventSource.Current.Message($"Dropping bad trade with reason: {ex.Message}");

                                await tx.CommitAsync(); // Removes invalid orders from queue (in a real system we would re-add the valid bid)
                                continue;
                            }
                            catch (DuplicateAskException)
                            {
                                duplicateOrder = true;
                                ServiceEventSource.Current.Message($"Duplicate ask ${trade.Id}");
                            }
                            catch (DuplicateBidException)
                            {
                                duplicateOrder = true;
                                ServiceEventSource.Current.Message($"Duplicate bid ${trade.Id}");
                            }

                            try
                            {
                                await AddTradeToLogAsync(trade);
                            }
                            catch (TradeNotLoggedException)
                            {
                                // Failed to log the trade, backoff and retry
                                await BackOff(cancellationToken);
                                continue;
                            }

                            // If a duplicate order short cut the loop.
                            // Reconcilation can happen on the backend.
                            if (duplicateOrder) continue;  

                            var applied = await executeTradeAsync(trade, seller, buyer);
                            if (applied)
                            {
                                // Applied indicates both buyer and seller have been
                                // successfully updated and the trade has been logged.
                                // We can now commit the transaction to release our lock
                                // on the queue.

                                ServiceEventSource.Current.ServiceMessage(this.Context, $"trade {trade.Id} completed");
                                await tx.CommitAsync();

                                MetricsLog.Traded(trade, Metrics.TradedStatus.Completed);  // Record trade success
                            }
                            else
                            {
                                // Atleast one of the buyer or seller failed to update
                                // or we failed to log the trade.

                                // We are immediately dropping any failed message this
                                // would normally be deadlettered.
                                ServiceEventSource.Current.ServiceMessage(this.Context, $"trade {trade.Id} aborted");
                                tx.Abort();

                                MetricsLog.Traded(trade, Metrics.TradedStatus.Failed); // Record trade failed
                            }
                        }
                    }
                    catch (TradeNotLoggedException)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Trade failed to log, abort transaction");

                        tx.Abort();
                        await BackOff(cancellationToken);
                        continue;
                    }
                    catch (FabricNotPrimaryException)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Fabric cannot perform write as it is not the primary replica");
                        return;
                    }
                    catch (FabricNotReadableException)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Fabric is not currently readable, aborting and will retry");

                        tx.Abort();
                        await BackOff(cancellationToken);
                        continue;
                    }
                    catch (InvalidOperationException)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Invalid operation performed, aborting and will retry");

                        tx.Abort();
                        await BackOff(cancellationToken);
                        continue;
                    }
                    catch (TimeoutException)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Operation timed out, aborting and will retry");

                        tx.Abort();
                        await BackOff(cancellationToken);
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// BackOff will delay execution for n seconds
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task BackOff(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message($"Fulfillment is backing off for {backOffDurationInSec} seconds");
            await Task.Delay(TimeSpan.FromSeconds(backOffDurationInSec), cancellationToken);
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
                                            .AddSingleton<StatefulServiceContext>(serviceContext)
                                            .AddSingleton<IReliableStateManager>(this.StateManager)
                                            .AddSingleton<Fulfillment>(this)
                                            .AddSingleton<ITelemetryInitializer>((serviceProvider) => FabricTelemetryInitializerExtension.CreateFabricTelemetryInitializer(serviceContext))
                                            .AddSingleton<ITelemetryModule>(new ServiceRemotingDependencyTrackingTelemetryModule())
                                            .AddSingleton<ITelemetryModule>(new ServiceRemotingRequestTrackingTelemetryModule()))
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
