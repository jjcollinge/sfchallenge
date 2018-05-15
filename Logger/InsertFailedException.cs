using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Logger
{
    public class InsertFailedException : Exception
    {
        public InsertFailedException()
        { }

        public InsertFailedException(string message) :
            base(message)
        { }

        public InsertFailedException(string message, Exception inner) :
            base(message, inner)
        { }
    }
}
