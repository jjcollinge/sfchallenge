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
                throw new InvalidTransferRequestException("Username cannot be null, empty or contain whitespace");
            }
        }

        public static void ThrowIfNotValidTransferRequest(Transfer transfer)
        {
            if (transfer.Ask == null || transfer.Bid == null)
            {
                throw new InvalidTransferRequestException("Bid or ask cannot be null");
            }
            if (transfer.Ask.Value > transfer.Bid.Value)
            {
                throw new InvalidTransferRequestException("The ask value cannot be higher than the bid value");
            }
            if (transfer.Ask.Quantity < transfer.Bid.Quantity)
            {
                throw new InvalidTransferRequestException("The ask quantity cannot be lower than the bid quantity");
            }
        }

        public static void ThrowIfNotValidTransfer(Transfer transfer, User seller, User buyer)
        {
            if (seller == null)
            {
                throw new BadSellerException($"Matched seller '{seller}' doesn't exist");
            }
            if (buyer == null)
            {
                throw new BadBuyerException($"Matched seller '{buyer}' doesn't exist");
            }
            if (seller.Quantity < transfer.Bid.Quantity)
            {
                throw new BadSellerException($"Matched seller '{seller.Id}' doesn't have suffient quantity to satisfy the transfer");
            }
            if (buyer.Balance < transfer.Bid.Value)
            {
                throw new BadBuyerException($"Matched buyer '{buyer.Id}' doesn't have suffient balance to satisfy the transfer");
            }
            if (buyer.TransferIds.Any(t => t.Split("_").LastOrDefault() == transfer.Bid.Id))
            {
                throw new BadBuyerException("$The bid order has already been processed");
            }
            if (seller.TransferIds.Any(t => t.Split("_").FirstOrDefault() == transfer.Ask.Id))
            {
                throw new BadSellerException("$The ask order has already been processed");
            }
        }
    }
}
