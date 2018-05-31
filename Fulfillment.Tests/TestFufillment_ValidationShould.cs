using Common;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Fulfillment.Tests
{
    public class TestFufillment_ValidationShould
    {
        [Fact]
        public void NotThrowIfValidOrder()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";

            var sellerCurrencyAmounts = new Dictionary<string, double>();
            sellerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetBuyerWantCurrency(), 100);
            var seller = new User(sellerId, "seller", sellerCurrencyAmounts, null);

            var buyerCurrencyAmounts = new Dictionary<string, double>();
            buyerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetSellerWantCurrency(), 100);
            var buyer = new User(buyerId, "buyer", buyerCurrencyAmounts, null);

            var tradeRequest = new TradeRequestModel();
            tradeRequest.Ask = new Order(askId, sellerId, CurrencyPair.GBPUSD, 10, 10, DateTime.UtcNow);
            tradeRequest.Bid = new Order(bidId, buyerId, CurrencyPair.GBPUSD, 10, 10, DateTime.UtcNow);
            tradeRequest.Settlement = new Order(CurrencyPair.GBPUSD, 10, 10);

            Validation.ThrowIfNotValidTrade(tradeRequest, seller, buyer);
        }

        [Fact]
        public void ThrowIfDuplicateOrder()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";
            
            var tradeRequest = new TradeRequestModel();
            tradeRequest.Ask = new Order(askId, sellerId, CurrencyPair.GBPUSD, 10, 10, DateTime.UtcNow);
            tradeRequest.Bid = new Order(bidId, buyerId, CurrencyPair.GBPUSD, 10, 10, DateTime.UtcNow);
            tradeRequest.Settlement = new Order(CurrencyPair.GBPUSD, 10, 10);

            Trade trade = tradeRequest;

            var sellerCurrencyAmounts = new Dictionary<string, double>();
            sellerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetBuyerWantCurrency(), 100);
            var seller = new User(sellerId, "seller", sellerCurrencyAmounts, new List<string>() { trade.Id });

            var buyerCurrencyAmounts = new Dictionary<string, double>();
            buyerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetSellerWantCurrency(), 100);
            var buyer = new User(buyerId, "buyer", buyerCurrencyAmounts, new List<string>() { trade.Id });

            Assert.Throws<DuplicateBidException>(() => Validation.ThrowIfNotValidTrade(tradeRequest, seller, buyer));
        }

        [Fact]
        public void ThrowIfBuyerNull()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";

            var sellerCurrencyAmounts = new Dictionary<string, double>();
            sellerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetBuyerWantCurrency(), 100);
            var seller = new User(sellerId, "seller", sellerCurrencyAmounts, null);
            User buyer = null;

            var tradeRequest = new TradeRequestModel();
            tradeRequest.Ask = new Order(askId, sellerId, CurrencyPair.GBPUSD, 10, 10, DateTime.UtcNow);
            tradeRequest.Bid = new Order(bidId, buyerId, CurrencyPair.GBPUSD, 10, 10, DateTime.UtcNow);
            tradeRequest.Settlement = new Order(CurrencyPair.GBPUSD, 10, 10);

            Assert.Throws<BadBuyerException>(() => Validation.ThrowIfNotValidTrade(tradeRequest, seller, buyer));
        }

        [Fact]
        public void ThrowIfSellerNull()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";

            User seller = null;
            var buyerCurrencyAmounts = new Dictionary<string, double>();
            buyerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetSellerWantCurrency(), 100);
            var buyer = new User(buyerId, "buyer", buyerCurrencyAmounts, null);

            var tradeRequest = new TradeRequestModel();
            tradeRequest.Ask = new Order(askId, sellerId, CurrencyPair.GBPUSD, 10, 10, DateTime.UtcNow);
            tradeRequest.Bid = new Order(bidId, buyerId, CurrencyPair.GBPUSD, 10, 10, DateTime.UtcNow);
            tradeRequest.Settlement = new Order(CurrencyPair.GBPUSD, 10, 10);

            Assert.Throws<BadSellerException>(() => Validation.ThrowIfNotValidTrade(tradeRequest, seller, buyer));
        }

        [Fact]
        public void ThrowIfSellerAmountLowerThanBidAmount()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";

            var sellerCurrencyAmounts = new Dictionary<string, double>();
            sellerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetBuyerWantCurrency(), 5);
            var seller = new User(sellerId, "seller", sellerCurrencyAmounts, null);

            var buyerCurrencyAmounts = new Dictionary<string, double>();
            buyerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetSellerWantCurrency(), 100);
            var buyer = new User(buyerId, "buyer", buyerCurrencyAmounts, null);

            var tradeRequest = new TradeRequestModel();
            tradeRequest.Ask = new Order(askId, sellerId, CurrencyPair.GBPUSD, 100, 10, DateTime.UtcNow);
            tradeRequest.Bid = new Order(bidId, buyerId, CurrencyPair.GBPUSD, 100, 10, DateTime.UtcNow);
            tradeRequest.Settlement = new Order(CurrencyPair.GBPUSD, 10, 10);

            Assert.Throws<BadSellerException>(() => Validation.ThrowIfNotValidTrade(tradeRequest, seller, buyer));
        }

        [Fact]
        public void ThrowIfBuyerAmountLowerThanBidAmount()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";

            var sellerCurrencyAmounts = new Dictionary<string, double>();
            sellerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetBuyerWantCurrency(), 100);
            var seller = new User(sellerId, "seller", sellerCurrencyAmounts, null);

            var buyerCurrencyAmounts = new Dictionary<string, double>();
            buyerCurrencyAmounts.Add(CurrencyPair.GBPUSD.GetSellerWantCurrency(), 5);
            var buyer = new User(buyerId, "buyer", buyerCurrencyAmounts, null);

            var tradeRequest = new TradeRequestModel();
            tradeRequest.Ask = new Order(askId, sellerId, CurrencyPair.GBPUSD, 100, 10, DateTime.UtcNow);
            tradeRequest.Bid = new Order(bidId, buyerId, CurrencyPair.GBPUSD, 100, 10, DateTime.UtcNow);
            tradeRequest.Settlement = new Order(CurrencyPair.GBPUSD, 100, 10);

            Assert.Throws<BadBuyerException>(() => Validation.ThrowIfNotValidTrade(tradeRequest, seller, buyer));
        }
    }
}
