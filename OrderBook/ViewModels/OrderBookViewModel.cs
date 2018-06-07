using Common;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace OrderBook
{
    public class OrderBookViewModel
    {
        public OrderBookViewModel()
        {
            Timestamp = DateTime.Now;
        }

        public string CurrencyPair { get; set; }
        public List<KeyValuePair<string, Order>> Asks { get; set; }
        public List<KeyValuePair<string, Order>> Bids { get; set; }
        public int AsksCount { get; set; }
        public int BidsCount { get; set; }
        public DateTime Timestamp { get; }
    }
}
