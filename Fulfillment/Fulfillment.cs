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

        public ConfigurationPackage configPackage { get; private set; }

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
            configPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
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
            IsValidTransferRequest(transferRequest);
            var pendingTransfers = await Transfers.CountAsync();
            var maxPendingTransfers = int.Parse(configPackage.Settings.Sections["FulfillmentConfig"].Parameters["Fulfillment_MaxTransfersPending"].Value);
            if (pendingTransfers > maxPendingTransfers)
            {
                ServiceEventSource.Current.ServiceMaxPendingLimitHit();
                throw new MaxPendingTransfersExceededException(pendingTransfers);
            }

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
            IsValidUserRequest(userRequest);
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

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                // Respect the cancellation token so that the Service Fabric
                // runtime can kill us properly.
                cancellationToken.ThrowIfCancellationRequested();


#if DEBUG
                // Throttle loop: This limit
                // cannot be removed or you will fail an Audit. 
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
#endif

                using (var tx = this.StateManager.CreateTransaction())
                {
                    Transfer transfer = null;
                    try
                    {
                        // Pop a transfer of the transfer queue
                        transfer = await this.Transfers.DequeueAsync(tx);
                        if (transfer != null)
                        {
                            // Get the buyer and seller associated with the transfer
                            var seller = await Users.GetUserAsync(transfer.Ask.UserId);
                            var buyer = await Users.GetUserAsync(transfer.Bid.UserId);

                            // Conditions that should flag a transfer as invalid and thus
                            // we do not want to put it back on the queue should throw
                            // an exception.
                            // Conditions where it may be a transient failure and we wish
                            // to retry should abort the transaction.
                            IsValidTransfer(transfer, seller, buyer);

                            // Apply the transfer
                            var applied = await ApplyTransferAsync(tx, transfer, seller, buyer);
                            if (applied)
                            {
                                // Applied indicates both buyer and seller have been
                                // successfully updated. We can now commit the transaction
                                // to release our lock on the queue.
                                await tx.CommitAsync();
                                ServiceEventSource.Current.ServiceMessage(this.Context, $"transfer {transfer.Id} completed");
                            }
                            else
                            {
                                // Atleast one of the buyer or seller failed to update
                                // We will abort the transaction and assume a transient
                                // failure.
                                tx.Abort();
                                ServiceEventSource.Current.ServiceMessage(this.Context, $"transfer {transfer.Id} aborted");
                            }
                        }
                    }
                    catch (BadBuyerException ex)
                    {
                        await tx.CommitAsync();
                        ServiceEventSource.Current.Message($"Dropping bad transfer. Reason: {ex.Message}");

                        if (transfer != null)
                        {
                            await RedoOrderAsync(transfer.Ask);
                        }
                        continue;
                    }
                    catch (BadSellerException ex)
                    {
                        await tx.CommitAsync();
                        ServiceEventSource.Current.Message($"Dropping bad transfer. Reason: {ex.Message}");

                        if (transfer != null)
                        {
                            await RedoOrderAsync(transfer.Bid);
                        }
                        continue;
                    }
                }
            }
        }

        private async Task RedoOrderAsync(Order order)
        {
            var configurationPackage = Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            var dnsName = configurationPackage.Settings.Sections["FulfillmentConfig"].Parameters["OrderBook_DnsName"].Value;
            var port = configurationPackage.Settings.Sections["FulfillmentConfig"].Parameters["OrderBook_Port"].Value;

            var orderBookEndpoint = $"http://{dnsName}:{port}";

            var content = new StringContent(JsonConvert.SerializeObject(order), Encoding.UTF8, "application/json");
            var addOrderUri = $"{orderBookEndpoint}/api/orders";
            HttpResponseMessage res;
            res = await client.PostAsync(addOrderUri, content);
        }

        public void IsValidUserRequest(UserRequestModel user)
        {
            if (string.IsNullOrWhiteSpace(user.Username))
            {
                throw new InvalidTransferRequestException("Username cannot be null, empty or contain whitespace");
            }
        }

        private void IsValidTransferRequest(Transfer transfer)
        {
            if (transfer.Ask == null || transfer.Bid == null)
            {
                throw new InvalidTransferRequestException("Bid or ask cannot be null");
            }
            if (transfer.Ask.Value > transfer.Bid.Value)
            {
                throw new InvalidTransferRequestException("The ask value cannot be higher than the bid value");
            }
            if (transfer.Ask.Quantity < transfer.Bid.Quantity)
            {
                throw new InvalidTransferRequestException("The ask quantity cannot be lower than the bid quantity");
            }
        }

        /// <summary>
        /// Checks whether a transfer meets the validity requirements
        /// </summary>
        /// <param name="transfer"></param>
        /// <param name="seller"></param>
        /// <param name="buyer"></param>
        private static void IsValidTransfer(Transfer transfer, User seller, User buyer)
        {
            if (seller == null)
            {
                throw new BadSellerException($"Matched seller '{seller}' doesn't exist");
            }
            if (buyer == null)
            {
                throw new BadBuyerException($"Matched seller '{buyer}' doesn't exist");
            }
            if (seller.Quantity < transfer.Bid.Quantity)
            {
                throw new BadSellerException($"Matched seller '{seller.Id}' doesn't have suffient quantity to satisfy the transfer");
            }
            if (buyer.Balance < transfer.Bid.Value)
            {
                throw new BadBuyerException($"Matched buyer '{buyer.Id}' doesn't have suffient balance to satisfy the transfer");
            }
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
            // Apply the transfer operations
            // to the buyer.
            var buyerTransfers = buyer.Transfers.ToList();
            buyerTransfers.Add(transfer);
            buyer = new User(buyer.Id,
                            buyer.Username,
                            buyer.Quantity + transfer.Bid.Quantity,
                            buyer.Balance - (transfer.Bid.Value * transfer.Bid.Quantity),
                            buyerTransfers);

            // Apply the transfer operations
            // to the seller.
            var sellerTransfers = seller.Transfers.ToList();
            sellerTransfers.Add(transfer);
            seller = new User(seller.Id,
                                seller.Username,
                                seller.Quantity - transfer.Bid.Quantity,
                                seller.Balance + (transfer.Bid.Value * transfer.Bid.Quantity),
                                sellerTransfers);

            //TODO: Invesitgate failure modes.
            var buyerUpdated = await Users.UpdateUserAsync(buyer);
            var sellerUpdated = await Users.UpdateUserAsync(seller);

            return (buyerUpdated && sellerUpdated);
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
