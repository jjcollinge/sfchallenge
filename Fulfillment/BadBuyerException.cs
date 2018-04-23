using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class BadBuyerException : Exception
    {
        public BadBuyerException()
        { }

        public BadBuyerException(string message) :
            base(message)
        { }

        public BadBuyerException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
