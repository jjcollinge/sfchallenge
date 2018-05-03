using System;
using System.Runtime.Serialization;

namespace Fulfillment
{
    [Serializable]
    internal class MaxPendingTransfersExceededException : Exception
    {
        private long pendingTransfers;

        public MaxPendingTransfersExceededException()
        {
        }

        public MaxPendingTransfersExceededException(long pendingTransfers)
        {
            this.pendingTransfers = pendingTransfers;
        }

        public MaxPendingTransfersExceededException(string message) : base(message)
        {
        }

        public MaxPendingTransfersExceededException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MaxPendingTransfersExceededException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}