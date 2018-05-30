using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.FabricTransport;
using System.Threading.Tasks;
using Common;
using System.Collections.Generic;
using System.Threading;

[assembly: FabricTransportServiceRemotingProvider(RemotingListener = RemotingListener.V2Listener, RemotingClient = RemotingClient.V2Client)]
namespace UserStore.Interface
{
    public interface IUserStore : IService
    {
        Task<User> GetUserAsync(string userId);
        /// <remarks>
        /// V2 Remoting bug does not allow return types to be of none concrete types like IEnumerable<T>. https://github.com/Azure/service-fabric-issues/issues/735
        /// </remarks>
        Task<List<User>> GetUsersAsync(CancellationToken cancellationToken);
        Task<string> AddUserAsync(User user, CancellationToken cancellationToken);
        Task<bool> UpdateUsersAsync(List<User> users, CancellationToken cancellationToken);
        Task<bool> UpdateUserAsync(User user, CancellationToken cancellationToken);
        Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken);
    }
}