using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class DuplicateBidException : Exception
    {
        public DuplicateBidException()
        { }

        public DuplicateBidException(string message) :
            base(message)
        { }

        public DuplicateBidException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
