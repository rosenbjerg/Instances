using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Instances;
using NUnit.Framework;

namespace Instance.Tests
{
    public class Tests
    {
        [Test]
        public void PublishesExitedEventOnError()
        {
            using var instance = new Instances.Instance("dotnet", "run --project Nopes");
            var completionSource = new TaskCompletionSource<int>();
            instance.Exited += (sender, args) => completionSource.TrySetResult(args);

            instance.Started = true;
            var result = completionSource.Task.GetAwaiter().GetResult();
            
            Assert.AreEqual(1, result);
        }
        [Test]
        public void StaticFinishSuccessTest()
        {
            var outputReceived = false;
            var (exitCode, _) = Instances.Instance.Finish("dotnet", "--list-runtimes", delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.AreEqual(0, exitCode);
        }
        [Test]
        public void StaticFinishErrorTest()
        {
            var outputReceived = false;
            var (exitCode, _) = Instances.Instance.Finish("dotnet", "run --project Nopes", delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.AreNotEqual(0, exitCode);
        }
        [Test]
        public async Task AsyncStaticFinishSuccessTest()
        {
            var outputReceived = false;
            var (exitCode, _) = await Instances.Instance.FinishAsync("dotnet", "--list-runtimes", delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.AreEqual(0, exitCode);
        }
        [Test]
        public async Task AsyncStaticFinishErrorTest()
        {
            var outputReceived = false;
            var (exitCode, _) = await Instances.Instance.FinishAsync("dotnet", "run --project Nopes", delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.AreNotEqual(0, exitCode);
        }
        [Test]
        public async Task PublishesExitedEventOnSuccess()
        {
            using var instance = new Instances.Instance("dotnet", "--list-runtimes");
            var completionSource = new TaskCompletionSource<int>();
            instance.Exited += (sender, args) => completionSource.TrySetResult(args);

            instance.Started = true;
            var result = await completionSource.Task;
            
            Assert.AreEqual(0, result);
        }
        [Test]
        public void PublishesErrorEvents()
        {
            using var instance = new Instances.Instance("dotnet", "run --project Nopes");
            var dataReceived = false;
            instance.DataReceived += (sender, args) => dataReceived = args.Type == DataType.Error;

            instance.BlockUntilFinished();
            
            Assert.IsTrue(dataReceived);
        }
        [Test]
        public async Task PublishesDataEvents()
        {
            using var instance = new Instances.Instance("dotnet", "--list-runtimes");
            var dataReceived = false;
            instance.DataReceived += (sender, args) => dataReceived = args.Type == DataType.Output;
            
            await instance.FinishedRunning();
            
            Assert.IsTrue(dataReceived);
        }
        [Test]
        public async Task IgnoreEmptyLinesWork()
        {
            using var instance = new Instances.Instance("dotnet", "--help") { IgnoreEmptyLines = false };
            await instance.FinishedRunning();
            var linesIncludingNewline = instance.OutputData.Count;
            
            using var instance2 = new Instances.Instance("dotnet", "--help") { IgnoreEmptyLines = true };
            await instance2.FinishedRunning();
            var linesExcludingNewline = instance2.OutputData.Count;
            
            Assert.Less(linesExcludingNewline, linesIncludingNewline);
        }
        [Test]
        public void SecondErrorTest()
        {
            using var instance = new Instances.Instance("dotnet", "run --project Nopes");

            instance.BlockUntilFinished();
            
            Assert.IsTrue(instance.ErrorData.First() == "The build failed. Fix the build errors and run again.");
        }
        [Test]
        public async Task BasicErrorTest()
        {
            using var instance = new Instances.Instance("dotnet", "run --project Nopes");
            
            var exitCode = await instance.FinishedRunning();
            
            Assert.AreEqual(1, exitCode);
            Assert.IsNotEmpty(string.Join("\n", instance.ErrorData));
        }
        [Test]
        public async Task SecondOutputTest()
        {
            using var instance = new Instances.Instance("dotnet", "--version");
            
            var exitCode = await instance.FinishedRunning();
            
            Assert.AreEqual(0, exitCode);
            Assert.AreEqual("3.1.301", instance.OutputData.First());
            Assert.IsTrue(!instance.ErrorData.Any());
        }
        [Test]
        public void BasicOutputTest()
        {
            using var instance = new Instances.Instance("dotnet", "--version");
            
            instance.BlockUntilFinished();
            
            Assert.IsNotEmpty(string.Join("\n", instance.OutputData));
            Assert.IsTrue(!instance.ErrorData.Any());
        }
        [Test]
        public async Task StartedPropertyBehavesCorrectly()
        {
            using var instance = new Instances.Instance("dotnet", "--version");
            Assert.False(instance.Started);
            var running = instance.FinishedRunning();
            Assert.True(instance.Started);
            await running;
            Assert.False(instance.Started);
        }
        [Test]
        public async Task BufferCapacitiesCapsOutput()
        {
            using var instance = new Instances.Instance("dotnet", "--help") { DataBufferCapacity = 3 };
            await instance.FinishedRunning();
            Assert.AreEqual(3, instance.OutputData.Count);
            Assert.IsEmpty(instance.ErrorData);
        }
        // [Test]
        // [Timeout(3)]
        // public async Task StopTest()
        // {
        //     var timeoutSeconds = 5;
        //     var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        //     var testCommand = isWindows ? "timeout" : "read";
        //     var testArgs = isWindows ? $"{timeoutSeconds}" : $"-t {timeoutSeconds} -n1";
        //     
        //     var started = DateTime.UtcNow;
        //     using var instance = new Instances.Instance(testCommand, testArgs);
        //     var running = instance.FinishedRunning();
        //     // instance.Started = false;
        //     await running;
        //
        //     var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
        //     
        //     Assert.Greater(timeoutSeconds / 2.0, elapsed);
        // }
        [Test]
        public void ThrowsOnDoubleStart()
        {
            Assert.Throws<InstanceException>(() =>
            {
                using var instance = new Instances.Instance("dotnet", "--version");
                instance.Started = true;
                instance.Started = true;
            });
        }
        [Test]
        public void ThrowsOnPreemptiveStop()
        {
            Assert.Throws<InstanceException>(() =>
            {
                using var instance = new Instances.Instance("dotnet", "--version");
                instance.Started = false;
            });
        }
        [Test]
        public async Task RestartTest()
        {
            using var instance = new Instances.Instance("dotnet", "--version");
            
            var exitCode = instance.BlockUntilFinished();
            
            Assert.AreEqual(0, exitCode);
            Assert.AreEqual("3.1.301", instance.OutputData.First());
            
            var exitCode2 = await instance.FinishedRunning();
            
            Assert.AreEqual(0, exitCode2);
            Assert.AreEqual("3.1.301", instance.OutputData.First());
            
            var exitCode3 = instance.BlockUntilFinished();
            
            Assert.AreEqual(0, exitCode3);
            Assert.AreEqual("3.1.301", instance.OutputData.First());
            
            var exitCode4 = await instance.FinishedRunning();
            
            Assert.AreEqual(0, exitCode4);
            Assert.AreEqual("3.1.301", instance.OutputData.First());
        }
    }
}