using System.Collections.Generic;
using System.Fabric;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using UserStore.Interface;
using Common;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Threading;
using Microsoft.ServiceFabric.Data;

namespace UserStore
{
    /// <summary>
    /// A statefull service, used to store Users. Access via binary remoting based on the V2 Remoting stack
    /// </summary>
    public sealed class UserStore : StatefulService, IUserStore
    {
        public const string StateManagerKey = "UserStore";

        public UserStore(StatefulServiceContext context)
            : base(context)
        { }
        public UserStore(StatefulServiceContext context, IReliableStateManagerReplica2 reliableStateManagerReplica)
            : base(context, reliableStateManagerReplica)
        { }

        /// <summary>
        /// Standard implementation for service endpoints using the V2 Remoting stack. See more: https://aka.ms/servicefabricservicecommunication
        /// </summary>
        /// <remarks>
        /// Iterates over the ServiceManifest.xml and expects a endpoint with name ServiceEndpointV2 to expose the service over Remoting.
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return this.CreateServiceRemotingReplicaListeners();
        }

        public async Task<User> GetUserAsync(string userId)
        {
            IReliableDictionary<string, User> users =
               await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            User user = null;
            using (var tx = this.StateManager.CreateTransaction())
            {
                var tryUser = await users.TryGetValueAsync(tx, userId);
                if (tryUser.HasValue)
                {
                    user = tryUser.Value;
                }
                await tx.CommitAsync();
            }
            return user;
        }

        public async Task<List<User>> GetUsersAsync()
        {
            IReliableDictionary<string, User> users =
               await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            var maxExecutionTime = TimeSpan.FromSeconds(30D); // Stop retrieving users after 30 sec
            var cancellationToken = new CancellationTokenSource(maxExecutionTime).Token;

            var returnList = new List<User>();
            
            using (var tx = this.StateManager.CreateTransaction())
            {
                var asyncEnumerable = await users.CreateEnumerableAsync(tx);
                var asyncEnumerator = asyncEnumerable.GetAsyncEnumerator();

                try
                {
                    while (await asyncEnumerator.MoveNextAsync(cancellationToken))
                    {
                        returnList.Add(asyncEnumerator.Current.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }

                await tx.CommitAsync();
            }
            return returnList;
        }

        public async Task<string> AddUserAsync(User user)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            using (var tx = this.StateManager.CreateTransaction())
            {
                await users.AddAsync(tx, user.Id, user);
                await tx.CommitAsync();
            }
            return user.Id;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            using (var tx = this.StateManager.CreateTransaction())
            {
                bool result;
                var current = await users.TryGetValueAsync(tx, user.Id, LockMode.Update);
                if (current.HasValue)
                {
                    result = await users.TryUpdateAsync(tx, user.Id, user, current.Value);
                    await tx.CommitAsync();
                }
                else
                {
                    throw new ApplicationException($"Cannot update non existent user '{user.Id}'");
                }

                return result;
            }
        }
        
        public async Task<bool> DeleteUserAsync(string userId)
        {
            IReliableDictionary<string, User> users =
              await this.StateManager.GetOrAddAsync<IReliableDictionary<string, User>>(StateManagerKey);

            bool removed;
            using (var tx = this.StateManager.CreateTransaction())
            {
                var result = await users.TryRemoveAsync(tx, userId);
                if (result.HasValue)
                {
                    removed = true;
                }
                else
                {
                    removed = false;
                }
                await tx.CommitAsync();
            }
            return removed;
        }
    }
}