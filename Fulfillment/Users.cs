using Common;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class Users
    {
        private string storeName = "";
        private IReliableStateManager stateManager;

        public Users(IReliableStateManager stateManager,string storeName)
        {
            this.stateManager = stateManager;
            this.storeName = storeName;
        }

        public async Task<List<User>> GetUsersAsync()
        {
            List<User> result = new List<User>();

            IReliableDictionary<string, User> users =
              await this.stateManager.GetOrAddAsync<IReliableDictionary<string, User>>(storeName);

            using (var tx = this.stateManager.CreateTransaction())
            {
                Microsoft.ServiceFabric.Data.IAsyncEnumerable<KeyValuePair<string, User>> enumerable = await users.CreateEnumerableAsync(tx);
                Microsoft.ServiceFabric.Data.IAsyncEnumerator<KeyValuePair<string, User>> enumerator = enumerable.GetAsyncEnumerator();

                while (await enumerator.MoveNextAsync(CancellationToken.None))
                {
                    result.Add(enumerator.Current.Value);
                }
                await tx.CommitAsync();
            }
            return result;
        }

        public async Task<User> GetUserAsync(string id)
        {
            IReliableDictionary<string, User> users =
               await this.stateManager.GetOrAddAsync<IReliableDictionary<string, User>>(storeName);

            User user = null;
            using (var tx = this.stateManager.CreateTransaction())
            {
                var tryUser = await users.TryGetValueAsync(tx, id);
                if (tryUser.HasValue)
                {
                    user = tryUser.Value;
                }
                await tx.CommitAsync();
            }
            return user;
        }

        public async Task<bool> Exists(string userId)
        {
            IReliableDictionary<string, User> users =
              await this.stateManager.GetOrAddAsync<IReliableDictionary<string, User>>(storeName);

            using (var tx = this.stateManager.CreateTransaction())
            {
                var exists = await users.ContainsKeyAsync(tx, userId);
                await tx.CommitAsync();
                return exists;
            }
        }

        public async Task<string> AddUserAsync(User user)
        {
            IReliableDictionary<string, User> users =
              await this.stateManager.GetOrAddAsync<IReliableDictionary<string, User>>(storeName);

            using (var tx = this.stateManager.CreateTransaction())
            {
                await users.AddAsync(tx, user.Id, user);
                await tx.CommitAsync();
            }
            return user.Id;
        }

        public async Task<bool> UpdateUserAsync(ITransaction tx, User user)
        {
            IReliableDictionary<string, User> users =
              await this.stateManager.GetOrAddAsync<IReliableDictionary<string, User>>(storeName);

            var current = await users.TryGetValueAsync(tx, user.Id, LockMode.Update);
            if (current.HasValue)
            {
                return await users.TryUpdateAsync(tx, user.Id, user, current.Value);
            }
            else
            {
                throw new Exception($"Cannot update non existent user '{user.Id}'");
            }
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            IReliableDictionary<string, User> users =
              await this.stateManager.GetOrAddAsync<IReliableDictionary<string, User>>(storeName);

            bool removed;
            using (var tx = this.stateManager.CreateTransaction())
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
