using Common;
using Microsoft.ServiceFabric.Data.Collections;
using ServiceFabric.Mocks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Fulfillment.Tests
{
    public class TestFulfillment_AddUserShould
    {
        [Fact]
        public async Task AddValidUserToDictionary()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new Fulfillment(context, stateManager);

            var qty = (uint)100;
            var balance = (uint)100;
            var username = "user1";
            var request = new UserRequestModel
            {
                Quantity = qty,
                Balance = balance,
                Username = username,
            };

            var userId = await service.AddUserAsync(request);
            var expected = new User(userId, username, qty, balance, null);

            var users = await stateManager.TryGetAsync<IReliableDictionary<string, User>>(Fulfillment.UserStoreName);
            var actual = (await users.Value.TryGetValueAsync(new MockTransaction(stateManager, 1), userId)).Value;
            Assert.Equal(expected, actual);
        }

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
