using Common;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace OrderBook
{
    [DataContract]
    public class OrderQueue
    {
        [DataMember]
        private ImmutableList<Order> queue;

        public OrderQueue()
        {
            this.queue = ImmutableList<Order>.Empty;
        }

        public IEnumerable<Order> Enqueue(Order order)
        {
            this.queue = this.queue.Add(order);
            return this.queue;
        }

        public IEnumerable<Order> Dequeue()
        {
            if (!this.queue.IsEmpty)
            {
                this.queue = this.queue.RemoveAt(0);
            }
            return this.queue;
        }

        public Order Peek()
        {
            return this.queue.FirstOrDefault();
        }

        public int Count()
        {
            return this.queue.Count();
        }

    }
}
