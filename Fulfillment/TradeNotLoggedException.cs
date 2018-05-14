using System;
using System.Runtime.Serialization;

namespace Fulfillment
{
    [Serializable]
    public class TradeNotLoggedException : Exception
    {
        public TradeNotLoggedException()
        {
        }

        public TradeNotLoggedException(string message) : base(message)
        {
        }

        public TradeNotLoggedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected TradeNotLoggedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}