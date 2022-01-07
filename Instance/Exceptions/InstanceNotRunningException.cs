namespace Instances
{
    public class InstanceNotRunningException : InstanceException
    {
        public InstanceNotRunningException() : base("Instance is not running!")
        {
        }
    }
}