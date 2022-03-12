using System;

namespace Instances.Exceptions
{
    public class InstanceFileNotFoundException : InstanceException
    {
        public InstanceFileNotFoundException(string fileName, Exception innerException) : base($"File not found: {fileName}", innerException)
        {
        }
    }
}