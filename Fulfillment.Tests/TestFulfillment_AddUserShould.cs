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
            var context = Helpers.GetMockContext();
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);

            var username = "  ";
            var request = new UserRequestModel
            {
                CurrencyAmounts = null,
                Username = username,
            };

            await Assert.ThrowsAsync<InvalidUserRequestException>(() => service.AddUserAsync(request));
        }
    }
}