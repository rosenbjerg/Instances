using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Instances
{
    public interface IProcessInstance : IDisposable
    {
        Task SendInputAsync(string input);
        void SendInput(string input);

        Task<IProcessResult> WaitForExitAsync(CancellationToken cancellationToken = default);
        IProcessResult WaitForExit();
        
        IProcessResult Kill();
        
        IReadOnlyCollection<string> OutputData { get; }
        IReadOnlyCollection<string> ErrorData { get; }

        event EventHandler<IProcessResult>? Exited;
        event EventHandler<string>? OutputDataReceived;
        event EventHandler<string>? ErrorDataReceived;
    }
}