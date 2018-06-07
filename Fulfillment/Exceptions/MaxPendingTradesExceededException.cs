using System;
using System.Runtime.Serialization;

namespace Fulfillment
{
    [Serializable]
    public class MaxPendingTradesExceededException : Exception
    {
        private long pendingTransfers;

        public MaxPendingTradesExceededException()
        {
        }

        public MaxPendingTradesExceededException(long pendingTransfers)
        {
            this.pendingTransfers = pendingTransfers;
        }

        public MaxPendingTradesExceededException(string message) : base(message)
        {
        }

        public MaxPendingTradesExceededException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MaxPendingTradesExceededException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}