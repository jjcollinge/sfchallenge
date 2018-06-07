using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class DuplicateAskException : Exception
    {
        public DuplicateAskException()
        { }

        public DuplicateAskException(string message) :
            base(message)
        { }

        public DuplicateAskException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
