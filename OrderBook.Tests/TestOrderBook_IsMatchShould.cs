using Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderBook.Tests
{
    public class TestOrderBook_IsMatchShould
    {
        public void ReturnTrueWhenValueOverlapsAndQuantityIsSufficient()
        {
            var ask = new Order(100, 200, string.Empty);
            var bid = new Order(50, 220, string.Empty);
        }
    }
}
