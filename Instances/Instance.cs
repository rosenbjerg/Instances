using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Instances
{
    public static class Instance
    {
        public static IProcessInstance Start(string path, string arguments = "", EventHandler<string>? outputHandler = default, EventHandler<string>? errorHandler = default)
        {
            return Start(new ProcessStartInfo { FileName = path, Arguments = arguments }, outputHandler, errorHandler);
        }

        public static IProcessInstance Start(ProcessStartInfo startInfo, EventHandler<string>? outputHandler, EventHandler<string>? errorHandler)
        {
            var arguments = new ProcessArguments(startInfo);
            if (outputHandler != default) arguments.OutputDataReceived += outputHandler;
            if (errorHandler != default) arguments.ErrorDataReceived += errorHandler;

            return arguments.Start();
        }
        
        public static IProcessResult Finish(string path, string arguments = "", EventHandler<string>? outputHandler = default, EventHandler<string>? errorHandler = default)
        {
            return Finish(new ProcessStartInfo {FileName = path, Arguments = arguments}, outputHandler, errorHandler);
        }

        public static IProcessResult Finish(ProcessStartInfo startInfo, EventHandler<string>? outputHandler = default, EventHandler<string>? errorHandler = default)
        {
            using var instance = Start(startInfo, outputHandler, errorHandler);
            return instance.WaitForExit();
        }

        public static Task<IProcessResult> FinishAsync(string path, string arguments = "", CancellationToken cancellationToken = default, EventHandler<string>? outputHandler = default, EventHandler<string>? errorHandler = default)
        {
            var processStartArguments = new ProcessStartInfo { FileName = path, Arguments = arguments };
            return FinishAsync(processStartArguments, cancellationToken, outputHandler, errorHandler);
        }

        public static async Task<IProcessResult> FinishAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default, EventHandler<string>? outputHandler = default, EventHandler<string>? errorHandler = default)
        {
            using var instance = Start(startInfo, outputHandler, errorHandler);
            return await instance.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}