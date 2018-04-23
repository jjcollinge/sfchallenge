using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Common
{
    public class TransferRequestModel
    {
        public Order Ask { get; set; }

        public Order Bid { get; set; }

        public static implicit operator Transfer(TransferRequestModel request)
        {
            if (request == null)
            {
                return null;
            }
            var id = Guid.NewGuid().ToString();
            return new Transfer(id, request.Ask, request.Bid);
        }
    }
}
