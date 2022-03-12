using System;
using System.ComponentModel;
using System.Diagnostics;
using Instances.Exceptions;

namespace Instances
{
    public class ProcessArguments
    {
        private readonly ProcessStartInfo _processStartInfo;

        public ProcessArguments(string path, string arguments) : this(new ProcessStartInfo { FileName = path, Arguments = arguments }) { }

        public ProcessArguments(ProcessStartInfo processStartInfo)
        {
            _processStartInfo = processStartInfo;
        }

        public bool IgnoreEmptyLines { get; set; }
        public int DataBufferCapacity { get; set; } = int.MaxValue;

        public event EventHandler<IProcessResult>? Exited;
        public event EventHandler<string>? OutputDataReceived;
        public event EventHandler<string>? ErrorDataReceived;

        public IProcessInstance Start()
        {
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
            
            var instance = new ProcessInstance(process, IgnoreEmptyLines, DataBufferCapacity);

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
            catch (Win32Exception e) when(e.Message == "The system cannot find the file specified." || e.Message == "No such file or directory")
            {
                throw new InstanceFileNotFoundException(_processStartInfo.FileName, e);
            }
        }
    }
}