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
            if (trade.Ask.Price > trade.Bid.Price)
            {
                throw new InvalidTradeRequestException("The ask price cannot be higher than the bid price");
            }
            if (trade.Ask.Amount < trade.Bid.Amount)
            {
                throw new InvalidTradeRequestException("The ask amount cannot be lower than the bid amount");
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
            if (!seller.CurrencyAmounts.ToDictionary(x => x.Key, x => x.Value).ContainsKey(trade.Bid.Pair.GetBuyerWantCurrency()))
            {
                throw new BadSellerException($"Matched seller doesn't own any of the currency {trade.Bid.Pair.GetBuyerWantCurrency()} the buyer wants");
            }
            if (seller.CurrencyAmounts.ToDictionary(x => x.Key, x => x.Value)[trade.Bid.Pair.GetBuyerWantCurrency()] < trade.Settlement.Amount)
            {
                throw new BadSellerException($"Matched seller '{seller.Id}' doesn't have suffient quantity to satisfy the trade");
            }
            if (buyer.CurrencyAmounts.ToDictionary(x => x.Key, x => x.Value)[trade.Bid.Pair.GetSellerWantCurrency()] < (trade.Bid.Amount * trade.Ask.Price))
            {
                throw new BadBuyerException($"Matched buyer '{buyer.Id}' doesn't have suffient balance to satisfy the trade");
            }
            if (buyer.LatestTrades.Any(t => t.Split("_").LastOrDefault() == trade.Bid.Id))
            {
                throw new BadBuyerException("$The bid order has already been processed");
            }
            if (seller.LatestTrades.Any(t => t.Split("_").FirstOrDefault() == trade.Ask.Id))
            {
                throw new BadSellerException("$The ask order has already been processed");
            }
        }
    }
}
