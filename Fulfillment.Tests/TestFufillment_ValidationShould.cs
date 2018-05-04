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

            var seller = new User(sellerId, "seller", 100, 10, null);
            var buyer = new User(buyerId, "buyer", 10, 100, null);

            var transferRequest = new TransferRequestModel();
            transferRequest.Ask = new Order(askId, 10, 10, sellerId, DateTime.UtcNow);
            transferRequest.Bid = new Order(bidId, 10, 10, buyerId, DateTime.UtcNow);
           
            Validation.ThrowIfNotValidTransfer(transferRequest, seller, buyer);
        }

        [Fact]
        public void ThrowIfDuplicateOrder()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";
            
            var transferRequest = new TransferRequestModel();
            transferRequest.Ask = new Order(askId, 10, 10, sellerId, DateTime.UtcNow);
            transferRequest.Bid = new Order(bidId, 10, 10, buyerId, DateTime.UtcNow);

            Transfer transfer = transferRequest;

            var seller = new User(sellerId, "seller", 100, 10, new List<string>() { transfer.Id });
            var buyer = new User(buyerId, "buyer", 10, 100, new List<string>() { transfer.Id });

            Assert.Throws<BadBuyerException>(() => Validation.ThrowIfNotValidTransfer(transferRequest, seller, buyer));
        }

        [Fact]
        public void NotThrowIfBuyerNull()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";

            var seller = new User(sellerId, "seller", 100, 10, null);
            User buyer = null;

            var transferRequest = new TransferRequestModel();
            transferRequest.Ask = new Order(askId, 10, 10, sellerId, DateTime.UtcNow);
            transferRequest.Bid = new Order(bidId, 10, 10, buyerId, DateTime.UtcNow);

            Assert.Throws<BadBuyerException>(() => Validation.ThrowIfNotValidTransfer(transferRequest, seller, buyer));
        }

        [Fact]
        public void NotThrowIfSellerNull()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";

            User seller = null;
            var buyer = new User(buyerId, "buyer", 10, 100, null);

            var transferRequest = new TransferRequestModel();
            transferRequest.Ask = new Order(askId, 10, 10, sellerId, DateTime.UtcNow);
            transferRequest.Bid = new Order(bidId, 10, 10, buyerId, DateTime.UtcNow);

            Assert.Throws<BadSellerException>(() => Validation.ThrowIfNotValidTransfer(transferRequest, seller, buyer));
        }

        [Fact]
        public void ThrowIfSellerQuantityLowerThanBidQuantity()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";

            var seller = new User(sellerId, "seller", 5, 10, null);
            var buyer = new User(buyerId, "buyer", 10, 100, null);

            var transferRequest = new TransferRequestModel();
            transferRequest.Ask = new Order(askId, 10, 10, sellerId, DateTime.UtcNow);
            transferRequest.Bid = new Order(bidId, 10, 10, buyerId, DateTime.UtcNow);

            Assert.Throws<BadSellerException>(() => Validation.ThrowIfNotValidTransfer(transferRequest, seller, buyer));
        }

        [Fact]
        public void ThrowIfBuyerBalanceLowerThanBidValue()
        {
            var askId = "ask1";
            var bidId = "bid1";

            var sellerId = "user1";
            var buyerId = "user2";

            var seller = new User(sellerId, "seller", 10, 10, null);
            var buyer = new User(buyerId, "buyer", 10, 5, null);

            var transferRequest = new TransferRequestModel();
            transferRequest.Ask = new Order(askId, 10, 10, sellerId, DateTime.UtcNow);
            transferRequest.Bid = new Order(bidId, 10, 10, buyerId, DateTime.UtcNow);

            Assert.Throws<BadBuyerException>(() => Validation.ThrowIfNotValidTransfer(transferRequest, seller, buyer));
        }
    }
}
