using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OrderBook
{
    public class InvalidOrderException : Exception
    {
        public InvalidOrderException()
        { }

        public InvalidOrderException(string message) :
            base(message)
        { }

        public InvalidOrderException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
