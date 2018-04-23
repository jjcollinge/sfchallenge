using System;
using System.Collections.Generic;
using System.Text;

namespace Common
{
    public class OrderRequestModel
    {
        public uint Value { get; set; }
        public uint Quantity { get; set; }
        public string UserId { get; set; }

        public static implicit operator Order(OrderRequestModel request)
        {
            return new Order(request.Value, request.Quantity, request.UserId);
        }
    }
}