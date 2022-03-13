using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Instances.Exceptions;

namespace Instances
{
    public class ProcessInstance : IProcessInstance
    {
        private readonly bool _ignoreEmptyLines;
        private readonly int _dataBufferCapacity;
        private readonly Process _process;
        private readonly TaskCompletionSource<bool> _mainTask = new TaskCompletionSource<bool>();
        private readonly TaskCompletionSource<bool> _stdoutTask = new TaskCompletionSource<bool>();
        private readonly TaskCompletionSource<bool> _stderrTask = new TaskCompletionSource<bool>();
        private readonly Queue<string> _outputData = new Queue<string>();
        private readonly Queue<string> _errorData = new Queue<string>();

        internal ProcessInstance(Process process, bool ignoreEmptyLines, int dataBufferCapacity)
        {
            process.OutputDataReceived += ReceiveOutput;
            process.ErrorDataReceived += ReceiveError;
            process.Exited += ReceiveExit;
            
            _process = process;
            _ignoreEmptyLines = ignoreEmptyLines;
            _dataBufferCapacity = dataBufferCapacity;
        }
        
        public IReadOnlyCollection<string> OutputData => _outputData.ToList().AsReadOnly();
        public IReadOnlyCollection<string> ErrorData => _errorData.ToList().AsReadOnly();

        public event EventHandler<IProcessResult>? Exited;
        public event EventHandler<string>? OutputDataReceived;
        public event EventHandler<string>? ErrorDataReceived;
        
        public async Task SendInputAsync(string input)
        {
            ThrowIfProcessExited();

            await _process.StandardInput.WriteAsync(input).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        public void SendInput(string input)
        {
            ThrowIfProcessExited();
            _process.StandardInput.Write(input);
            _process.StandardInput.Flush();
        }

        public IProcessResult Kill()
        {
            ThrowIfProcessExited();

            try
            {
                _process.Kill();
                return GetResult();
            }
            catch (InvalidOperationException e)
            {
                throw new InstanceProcessAlreadyExitedException(e);
            }
        }

        public async Task<IProcessResult> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfProcessExited();

            if (cancellationToken != default) cancellationToken.Register(() => _process.Kill());

            await _mainTask.Task.ConfigureAwait(false);
            return GetResult();
        }

        public IProcessResult WaitForExit()
        {
            ThrowIfProcessExited();

            try
            {
                _process.WaitForExit();
                return GetResult();
            }
            catch (SystemException e)
            {
                throw new InstanceProcessAlreadyExitedException(e);
            }
        }
        
        public void Dispose()
        {
            _process.Dispose();
        }

        private void ReceiveExit(object sender, EventArgs e)
        {
            Task.WhenAll(_stdoutTask!.Task, _stderrTask!.Task).ContinueWith(task =>
            {
                Exited?.Invoke(sender, GetResult());
                return _mainTask.TrySetResult(true);
            });
        }
        private void ReceiveOutput(object _, DataReceivedEventArgs e) => AddData(_outputData, e.Data, OutputDataReceived, _stdoutTask);

        private void ReceiveError(object _, DataReceivedEventArgs e) => AddData(_errorData, e.Data, ErrorDataReceived, _stderrTask);

        private void AddData(Queue<string> dataList, string? data, EventHandler<string>? eventHandler, TaskCompletionSource<bool> taskCompletionSource)
        {
            if (data == null)
            {
                taskCompletionSource.TrySetResult(true);
            }
            else
            {
                if (_ignoreEmptyLines && data == string.Empty) return;
                dataList.Enqueue(data);
                for (var i = 0; i < dataList.Count - _dataBufferCapacity; i++) 
                    dataList.Dequeue();
                eventHandler?.Invoke(this, data);
            }
        }

        private IProcessResult GetResult()
        {
            var exitCode = _process.HasExited ? _process.ExitCode : -100;
            return new ProcessResult(exitCode, _outputData.ToArray(), _errorData.ToArray());
        }

        private void ThrowIfProcessExited()
        {
            if (_process.HasExited) throw new InstanceProcessAlreadyExitedException();
        }
    }
}