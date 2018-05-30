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
using Microsoft.ServiceFabric.Data.Collections;
using System.Security;
using MongoDB.Bson;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.ServiceFabric;

namespace Logger
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    public sealed class Logger : StatefulService
    {
        public const string QueueName = "toExport";
        private const string databaseName = "exchange";
        private const string collectionName = "trades";
        private ITradeLogger tradeLogger;
        private const int backOffDurationInSec = 2;

        public Logger(StatefulServiceContext context)
            : base(context)
        {
            Init();
        }

        public Logger(StatefulServiceContext context, IReliableStateManagerReplica reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        {
        }

        /// <summary>
        /// Init setups in any configuration values
        /// </summary>
        private void Init()
        {
            ConfigurationPackage configPackage = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            String connectionString = configPackage.Settings.Sections["DB"].Parameters["MongoConnectionString"].Value;
            bool.TryParse(configPackage.Settings.Sections["DB"].Parameters["MongoEnableSSL"].Value, out var enableSsl);
            tradeLogger = MongoDBTradeLogger.Create(connectionString, enableSsl, databaseName, collectionName);
        }

        /// <summary>
        /// Adds a trade to the trade log
        /// </summary>
        /// <param name="trade"></param>
        /// <returns></returns>
        public async Task LogAsync(Trade trade, CancellationToken cancellationToken)
        {
            IReliableConcurrentQueue<Trade> trades =
             await this.StateManager.GetOrAddAsync<IReliableConcurrentQueue<Trade>>(QueueName);

            var executed = false;
            var retryCount = 0;
            List<Exception> exceptions = new List<Exception>();
            while (!executed && retryCount < 3)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await executeAddTradeAsync(trade, trades, cancellationToken);
                    executed = true;
                }
                catch (TimeoutException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
                catch (TransactionFaultedException ex)
                {
                    exceptions.Add(ex);
                    retryCount++;
                    continue;
                }
            }
            if (exceptions.Count > 0)
                throw new AggregateException(
                    "Encounted errors while trying to add trade",
                    exceptions);
        }

        private async Task executeAddTradeAsync(Trade trade, IReliableConcurrentQueue<Trade> exportQueue, CancellationToken cancellationToken)
        {
            using (var tx = this.StateManager.CreateTransaction())
            {
                await exportQueue.EnqueueAsync(tx, trade, cancellationToken);
                await tx.CommitAsync();
            }
        }

        public async Task<long> ActiveTradeCountAsync()
        {
            IReliableConcurrentQueue<Trade> trades =
             await this.StateManager.GetOrAddAsync<IReliableConcurrentQueue<Trade>>(QueueName);

            return trades.Count;
        }

        public async Task<long> LoggedTradeCountAsync(CancellationToken cancellationToken)
        {
            return await tradeLogger.CountAsync(cancellationToken);
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            IReliableConcurrentQueue<Trade> exportQueue =
             await this.StateManager.GetOrAddAsync<IReliableConcurrentQueue<Trade>>(QueueName);

            // Take each trade from the queue and insert
            // it into an external trade log store.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Wait to process until logs are received, check anyway if timeout occurs
                    if (exportQueue.Count < 1)
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
                catch (FabricNotPrimaryException)
                {
                    return;
                }
               
                using (var tx = this.StateManager.CreateTransaction())
                {
                    try
                    {
                        // This can be batched...
                        var result = await exportQueue.TryDequeueAsync(tx, cancellationToken);
                        if (result.HasValue)
                        {
                            var trade = result.Value;

                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Writing trade {trade.Id} to log");
                            await tradeLogger.InsertAsync(trade, cancellationToken);
                            await tx.CommitAsync();
                        }
                    }
                    catch (LoggerDisconnectedException)
                    {
                        // Logger may have lost connection
                        // Back off and retry connection
                        await BackOff(cancellationToken);
                        Init(); // reinitialize connection
                        continue;
                    }
                    catch (FabricNotPrimaryException)
                    {
                        // Attempted to perform write on a non
                        // primary replica.
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Fabric cannot perform write as it is not the primary replica");
                        return;
                    }
                    catch (FabricNotReadableException)
                    {
                        // Fabric is not yet readable - this is a transient exception
                        // Backing off temporarily before retrying
                        await BackOff(cancellationToken);
                        continue;
                    }
                    catch (InsertFailedException ex)
                    {
                        // Insert failed, assume connection problem and transient.
                        // backoff and retry
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Logger error,  {ex.Message}");
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
            ServiceEventSource.Current.Message($"Logger is backing off for {backOffDurationInSec} seconds");
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
                                            .AddSingleton<Logger>(this)
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
