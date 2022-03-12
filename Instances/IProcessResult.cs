using System.Collections.Generic;

namespace Instances
{
    public interface IProcessResult 
    {
        int ExitCode { get; }
        
        IReadOnlyList<string> OutputData { get; }
        
        IReadOnlyList<string> ErrorData { get; }
    }
}