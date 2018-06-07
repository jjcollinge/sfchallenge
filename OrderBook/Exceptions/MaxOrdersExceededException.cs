using System;
using System.Runtime.Serialization;

namespace OrderBook
{
    [Serializable]
    public class MaxOrdersExceededException : Exception
    {
        private long currentBids;

        public MaxOrdersExceededException()
        {
        }

        public MaxOrdersExceededException(long currentBids)
        {
            this.currentBids = currentBids;
        }

        public MaxOrdersExceededException(string message) : base(message)
        {
        }

        public MaxOrdersExceededException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MaxOrdersExceededException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}