using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Instances
{
    public class Instance : IDisposable
    {
        public static (int exitCode, Instance instance) Finish(string path, string arguments = "", EventHandler<(DataType Type, string Data)> outputHandler = default)
        {
            return Finish(new ProcessStartInfo {FileName = path, Arguments = arguments}, outputHandler);
        }

        public static (int exitCode, Instance instance) Finish(ProcessStartInfo startInfo, EventHandler<(DataType Type, string Data)> outputHandler = default)
        {
            var instance = new Instance(startInfo);
            if (outputHandler != default) instance.DataReceived += outputHandler; 
            var exitCode = instance.BlockUntilFinished();
            return (exitCode, instance);
        }

        public static Task<(int exitCode, Instance instance)> FinishAsync(string path, string arguments = "", EventHandler<(DataType Type, string Data)> outputHandler = default)
        {
            return FinishAsync(new ProcessStartInfo {FileName = path, Arguments = arguments}, outputHandler);
        }

        public static async Task<(int exitCode, Instance instance)> FinishAsync(ProcessStartInfo startInfo, EventHandler<(DataType Type, string Data)> outputHandler = default)
        {
            var instance = new Instance(startInfo);
            if (outputHandler != default) instance.DataReceived += outputHandler; 
            var exitCode = await instance.FinishedRunning();
            return (exitCode, instance);
        }

        private readonly ProcessStartInfo _startInfo;

        private Process _process;
        private TaskCompletionSource<bool> _mainTask;
        private TaskCompletionSource<bool> _stdoutTask;
        private TaskCompletionSource<bool> _stderrTask;

        private readonly Stack<string> _outputData = new Stack<string>();
        private readonly Stack<string> _errorData = new Stack<string>();
        private bool _started;

        public Instance(ProcessStartInfo startInfo)
        {
            _startInfo = startInfo;
        }

        public Instance(string path, string arguments = "") : this(new ProcessStartInfo
            {FileName = path, Arguments = arguments})
        {
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
                AddData(_errorData, e.Message, DataType.Error, DataBufferCapacity, IgnoreEmptyLines, DataReceived,
                    _stdoutTask.TrySetResult);
            }
        }

        public bool IgnoreEmptyLines { get; set; } = true;
        public int DataBufferCapacity { get; set; } = 100;

        public bool Started
        {
            get => _started;
            set
            {
                if (_started && value) throw new InstanceException("Instance has already been started!");
                if (!_started && !value) throw new InstanceException("Instance is not running!");

                if (value) Start();
                else _process?.Kill();
            }
        }

        private void Start()
        {
            if (_started) throw new InstanceException("Instance has already been started!");
            
            _outputData.Clear();
            _errorData.Clear();
            _mainTask = new TaskCompletionSource<bool>();
            _stdoutTask = new TaskCompletionSource<bool>();
            _stderrTask = new TaskCompletionSource<bool>();

            InitializeProcess();

            _started = true;
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        private void InitializeProcess()
        {
            _process?.Dispose();
            _process = new Process
            {
                StartInfo = _startInfo,
                EnableRaisingEvents = true
            };
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.OutputDataReceived += ReceiveOutput;
            _process.ErrorDataReceived += ReceiveError;
            _process.Exited += ReceiveExit;
        }

        private void ReceiveExit(object sender, EventArgs e)
        {
            Task.WhenAll(_stdoutTask.Task, _stderrTask.Task).ContinueWith(task =>
            {
                Exited?.Invoke(this, _process.HasExited ? _process.ExitCode : -100);
                return _mainTask.TrySetResult(true);
            });
        }

        public event EventHandler<int> Exited;
        public event EventHandler<(DataType Type, string Data)> DataReceived;

        public async Task<int> FinishedRunning()
        {
            if (!_started) Started = true;
            await _mainTask.Task.ConfigureAwait(false);
            return _process.ExitCode;
        }

        public int BlockUntilFinished()
        {
            if (!_started) Started = true;
            _process.WaitForExit();
            return _process.ExitCode;
        }

        private void ReceiveOutput(object _, DataReceivedEventArgs e) => AddData(_outputData, e.Data, DataType.Output,
            DataBufferCapacity, IgnoreEmptyLines, DataReceived, _stdoutTask.TrySetResult);

        private void ReceiveError(object _, DataReceivedEventArgs e) => AddData(_errorData, e.Data, DataType.Error,
            DataBufferCapacity, IgnoreEmptyLines, DataReceived, _stderrTask.TrySetResult);

        private static void AddData(Stack<string> dataList, string? data, DataType type, int capacity, bool ignoreEmpty,
            EventHandler<(DataType Type, string Data)> dataTrigger, Func<bool, bool> nullTrigger)
        {
            if (data == null)
            {
                nullTrigger(true);
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