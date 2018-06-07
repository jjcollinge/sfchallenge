using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OrderBook
{
    public class InvalidAskException : Exception
    {
        public InvalidAskException()
        { }

        public InvalidAskException(string message) :
            base(message)
        { }

        public InvalidAskException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
