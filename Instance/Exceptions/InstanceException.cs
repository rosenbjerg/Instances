using System;

namespace Instances
{
    public class InstanceException : Exception
    {
        public InstanceException(string msg) : base(msg)
        {
        }
        public InstanceException(string msg, Exception innerException) : base(msg, innerException)
        {
        }
    }
}