using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class InvalidTransferRequestException: Exception
    {
        public InvalidTransferRequestException()
        { }

        public InvalidTransferRequestException(string message) :
            base(message)
        { }

        public InvalidTransferRequestException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
