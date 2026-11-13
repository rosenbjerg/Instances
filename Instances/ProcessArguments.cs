using System;
using System.ComponentModel;
using System.Diagnostics;
using Instances.Exceptions;

namespace Instances
{
    public class ProcessArguments
    {
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
            catch (Win32Exception e) when (e.Message.Contains("The system cannot find the file specified") || e.Message.Contains("No such file or directory") )
            {
                throw new InstanceFileNotFoundException(_processStartInfo.FileName, e);
            }
        }
    }
}