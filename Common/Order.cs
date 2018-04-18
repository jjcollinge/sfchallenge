using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Common
{
    public class Order
    {
        public Order()
        {
            this.Id = Guid.NewGuid().ToString();
        }

        public Order(string id)
        {
            this.Id = id;
        }

        public string Id { get; }
        public string UserId { get; set; }
        public uint Value { get; set; }
        public uint Quantity { get; set; }
    }
}
