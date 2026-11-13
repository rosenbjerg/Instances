using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Instances.Exceptions;

namespace Instances
{
    public class ProcessInstance : IProcessInstance
    {
        private static readonly TimeSpan KillGracePeriod = TimeSpan.FromSeconds(5);
        private const int MaxHandlerExceptions = 100;

        [ThreadStatic] private static ProcessInstance? _raisingExited;

        private readonly bool _ignoreEmptyLines;
        private readonly int _dataBufferCapacity;
        private readonly Process _process;
        private readonly TaskCompletionSource<IProcessResult> _completion = new TaskCompletionSource<IProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _stdoutTask = new TaskCompletionSource<bool>();
        private readonly TaskCompletionSource<bool> _stderrTask = new TaskCompletionSource<bool>();
        private readonly Queue<string> _outputData = new Queue<string>();
        private readonly Queue<string> _errorData = new Queue<string>();
        private readonly ConcurrentQueue<Exception> _handlerExceptions = new ConcurrentQueue<Exception>();
        private readonly object _resultLock = new object();
        private IProcessResult? _result;
        private int _completing;
        private volatile bool _disposed;
        private bool _inputClosed;

        internal ProcessInstance(Process process, bool ignoreEmptyLines, int dataBufferCapacity)
        {
            process.OutputDataReceived += ReceiveOutput;
            process.ErrorDataReceived += ReceiveError;
            process.Exited += ReceiveExit;

            _process = process;
            _ignoreEmptyLines = ignoreEmptyLines;
            _dataBufferCapacity = dataBufferCapacity;
        }

        public IReadOnlyCollection<string> OutputData => Array.AsReadOnly(Snapshot(_outputData));
        public IReadOnlyCollection<string> ErrorData => Array.AsReadOnly(Snapshot(_errorData));

        public event EventHandler<IProcessResult>? Exited;
        public event EventHandler<string>? OutputDataReceived;
        public event EventHandler<string>? ErrorDataReceived;

        public async Task SendInputAsync(string input)
        {
            ThrowIfInputUnavailable();
            await _process.StandardInput.WriteAsync(input).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }

        public void SendInput(string input)
        {
            ThrowIfInputUnavailable();
            _process.StandardInput.Write(input);
            _process.StandardInput.Flush();
        }

        public void CloseInput()
        {
            ThrowIfDisposed();
            if (_inputClosed) return;
            _inputClosed = true;
            _process.StandardInput.Close();
        }

        public IProcessResult Kill()
        {
            ThrowIfDisposedAsExited();
            var finished = FinishedResult;
            if (finished != null) return finished;

            KillProcess();
            WaitForKilledProcessToExit();
            return FinishedResult ?? new ProcessResult(_process.ExitCode, Snapshot(_outputData), Snapshot(_errorData));
        }

        public async Task<IProcessResult> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var result = FinishedResult ?? (cancellationToken.CanBeCanceled
                ? await WaitOrKillAsync(cancellationToken).ConfigureAwait(false)
                : await _completion.Task.ConfigureAwait(false));
            return ThrowIfHandlersFailed(result);
        }

        public IProcessResult WaitForExit()
        {
            ThrowIfDisposedAsExited();
            var finished = FinishedResult;
            if (finished != null) return ThrowIfHandlersFailed(finished);

            try
            {
                _process.WaitForExit();
            }
            catch (SystemException e)
            {
                throw new InstanceProcessAlreadyExitedException(e);
            }
            Task.WaitAll(_stdoutTask.Task, _stderrTask.Task);
            return ThrowIfHandlersFailed(GetOrCreateResult());
        }

        public void Dispose()
        {
            _disposed = true;
            _process.Dispose();
        }

        private IProcessResult? FinishedResult =>
            _completion.Task.Status == TaskStatus.RanToCompletion ? _completion.Task.Result
            : ReferenceEquals(_raisingExited, this) ? _result
            : null;

        private async Task<IProcessResult> WaitOrKillAsync(CancellationToken cancellationToken)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                await Task.WhenAny(_completion.Task, cancelled.Task).ConfigureAwait(false);
            }

            if (!_completion.Task.IsCompleted)
            {
                KillProcess();
                if (await Task.WhenAny(_completion.Task, Task.Delay(KillGracePeriod)).ConfigureAwait(false) != _completion.Task)
                {
                    WaitForKilledProcessToExit();
                    Complete();
                }
            }
            return await _completion.Task.ConfigureAwait(false);
        }

        private void KillProcess()
        {
            try
            {
                _process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception) when (_process.HasExited)
            {
            }
        }

        private void WaitForKilledProcessToExit()
        {
            if (!_process.WaitForExit((int)KillGracePeriod.TotalMilliseconds))
                throw new InstanceException($"The process did not exit within {KillGracePeriod.TotalSeconds} seconds after it was killed.");
        }

        private void ReceiveExit(object? sender, EventArgs e)
        {
            // Must stay asynchronous: the BCL raises Exited while holding lock(process), see issue #10.
            Task.WhenAll(_stdoutTask.Task, _stderrTask.Task).ContinueWith(_ => Complete(), TaskScheduler.Default);
        }

        private void Complete()
        {
            if (Interlocked.Exchange(ref _completing, 1) == 1) return;

            IProcessResult result;
            try
            {
                result = GetOrCreateResult();
            }
            catch (Exception e)
            {
                _completion.TrySetException(e);
                return;
            }

            RaiseExited(result);
            _completion.TrySetResult(result);
        }

        private IProcessResult GetOrCreateResult()
        {
            lock (_resultLock)
            {
                return _result ??= new ProcessResult(_process.ExitCode, Snapshot(_outputData), Snapshot(_errorData));
            }
        }

        private void RaiseExited(IProcessResult result)
        {
            var previous = _raisingExited;
            _raisingExited = this;
            try
            {
                Exited?.Invoke(_process, result);
            }
            catch (Exception)
            {
            }
            finally
            {
                _raisingExited = previous;
            }
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
                lock (dataList)
                {
                    dataList.Enqueue(data);
                    while (dataList.Count > _dataBufferCapacity)
                        dataList.Dequeue();
                }
                try
                {
                    eventHandler?.Invoke(this, data);
                }
                catch (Exception e)
                {
                    if (_handlerExceptions.Count < MaxHandlerExceptions) _handlerExceptions.Enqueue(e);
                }
            }
        }

        private IProcessResult ThrowIfHandlersFailed(IProcessResult result)
        {
            var exceptions = _handlerExceptions.ToArray();
            if (exceptions.Length == 1) ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
            if (exceptions.Length > 1) throw new AggregateException(exceptions);
            return result;
        }

        private static string[] Snapshot(Queue<string> dataList)
        {
            lock (dataList)
            {
                return dataList.ToArray();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProcessInstance));
        }

        private void ThrowIfDisposedAsExited()
        {
            if (_disposed) throw new InstanceProcessAlreadyExitedException(new ObjectDisposedException(nameof(ProcessInstance)));
        }

        private void ThrowIfInputUnavailable()
        {
            ThrowIfDisposed();
            if (_inputClosed) throw new InvalidOperationException("Standard input has been closed.");
            if (_process.HasExited) throw new InstanceProcessAlreadyExitedException();
        }
    }
}
