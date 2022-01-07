using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Instances
{
    public class Instance : IDisposable
    {
        public static (int exitCode, Instance instance) Finish(string path, string arguments = "", EventHandler<(DataType Type, string Data)>? outputHandler = default)
        {
            return Finish(new ProcessStartInfo {FileName = path, Arguments = arguments}, outputHandler);
        }

        public static (int exitCode, Instance instance) Finish(ProcessStartInfo startInfo, EventHandler<(DataType Type, string Data)>? outputHandler = default)
        {
            var instance = new Instance(startInfo);
            if (outputHandler != default) instance.DataReceived += outputHandler; 
            var exitCode = instance.BlockUntilFinished();
            return (exitCode, instance);
        }

        public static Task<(int exitCode, Instance instance)> FinishAsync(string path, string arguments = "", EventHandler<(DataType Type, string Data)>? outputHandler = default)
        {
            return FinishAsync(new ProcessStartInfo {FileName = path, Arguments = arguments}, outputHandler);
        }

        public static async Task<(int exitCode, Instance instance)> FinishAsync(ProcessStartInfo startInfo, EventHandler<(DataType Type, string Data)>? outputHandler = default)
        {
            var instance = new Instance(startInfo);
            if (outputHandler != default) instance.DataReceived += outputHandler; 
            var exitCode = await instance.FinishedRunning();
            return (exitCode, instance);
        }

        private readonly ProcessStartInfo? _startInfo;

        private Process? _process;
        private TaskCompletionSource<bool>? _mainTask;
        private TaskCompletionSource<bool>? _stdoutTask;
        private TaskCompletionSource<bool>? _stderrTask;

        private readonly Queue<string> _outputData = new Queue<string>();
        private readonly Queue<string> _errorData = new Queue<string>();
        private bool _started;

        public Instance(ProcessStartInfo startInfo)
        {
            _startInfo = startInfo;
        }

        public Instance(string path, string arguments = "") : this(new ProcessStartInfo
            {FileName = path, Arguments = arguments})
        {
        }

        public IReadOnlyList<string> OutputData => _outputData.ToList().AsReadOnly();
        public IReadOnlyList<string> ErrorData => _errorData.ToList().AsReadOnly();

        public async Task SendInput(string input)
        {
            if (_process != null)
            {
                await _process.StandardInput.WriteAsync(input);
            }
        }

        public bool IgnoreEmptyLines { get; set; } = true;
        public int DataBufferCapacity { get; set; } = 100;

        public bool Started
        {
            get => _started;
            set
            {
                if (_started && value) throw new InstanceAlreadyStartedException();
                if (!_started && !value) throw new InstanceNotRunningException();

                if (value) Start();
                else _process?.Kill();
            }
        }

        private void Start()
        {
            if (_started) throw new InstanceAlreadyStartedException();
            
            _outputData!.Clear();
            _errorData!.Clear();
            _mainTask = new TaskCompletionSource<bool>();
            _stdoutTask = new TaskCompletionSource<bool>();
            _stderrTask = new TaskCompletionSource<bool>();

            InitializeProcess();

            _started = true;
            try
            {
                _process!.Start();
            }
            catch (Win32Exception e) when(e.Message == "The system cannot find the file specified." || e.Message == "No such file or directory")
            {
                throw new InstanceFileNotFoundException(_process!.StartInfo.FileName, e);
            }
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        private void InitializeProcess()
        {
            _process?.Dispose();
            _process = new Process
            {
                StartInfo = _startInfo ?? new ProcessStartInfo(),
                EnableRaisingEvents = true
            };
            _process.StartInfo.CreateNoWindow = true;
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
            Task.WhenAll(_stdoutTask!.Task, _stderrTask!.Task).ContinueWith(task =>
            {
                _started = false;
                Exited?.Invoke(this, _process!.HasExited ? _process.ExitCode : -100);
                return _mainTask!.TrySetResult(true);
            });
        }

        public event EventHandler<int>? Exited;
        public event EventHandler<(DataType Type, string Data)>? DataReceived;

        public async Task<int> FinishedRunning()
        {
            if (!_started) Started = true;
            await _mainTask!.Task.ConfigureAwait(false);
            return _process!.ExitCode;
        }

        public int BlockUntilFinished()
        {
            if (!_started) Started = true;
            _process!.WaitForExit();
            return _process.ExitCode;
        }

        private void ReceiveOutput(object _, DataReceivedEventArgs e) => AddData(_outputData, e.Data, DataType.Output,
            DataBufferCapacity, IgnoreEmptyLines, DataReceived, _stdoutTask!.TrySetResult);

        private void ReceiveError(object _, DataReceivedEventArgs e) => AddData(_errorData, e.Data, DataType.Error,
            DataBufferCapacity, IgnoreEmptyLines, DataReceived, _stderrTask!.TrySetResult);

        private static void AddData(Queue<string> dataList, string? data, DataType type, int capacity, bool ignoreEmpty,
            EventHandler<(DataType Type, string Data)>? dataTrigger, Func<bool, bool> nullTrigger)
        {
            if (data == null)
            {
                nullTrigger(true);
            }
            else
            {
                if (ignoreEmpty && data == string.Empty) return;
                dataList.Enqueue(data);
                dataList.DequeueMultiple(dataList.Count - capacity);
                dataTrigger?.Invoke(null, (type, data));
            }
        }

        
        public void Dispose()
        {
            _process?.Dispose();
        }
    }
}