using System;
using System.Runtime.Serialization;

namespace Logger
{
    [Serializable]
    public class LoggerDisconnectedException : Exception
    {
        public LoggerDisconnectedException()
        {
        }

        public LoggerDisconnectedException(string message) : base(message)
        {
        }

        public LoggerDisconnectedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected LoggerDisconnectedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}