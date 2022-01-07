using System;

namespace Instances
{
    public class InstanceFileNotFoundException : InstanceException
    {
        public InstanceFileNotFoundException(string fileName, Exception innerException) : base($"File not found: {fileName}", innerException)
        {
        }
    }
}