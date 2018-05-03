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

namespace Logger
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    public sealed class Logger : StatefulService
    {
        public const string QueueName = "toExport";
        private const string databaseName = "exchange";
        private const string collectionName = "transfers";
        private ITransferLogger transferLogger;

        public Logger(StatefulServiceContext context)
            : base(context)
        {
            Init();
        }

        private void Init()
        {
            ConfigurationPackage configPackage = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            String connectionString = configPackage.Settings.Sections["CosmosDB"].Parameters["ConnectionString"].Value;
            transferLogger = MongoDBTransferLogger.Create(connectionString, databaseName, collectionName);
        }

        public async Task LogAsync(Transfer transfer)
        {
            IReliableQueue<Transfer> exportQueue =
             await this.StateManager.GetOrAddAsync<IReliableQueue<Transfer>>(QueueName);

            using (var tx = this.StateManager.CreateTransaction())
            {
                // Add transfer to log queue
                await exportQueue.EnqueueAsync(tx, transfer);
                await tx.CommitAsync();
            }
        }

        public async Task ClearAsync()
        {
            IReliableQueue<Transfer> exportQueue =
             await this.StateManager.GetOrAddAsync<IReliableQueue<Transfer>>(QueueName);

            using (var tx = this.StateManager.CreateTransaction())
            {
                // Clear the external log transfer store
                await transferLogger.ClearAsync();

                // Clear the queue
                await exportQueue.ClearAsync();
                await tx.CommitAsync();
            }
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReliableQueue<Transfer> exportQueue =
             await this.StateManager.GetOrAddAsync<IReliableQueue<Transfer>>(QueueName);

            // Take each transfer from the queue and
            // insert it into an external transfer
            // log store.
            while (true)
            {
                using (var tx = this.StateManager.CreateTransaction())
                {
                    var result = await exportQueue.TryDequeueAsync(tx);
                    if (result.HasValue)
                    {
                        var transfer = result.Value;
                        await transferLogger.InsertAsync(transfer);

                        await tx.CommitAsync();
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
                                            .AddSingleton<Logger>(this))
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
