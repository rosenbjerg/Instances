using System;

namespace Instances
{
    public static class ProcessInstanceExtensions
    {
        public static void CloseInput(this IProcessInstance instance)
        {
            if (!(instance is ProcessInstance processInstance))
                throw new NotSupportedException($"{instance.GetType().Name} cannot close its standard input.");
            processInstance.CloseInput();
        }
    }
}
