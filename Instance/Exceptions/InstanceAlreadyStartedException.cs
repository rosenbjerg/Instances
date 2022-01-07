namespace Instances
{
    public class InstanceAlreadyStartedException : InstanceException
    {
        public InstanceAlreadyStartedException() : base("Instance has already been started!")
        {
        }
    }
}