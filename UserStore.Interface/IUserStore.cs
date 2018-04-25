using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.FabricTransport;
using System.Threading.Tasks;
using Common;
using System.Collections.Generic;

[assembly: FabricTransportServiceRemotingProvider(RemotingListener = RemotingListener.V2Listener, RemotingClient = RemotingClient.V2Client)]
namespace UserStore.Interface
{
    public interface IUserStore : IService
    {
        Task<User> GetUserAsync(string userId);
        Task<IEnumerable<User>> GetUsersAsync();
        Task<string> AddUserAsync(User user);
        Task<bool> UpdateUserAsync(User user);
        Task<bool> DeleteUserAsync(string userId);
    }
}