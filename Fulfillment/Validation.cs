using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class Validation
    {
        public static void ThrowIfNotValidUserRequest(UserRequestModel user)
        {
            if (string.IsNullOrWhiteSpace(user.Username))
            {
                throw new InvalidTradeRequestException("Username cannot be null, empty or contain whitespace");
            }
        }

        public static void ThrowIfNotValidTradeRequest(Trade trade)
        {
            if (trade.Ask == null || trade.Bid == null)
            {
                throw new InvalidTradeRequestException("Bid or ask cannot be null");
            }
            if (trade.Ask.Value > trade.Bid.Value)
            {
                throw new InvalidTradeRequestException("The ask value cannot be higher than the bid value");
            }
            if (trade.Ask.Quantity < trade.Bid.Quantity)
            {
                throw new InvalidTradeRequestException("The ask quantity cannot be lower than the bid quantity");
            }
        }

        public static void ThrowIfNotValidTrade(Trade trade, User seller, User buyer)
        {
            if (seller == null)
            {
                throw new BadSellerException($"Matched seller doesn't exist");
            }
            if (buyer == null)
            {
                throw new BadBuyerException($"Matched seller doesn't exist");
            }
            if (seller.Quantity < trade.Bid.Quantity)
            {
                throw new BadSellerException($"Matched seller '{seller.Id}' doesn't have suffient quantity to satisfy the trade");
            }
            if (buyer.Balance < trade.Bid.Value)
            {
                throw new BadBuyerException($"Matched buyer '{buyer.Id}' doesn't have suffient balance to satisfy the trade");
            }
            if (buyer.TradeIds.Any(t => t.Split("_").LastOrDefault() == trade.Bid.Id))
            {
                throw new BadBuyerException("$The bid order has already been processed");
            }
            if (seller.TradeIds.Any(t => t.Split("_").FirstOrDefault() == trade.Ask.Id))
            {
                throw new BadSellerException("$The ask order has already been processed");
            }
        }
    }
}
