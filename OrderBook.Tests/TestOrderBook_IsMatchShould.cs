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
            var ask = new Order(CurrencyPair.GBPUSD, 100, 200);
            var bid = new Order(CurrencyPair.GBPUSD, 50, 220);
        }
    }
}
