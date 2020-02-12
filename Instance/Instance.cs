using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Instances
{
    public class Instance : IDisposable
    {
        private readonly ProcessStartInfo _startInfo;

        private bool _started;
        private Process _process;
        private readonly object _lock = new object();
        private TaskCompletionSource<bool> _stdoutTask;
        private TaskCompletionSource<bool> _stderrTask;

        private readonly Stack<string> _outputData = new Stack<string>();
        private readonly Stack<string> _errorData = new Stack<string>();

        public Instance(ProcessStartInfo startInfo)
        {
            _startInfo = startInfo!;
        }

        public Instance(string path, string arguments = "", string? username = default)
        {
            _startInfo = new ProcessStartInfo {FileName = path, Arguments = arguments};
            if (username != default) _startInfo.UserName = username;
        }

        public void ClearData(DataType? type = null)
        {
            if (type == DataType.Output || type == null) _outputData.Clear();
            if (type == DataType.Error || type == null) _errorData.Clear();
        }

        public IReadOnlyList<string> OutputData => _outputData.Reverse().ToList().AsReadOnly();
        public IReadOnlyList<string> ErrorData => _errorData.Reverse().ToList().AsReadOnly();

        public async Task SendInput(string input)
        {
            try
            {
                await _process.StandardInput.WriteAsync(input!);
            }
            catch (Exception e)
            {
                AddData(_errorData, e.Message, DataType.Error, DataBufferCapacity, IgnoreEmptyLines, DataReceived, _stdoutTask.TrySetResult, ExitReceived);
            }
        }

        public bool IgnoreEmptyLines { get; set; } = true;
        public int DataBufferCapacity { get; set; } = 100;

        public bool Started
        {
            get => _started;
            set
            {
                lock (_lock)
                {
                    if (_started && value) throw new Exception("Instance has already been started!");
                    if (!_started && !value) throw new Exception("Instance isn't currently running!");

                    _started = value;
                    _stdoutTask = new TaskCompletionSource<bool>();
                    _stderrTask = new TaskCompletionSource<bool>();
                    InitializeProcess();
                }

                if (value) StartProcess();
                else StopProcess();
            }
        }

        private void StartProcess()
        {
            _started = true;

            try
            {
                _started = _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                if (!_started) Exited?.Invoke(this, _process.ExitCode);
            }
            catch (Exception e)
            {
                AddData(_errorData, e.Message, DataType.Error, DataBufferCapacity, IgnoreEmptyLines, DataReceived, _stderrTask.TrySetResult, ExitReceived);
                _started = false;
                _stderrTask.TrySetResult(true);
                Exited?.Invoke(this, _process.ExitCode);
            }
        }

        private void InitializeProcess()
        {
            _process?.Dispose();
            _process = new Process
            {
                StartInfo = _startInfo,
                EnableRaisingEvents = true
            };
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.OutputDataReceived += ReceiveOutput;
            _process.ErrorDataReceived += ReceiveError;
        }

        private void StopProcess()
        {
            if (_process != default && !_process.HasExited)
            {
                _process.Kill();
                _started = false;
                Exited?.Invoke(this, _process.ExitCode);
            }
        }

        public event EventHandler<int> Exited;
        public event EventHandler<(DataType Type, string Data)> DataReceived;

        public async Task<int> FinishedRunning()
        {
            if (!_started) Started = true;
            await Task.WhenAny(new Task[]
            {
                _stdoutTask.Task,
                _stderrTask.Task
            }).ConfigureAwait(false);
            _started = false;
            return _process.ExitCode;
        }

        public int BlockUntilFinished()
        {
            if (!_started) Started = true;
            if (_process == null) return 0;
            _process.WaitForExit();
            _started = false;
            return _process.ExitCode;
        }
        
        private void ReceiveOutput(object _, DataReceivedEventArgs e) => AddData(_outputData, e.Data, DataType.Output,
            DataBufferCapacity, IgnoreEmptyLines, DataReceived, _stdoutTask.TrySetResult, ExitReceived);

        private void ReceiveError(object _, DataReceivedEventArgs e) => AddData(_errorData, e.Data, DataType.Error,
            DataBufferCapacity, IgnoreEmptyLines, DataReceived, _stderrTask.TrySetResult, ExitReceived);

        private void ExitReceived()
        {
            _started = false;
            Exited?.Invoke(this, _process.ExitCode);
        }
        
        private static void AddData(Stack<string> dataList, string? data, DataType type, int capacity, bool ignoreEmpty,
            EventHandler<(DataType Type, string Data)> dataTrigger, Func<bool, bool> firstNullTrigger, Action secondNullTrigger)
        {
            if (data == null)
            {
                firstNullTrigger(true);
                secondNullTrigger();
            }
            else
            {
                if (ignoreEmpty && data == "") return;
                dataList.Push(data);
                dataList.PopMultiple(dataList.Count - capacity);
                dataTrigger?.Invoke(null, (type, data));
            }
        }

        public void Dispose()
        {
            _process.Dispose();
        }
    }
}