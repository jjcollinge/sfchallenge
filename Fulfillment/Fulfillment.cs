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

namespace Fulfillment
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    public class Fulfillment : StatefulService
    {
        public const string TransferQueueName = "transfers";
        public const string UserStoreName = "users";
        private TransferQueue Transfers;
        private Users Users;

        public Fulfillment(StatefulServiceContext context)
            : base(context)
        {
            this.Transfers = new TransferQueue(this.StateManager, TransferQueueName);
            this.Users = new Users(this.StateManager, UserStoreName);
        }

        public Fulfillment(StatefulServiceContext context, IReliableStateManagerReplica reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        {
            this.Transfers = new TransferQueue(this.StateManager, TransferQueueName);
            this.Users = new Users(this.StateManager, UserStoreName);
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
            var transferId = await this.Transfers.PushAsync(transferRequest);
            return transferId;
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
        public async Task<List<User>> GetUsersAsync()
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
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
#endif
                using (var tx = this.StateManager.CreateTransaction())
                {

                    try
                    {
                        // Pop a transfer of the transfer queue
                        var transfer = await this.Transfers.PopAsync(tx);
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
                            }
                            else
                            {
                                // Atleast one of the buyer or seller failed to update
                                // We will abort the transaction and assume a transient
                                // failure.
                                tx.Abort();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Exception thrown when handling transfer. Assume invalid transfer,
                        // we'll commit the transaction to release the lock on the queue,
                        // log the exception and then skip to the next transfer in the queue.
                        await tx.CommitAsync();
                        ServiceEventSource.Current.Message($"Dropping bad transfer. Reason: {ex.Message}");
                        continue;
                    }
                }
            }
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
                throw new Exception($"Matched seller '{seller.Id}' doesn't exist");
            }
            if (buyer == null)
            {
                throw new Exception($"Matched seller '{buyer.Id}' doesn't exist");
            }
            if (seller.Quantity < transfer.Bid.Quantity)
            {
                throw new Exception($"Matched seller '{seller.Id}' doesn't have suffient quantity to satisfy the transfer");
            }
            if (buyer.Balance < transfer.Bid.Value)
            {
                throw new Exception($"Matched buyer '{buyer.Id}' doesn't have suffient balance to satisfy the transfer");
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
                            buyer.Balance - transfer.Bid.Value,
                            buyerTransfers);

            // Apply the transfer operations
            // to the seller.
            var sellerTransfers = seller.Transfers.ToList();
            sellerTransfers.Add(transfer);
            seller = new User(seller.Id,
                                seller.Username,
                                seller.Quantity - transfer.Bid.Quantity,
                                seller.Balance + transfer.Bid.Value,
                                sellerTransfers);

            //TODO: Invesitgate failure modes.
            var buyerUpdated = await Users.UpdateUserAsync(tx, buyer);
            var sellerUpdated = await Users.UpdateUserAsync(tx, seller);
            
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
