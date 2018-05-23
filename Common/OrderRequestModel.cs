using System;
using System.Collections.Generic;
using System.Text;

namespace Common
{
    public class OrderRequestModel
    {
        public string Pair { get; set; }
        public uint Amount { get; set; }
        public double Price { get; set; }
        public string UserId { get; set; }

        public static implicit operator Order(OrderRequestModel request)
        {
            var currencyPair = CurrencyPairExtensions.FromFriendlyString(request.Pair);
            return new Order(request.UserId, currencyPair, request.Amount, request.Price);
        }
    }
}