using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OrderBook
{
    public class InvalidBidException : Exception
    {
        public InvalidBidException()
        { }

        public InvalidBidException(string message) :
            base(message)
        { }

        public InvalidBidException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
