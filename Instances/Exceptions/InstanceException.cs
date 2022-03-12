using System;

namespace Instances.Exceptions
{
    public class InstanceException : Exception
    {
        public InstanceException(string msg, Exception innerException) : base(msg, innerException)
        {
        }
    }
}