using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class BadSellerException : Exception
    {
        public BadSellerException()
        { }

        public BadSellerException(string message) :
            base(message)
        { }

        public BadSellerException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
