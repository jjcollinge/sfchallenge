using Common;
using Microsoft.ServiceFabric.Data.Collections;
using ServiceFabric.Mocks;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace UserStore.Tests
{
    public class TestUserStore
    {
        [Fact]
        public async Task StoreUser()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var service = new UserStore(context, stateManager);

            var sutUser = new User("42", "Anders", 42, 128, new List<string>()
                {
                    "t1"
                });

            //create state
            await service.AddUserAsync(sutUser);

            //get state
            var dictionary = await stateManager.TryGetAsync<IReliableDictionary<string, User>>(UserStore.StateManagerKey);

            var actual = (await dictionary.Value.TryGetValueAsync(new MockTransaction(stateManager, 1), sutUser.Id)).Value;
            Assert.Equal(sutUser, actual);
        }
    }
}