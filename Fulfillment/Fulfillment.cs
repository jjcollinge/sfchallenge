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
        public const string TransferQueueName = "transfers";
        private TransferQueue Transfers;
        private readonly UserStoreClient Users;
        private static readonly HttpClient client = new HttpClient();
        private string loggerEndpoint;
        private string orderBookEndpoint;

        public Fulfillment(StatefulServiceContext context)
            : base(context)
        {
            Init();
            this.Transfers = new TransferQueue(this.StateManager, TransferQueueName);
            this.Users = new UserStoreClient();
        }

        public Fulfillment(StatefulServiceContext context, IReliableStateManagerReplica reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        {
            Init();
            this.Transfers = new TransferQueue(this.StateManager, TransferQueueName);
            this.Users = new UserStoreClient();
        }

        private void Init()
        {
            // Get configuration from our PackageRoot/Config/Setting.xml file
            var configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            var dnsName = configurationPackage.Settings.Sections["FulfillmentConfig"].Parameters["Logger_DnsName"].Value;
            var port = configurationPackage.Settings.Sections["FulfillmentConfig"].Parameters["Logger_Port"].Value;

            loggerEndpoint = $"http://{dnsName}:{port}";

            configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            dnsName = configurationPackage.Settings.Sections["FulfillmentConfig"].Parameters["OrderBook_DnsName"].Value;
            port = configurationPackage.Settings.Sections["FulfillmentConfig"].Parameters["OrderBook_Port"].Value;

            orderBookEndpoint = $"http://{dnsName}:{port}";
        }

        /// <summary>
        /// Checks whether the provided transfer
        /// meets the validity requirements. If it
        /// does, adds it the transfer queue.
        /// </summary>
        /// <param name="transfer"></param>
        /// <returns></returns>
        public async Task<string> AddTransferAsync(TransferRequestModel transferRequest)
        {
            Validation.ThrowIfNotValidTransferRequest(transferRequest);
            var transferId = await this.Transfers.EnqueueAsync(transferRequest);
            return transferId;
        }

        /// <summary>
        /// Gets the count of the number of transfers currently
        /// in the transfer queue
        /// </summary>
        /// <returns></returns>
        public async Task<long> GetTransfersCountAsync()
        {
            return await this.Transfers.CountAsync();
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
            var content = new StringContent(JsonConvert.SerializeObject(order), Encoding.UTF8, "application/json");
            var addTransferUri = $"{orderBookEndpoint}/api/orders/ask";
            await client.PostAsync(addTransferUri, content); //TODO: Handle errors
        }

        /// <summary>
        /// Attempts to re-order a bid
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private async Task ReOrderBidAsync(Order order)
        {
            var content = new StringContent(JsonConvert.SerializeObject(order), Encoding.UTF8, "application/json");
            var addTransferUri = $"{orderBookEndpoint}/api/orders/bid";
            await client.PostAsync(addTransferUri, content); //TODO: Handle errors
        }

        /// <summary>
        /// Sends transfer to logger for external
        /// persistence
        /// </summary>
        /// <param name="transfer"></param>
        /// <returns></returns>
        private async Task<bool> LogAsync(Transfer transfer)
        {
            var content = new StringContent(JsonConvert.SerializeObject(transfer), Encoding.UTF8, "application/json");
            var addLogUri = $"{loggerEndpoint}/api/logger";
            var res = await client.PostAsync(addLogUri, content); //TODO: Handle errors
            return res.IsSuccessStatusCode;
        }

        /// <summary>
        /// Attempts to transfer value and goods between
        /// the buyer and seller. We pass our existing
        /// transaction into the update to allow it to
        /// be commited atomically.
        /// </summary>
        /// <param name="tx"></param>
        /// <param name="transfer"></param>
        /// <param name="seller"></param>
        /// <param name="buyer"></param>
        /// <returns></returns>
        private async Task<bool> ApplyTransferAsync(ITransaction tx, Transfer transfer, User seller, User buyer)
        {
            var buyerTransfers = buyer.TransferIds.ToList();
            buyerTransfers.Add(transfer.Id);
            buyer = new User(buyer.Id,
                            buyer.Username,
                            buyer.Quantity + transfer.Bid.Quantity,
                            buyer.Balance - (transfer.Bid.Value * transfer.Bid.Quantity),
                            buyerTransfers);

            var sellerTransfers = seller.TransferIds.ToList();
            sellerTransfers.Add(transfer.Id);
            seller = new User(seller.Id,
                                seller.Username,
                                seller.Quantity - transfer.Bid.Quantity,
                                seller.Balance + (transfer.Bid.Value * transfer.Bid.Quantity),
                                sellerTransfers);

            var buyerUpdated = await Users.UpdateUserAsync(buyer);
            var sellerUpdated = await Users.UpdateUserAsync(seller);
            var logged = await LogAsync(transfer);

            return (buyerUpdated && sellerUpdated && logged);
        }


        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (true)
            {
                // Throttle loop:
                // This limit cannot be removed or you will fail an audit.
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                using (var tx = this.StateManager.CreateTransaction())
                {
                    Transfer transfer = null;
                    transfer = await this.Transfers.DequeueAsync(tx);

                    if (transfer != null)
                    {
                        // Get the buyer and seller associated with the transfer
                        var seller = await Users.GetUserAsync(transfer.Ask.UserId);
                        var buyer = await Users.GetUserAsync(transfer.Bid.UserId);

                        // If the transfer is invalid, we'll throw an exception
                        // and remove it from the queue. If there is a transient
                        // failure, we'll abort and put it back on the queue.
                        try
                        {
                            Validation.ThrowIfNotValidTransfer(transfer, seller, buyer);
                        }
                        catch (BadBuyerException ex)
                        {
                            await tx.CommitAsync();
                            ServiceEventSource.Current.Message($"Dropping bad transfer. Reason: {ex.Message}");

                            // Buyer was invalid, Seller's ask may
                            // still be valid.
                            if (transfer != null)
                            {
                                await ReOrderAskAsync(transfer.Ask);
                                ServiceEventSource.Current.Message($"Reordering ask {transfer.Ask.Id} for new match");
                            }
                            continue;
                        }
                        catch (BadSellerException ex)
                        {
                            await tx.CommitAsync();
                            ServiceEventSource.Current.Message($"Dropping bad transfer. Reason: {ex.Message}");

                            // Seller was invalid, Buyer's bid may
                            // still be valid.
                            if (transfer != null)
                            {
                                ServiceEventSource.Current.Message($"Reordering bid {transfer.Bid.Id} for new match");
                                await ReOrderBidAsync(transfer.Bid);
                            }
                            continue;
                        }

                        var applied = await ApplyTransferAsync(tx, transfer, seller, buyer);
                        // Applied indicates both buyer and seller have been
                        // successfully updated and the transfer has been logged.
                        // We can now commit the transaction to release our lock
                        // on the queue.
                        if (applied)
                        {
                            await tx.CommitAsync();
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"transfer {transfer.Id} completed");
                        }
                        // Atleast one of the buyer or seller failed to update
                        // or we failed to log the transfer.
                        // We will abort the transaction and assume a transient
                        // failure.
                        else
                        {
                            tx.Abort();
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"transfer {transfer.Id} aborted");
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
