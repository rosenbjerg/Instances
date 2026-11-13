using System;
using System.ComponentModel;
using System.Diagnostics;
using Instances.Exceptions;

namespace Instances
{
    public class ProcessArguments
    {
        private const int FileNotFoundErrorCode = 2;

        private readonly ProcessStartInfo _processStartInfo;
        private int _dataBufferCapacity = int.MaxValue;

        public ProcessArguments(string path, string arguments) : this(new ProcessStartInfo { FileName = path, Arguments = arguments }) { }

        public ProcessArguments(ProcessStartInfo processStartInfo)
        {
            _processStartInfo = processStartInfo;
        }

        public bool IgnoreEmptyLines { get; set; }
        public int DataBufferCapacity
        {
            get => _dataBufferCapacity;
            set => _dataBufferCapacity = value >= 0
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "DataBufferCapacity cannot be negative.");
        }
        public bool KillEntireProcessTree { get; set; }

        public event EventHandler<IProcessResult>? Exited;
        public event EventHandler<string>? OutputDataReceived;
        public event EventHandler<string>? ErrorDataReceived;

        public IProcessInstance Start()
        {
#if !NET5_0_OR_GREATER
            if (KillEntireProcessTree)
                throw new PlatformNotSupportedException("KillEntireProcessTree requires the .NET 8 build of Instances.");
#endif
            _processStartInfo.CreateNoWindow = true;
            _processStartInfo.UseShellExecute = false;
            _processStartInfo.RedirectStandardOutput = true;
            _processStartInfo.RedirectStandardInput = true;
            _processStartInfo.RedirectStandardError = true;
            var process = new Process
            {
                StartInfo = _processStartInfo,
                EnableRaisingEvents = true
            };
            
            var instance = new ProcessInstance(process, IgnoreEmptyLines, DataBufferCapacity, KillEntireProcessTree);

            instance.Exited += Exited;
            instance.OutputDataReceived += OutputDataReceived;
            instance.ErrorDataReceived += ErrorDataReceived;

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return instance;
            }
            catch (Exception e)
            {
                process.Dispose();
                if (e is Win32Exception { NativeErrorCode: FileNotFoundErrorCode })
                    throw new InstanceFileNotFoundException(_processStartInfo.FileName, e);
                throw;
            }
        }
    }
}