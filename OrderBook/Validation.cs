using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OrderBook
{
    public class Validation
    {
        /// <summary>
        /// Checks whether a given order meets
        /// the validity criteria. Throws
        /// InvalidOrderException if it doesn't.
        /// </summary>
        /// <param name="order"></param>
        public static void ThrowIfNotValidOrder(Order order)
        {
            if (string.IsNullOrWhiteSpace(order.Id))
            {
                throw new InvalidOrderException("Order Id cannot be null, empty or contain whitespace");
            }
            if (string.IsNullOrWhiteSpace(order.UserId))
            {
                throw new InvalidOrderException("Order cannot have a null or invalid user id");
            }
            if (order.Quantity == 0)
            {
                throw new InvalidOrderException("Order cannot have 0 quantity");
            }
            if (order.Value == 0)
            {
                throw new InvalidOrderException("Order cannot have 0 value");
            }
        }
    }
}
