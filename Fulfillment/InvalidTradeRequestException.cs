using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class InvalidTradeRequestException: Exception
    {
        public InvalidTradeRequestException()
        { }

        public InvalidTradeRequestException(string message) :
            base(message)
        { }

        public InvalidTradeRequestException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
