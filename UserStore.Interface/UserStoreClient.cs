using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Query;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Microsoft.ServiceFabric.Services.Client;
using Microsoft.ServiceFabric.Services.Communication.Client;
using Microsoft.ServiceFabric.Services.Remoting.Client;

namespace UserStore.Interface
{
    /// <summary>
    /// A partition aware UserStore client. Distributes the users evenly across all partitions.
    /// </summary>
    public class UserStoreClient : IUserStore
    {
        private readonly ServiceProxyFactory _serviceProxyFactory;
        private readonly Uri _userStoreServiceUri;
        public UserStoreClient()
        {
            var settings = new OperationRetrySettings(
                maxRetryBackoffIntervalOnNonTransientErrors: TimeSpan.FromSeconds(2.0), 
                maxRetryBackoffIntervalOnTransientErrors: TimeSpan.FromSeconds(2.0), 
                defaultMaxRetryCount: 3);

            _serviceProxyFactory = new ServiceProxyFactory(settings);

            var serviceUriBuilder = new ServiceUriBuilder("UserStore");
            _userStoreServiceUri = serviceUriBuilder.ToUri();
        }

        public async Task<string> AddUserAsync(User user, CancellationToken cancellationToken)
        {
            var userStoreProxy = GetUserStoreProxy(user);

            return await userStoreProxy.AddUserAsync(user, cancellationToken);
        }

        public async Task<bool> UpdateUsersAsync(List<User> users, CancellationToken cancellationToken)
        {
            var userStoreProxy = GetUserStoreProxy(users.First());

            return await userStoreProxy.UpdateUsersAsync(users, cancellationToken);
        }

        public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken)
        {
            var userStoreProxy = _serviceProxyFactory.CreateServiceProxy<IUserStore>(_userStoreServiceUri);
            var usersInPartition = await userStoreProxy.GetUsersAsync(cancellationToken);
            return usersInPartition;
        }

        public async Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken)
        {
            var userStoreProxy = GetUserStoreProxy(userId);

            return await userStoreProxy.DeleteUserAsync(userId, cancellationToken);
        }

        public async Task<User> GetUserAsync(string userId)
        {
            var userStoreProxy = GetUserStoreProxy(userId);

            return await userStoreProxy.GetUserAsync(userId);
        }

        public async Task<bool> UpdateUserAsync(User user, CancellationToken cancellationToken)
        {
            var userStoreProxy = GetUserStoreProxy(user);

            return await userStoreProxy.UpdateUserAsync(user, cancellationToken);
        }

        private IUserStore GetUserStoreProxy(string userId)
        {
            return _serviceProxyFactory.CreateServiceProxy<IUserStore>(_userStoreServiceUri);
        }

        private IUserStore GetUserStoreProxy(User user)
        {
            return _serviceProxyFactory.CreateServiceProxy<IUserStore>(_userStoreServiceUri);
        }
    }
}