using Common;
using ServiceFabric.Mocks;
using System.Threading.Tasks;
using Xunit;

namespace Fulfillment.Tests
{
    public class TestFulfillment_AddUserShould
    {
        [Fact]
        public async Task ThrowIfUsernameIsNullOrWhitespace()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);

            var qty = (uint)100;
            var balance = (uint)100;
            var username = "  ";
            var request = new UserRequestModel
            {
                Quantity = qty,
                Balance = balance,
                Username = username,
            };

            await Assert.ThrowsAsync<InvalidTransferRequestException>(() => service.AddUserAsync(request));
        }
    }
}