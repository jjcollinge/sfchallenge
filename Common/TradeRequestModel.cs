using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Common
{
    public class TradeRequestModel
    {
        public Order Ask { get; set; }

        public Order Bid { get; set; }

        public static implicit operator Trade(TradeRequestModel request)
        {
            if (request == null)
            {
                return null;
            }
            var id = request.Ask.Id + "_" + request.Bid.Id;
            return new Trade(id, request.Ask, request.Bid);
        }
    }
}
