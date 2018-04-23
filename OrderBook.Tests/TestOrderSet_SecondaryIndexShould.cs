using Common;
using ServiceFabric.Mocks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace OrderBook.Tests
{
    public class TestSecondaryIndex_OrderingShould
    {
        [Fact]
        public void StoreHighestValueAtLastIndex()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var orderSet = new OrderSet(stateManager, "test");

            var order1 = new Order(10, 10, string.Empty);
            var order2 = new Order(12, 5, string.Empty);
            var order3 = new Order(5, 10, string.Empty);

            orderSet.SecondaryIndex.Add(order1);
            orderSet.SecondaryIndex.Add(order2);
            orderSet.SecondaryIndex.Add(order3);

            var max = orderSet.GetMaxOrder();
            Assert.Equal(order2, max);
        }

        [Fact]
        public void StoreOldesetOrderAtLastIndex()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var orderSet = new OrderSet(stateManager, "test");

            var order1 = new Order(10, 10, string.Empty);
            var order2 = new Order(10, 10, string.Empty);
            var order3 = new Order(10, 10, string.Empty);

            orderSet.SecondaryIndex.Add(order1);
            orderSet.SecondaryIndex.Add(order2);
            orderSet.SecondaryIndex.Add(order3);

            var max = orderSet.GetMaxOrder();
            Assert.Equal(order1, max);
        }

        [Fact]
        public void StoreLowestValueAtFirstIndex()
        {
            var context = MockStatefulServiceContextFactory.Default;
            var stateManager = new MockReliableStateManager();
            var orderSet = new OrderSet(stateManager, "test");

            var order1 = new Order(10, 10, string.Empty);
            var order2 = new Order(12, 5, string.Empty);
            var order3 = new Order(5, 10, string.Empty);

            orderSet.SecondaryIndex.Add(order1);
            orderSet.SecondaryIndex.Add(order2);
            orderSet.SecondaryIndex.Add(order3);

            var min = orderSet.GetMinOrder();
            Assert.Equal(order3, min);
        }
    }
}
