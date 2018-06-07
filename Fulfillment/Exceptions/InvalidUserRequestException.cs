using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fulfillment
{
    public class InvalidUserRequestException : Exception
    {
        public InvalidUserRequestException()
        {}

        public InvalidUserRequestException(string message):
            base(message)
        {}

        public InvalidUserRequestException(string message, Exception inner):
            base(message, inner)
        {}
    }
}
