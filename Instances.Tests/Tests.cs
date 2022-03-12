using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Instances;
using Instances.Exceptions;
using NUnit.Framework;

namespace Instance.Tests
{
    public class Tests
    {
        [Test]
        public void PublishesExitedEventOnError()
        {
            var arguments = new ProcessArguments("dotnet", "run --project Nopes");
            var completionSource = new TaskCompletionSource<IProcessResult>();
            arguments.Exited += (sender, args) => completionSource.TrySetResult(args);

            arguments.Start();
            var result = completionSource.Task.GetAwaiter().GetResult();
            
            Assert.AreEqual(1, result.ExitCode);
        }
        [Test]
        public void StaticFinishSuccessTest()
        {
            var outputReceived = false;
            var processResult = Instances.Instance.Finish("dotnet", "--list-runtimes", delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.AreEqual(0, processResult.ExitCode);
        }
        [Test]
        public void StaticFinishErrorTest()
        {
            var outputReceived = false;
            var processResult = Instances.Instance.Finish("dotnet", "run --project Nopes", delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.AreNotEqual(0, processResult.ExitCode);
        }
        [Test]
        public async Task AsyncStaticFinishSuccessTest()
        {
            var outputReceived = false;
            var processResult = await Instances.Instance.FinishAsync("dotnet", "--list-runtimes", default, delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.AreEqual(0, processResult.ExitCode);
        }
        [Test]
        public async Task AsyncStaticFinishErrorTest()
        {
            var outputReceived = false;
            var processResult = await Instances.Instance.FinishAsync("dotnet", "run --project Nopes", default, delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.AreNotEqual(0, processResult.ExitCode);
        }
        [Test]
        public async Task PublishesExitedEventOnSuccess()
        {
            var processArguments = new ProcessArguments("dotnet", "--list-runtimes");
            var completionSource = new TaskCompletionSource<IProcessResult>();
            processArguments.Exited += (sender, args) => completionSource.TrySetResult(args);

            processArguments.Start();
            var result = await completionSource.Task;
            
            Assert.AreEqual(0, result.ExitCode);
        }
        [Test]
        public void PublishesErrorEvents()
        {
            var processArguments = new ProcessArguments("dotnet", "run --project Nopes");
            var dataReceived = false;
            processArguments.ErrorDataReceived += (sender, args) => dataReceived = true;

            using var instance = processArguments.Start();
            instance.WaitForExit();
            
            Assert.IsTrue(dataReceived);
        }
        [Test]
        public async Task PublishesDataEvents()
        {
            var processArguments = new ProcessArguments("dotnet", "--list-runtimes");
            var dataReceived = false;
            processArguments.OutputDataReceived += (sender, args) => dataReceived = true;
            
            using var instance = processArguments.Start();
            await instance.WaitForExitAsync();
            
            Assert.IsTrue(dataReceived);
        }
        [Test]
        public async Task IgnoreEmptyLinesWork()
        {
            var processArguments = new ProcessArguments("dotnet", "--help") { IgnoreEmptyLines = false };
            
            using var instance = processArguments.Start();
            await instance.WaitForExitAsync();
            var linesIncludingNewline = instance.OutputData.Count;

            processArguments.IgnoreEmptyLines = true;
            using var instance2 = processArguments.Start();
            await instance2.WaitForExitAsync();
            var linesExcludingNewline = instance2.OutputData.Count;
            
            Assert.Less(linesExcludingNewline, linesIncludingNewline);
        }
        [Test]
        public void SecondErrorTest()
        {
            var processArguments = new ProcessArguments("dotnet", "run --project Nopes") { IgnoreEmptyLines = true };

            using var instance = processArguments.Start();
            instance.WaitForExit();
            
            Assert.IsTrue(instance.ErrorData.First() == "The build failed. Fix the build errors and run again.");
        }
        [Test]
        public void ResultMatchesInstance()
        {
            var processArguments = new ProcessArguments("dotnet", "--help") { IgnoreEmptyLines = false };

            using var instance = processArguments.Start();
            var result = instance.WaitForExit();

            Assert.AreEqual(0, result.ExitCode);
            CollectionAssert.AreEqual(instance.ErrorData, result.ErrorData);
            CollectionAssert.AreEqual(instance.OutputData, result.OutputData);
        }
        [Test]
        public async Task BasicErrorTest()
        {
            var processArguments = new ProcessArguments("dotnet", "run --project Nopes");
            
            using var instance = processArguments.Start();
            var result = await instance.WaitForExitAsync();
            
            Assert.AreEqual(1, result.ExitCode);
            CollectionAssert.IsNotEmpty(instance.ErrorData);
        }
        [Test]
        public async Task SecondOutputTest()
        {
            var processArguments = new ProcessArguments("dotnet", "--help");
            
            using var instance = processArguments.Start();
            var result = await instance.WaitForExitAsync();
            
            Assert.AreEqual(0, result.ExitCode);
            Assert.IsTrue(instance.OutputData.Any(line => line.Contains("run")));
            Assert.IsTrue(!instance.ErrorData.Any());
        }
        [Test]
        public void BasicOutputTest()
        {
            var processArguments = new ProcessArguments("dotnet", "--version");
            
            using var instance = processArguments.Start();
            instance.WaitForExit();
            
            CollectionAssert.IsNotEmpty(instance.OutputData);
            CollectionAssert.IsEmpty(instance.ErrorData);
        }
        
        [Test]
        public async Task BufferCapacitiesCapsOutput()
        {
            var processArguments = new ProcessArguments("dotnet", "--help") { DataBufferCapacity = 3 };
            using var instance = processArguments.Start();
            await instance.WaitForExitAsync();
            Assert.AreEqual(3, instance.OutputData.Count);
            Assert.IsEmpty(instance.ErrorData);
        }

        [Test]
        public void ThrowsOnFileNotFound()
        {
            Assert.Throws<InstanceFileNotFoundException>(() =>
            {
                Instances.Instance.Finish("akjsdhfaklsjdhfasldkjh", "--version");
            });
        }
        
        [Test, Timeout(10000)]
        public async Task VerifyCancellationStopsProcess()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var started = DateTime.UtcNow;
            var instance = processArguments.Start();
            var cancel = new CancellationTokenSource();
            cancel.CancelAfter(200);
            await instance.WaitForExitAsync(cancel.Token);
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.Less(elapsed, 3);
            Assert.Greater(elapsed, 0.19);
        }
        
        [Test, Timeout(10000)]
        public async Task VerifyKillStopsProcess()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var started = DateTime.UtcNow;
            var instance = processArguments.Start();
            Task.Delay(200).ContinueWith(t => instance.Kill());
            await instance.WaitForExitAsync();
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.Less(elapsed, 3);
            Assert.Greater(elapsed, 0.19);
        }
        
        [Test, Timeout(10000)]
        public async Task DoubleKillThrowsException()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var instance = processArguments.Start();
            await Task.Delay(200);
            instance.Kill();
            Assert.Throws<InstanceProcessAlreadyExitedException>(() => instance.Kill());
        }
        
        [Test, Timeout(10000)]
        public void DoubleWaitForExitThrowsException()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var instance = processArguments.Start();
            Task.Delay(100).ContinueWith(t => instance.SendInput("ok"));
            instance.WaitForExit();
            Assert.Throws<InstanceProcessAlreadyExitedException>(() => instance.WaitForExit());
        }
        
        [Test, Timeout(10000)]
        public async Task DoubleWaitForExitAsyncThrowsException()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var instance = processArguments.Start();
            Task.Delay(100).ContinueWith(t => instance.SendInput("ok"));
            await instance.WaitForExitAsync();
            Assert.ThrowsAsync<InstanceProcessAlreadyExitedException>(() => instance.WaitForExitAsync());
        }
        
        [Test, Timeout(10000)]
        public async Task VerifySendInputBehaviour()
        {
            var processArguments = GetWaitingProcessArguments();

            var started = DateTime.UtcNow;
            var instance = processArguments.Start();

            Task.Delay(200).ContinueWith(t => instance.SendInput("ok"));
            await instance.WaitForExitAsync();
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.Less(elapsed, 5);
            Assert.Greater(elapsed, 0.19);
        }
        
        [Test, Timeout(10000)]
        public async Task VerifySendInputAsyncBehaviour()
        {
            var processArguments = GetWaitingProcessArguments();

            var started = DateTime.UtcNow;
            var instance = processArguments.Start();
            
            Task.Delay(200).ContinueWith(t => instance.SendInputAsync("ok"));
            await instance.WaitForExitAsync();
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.Less(elapsed, 5);
            Assert.Greater(elapsed, 0.19);
        }

        private static ProcessArguments GetWaitingProcessArguments()
        {
            return new ProcessArguments("dotnet", "run --project ../../../../Instances.Tests.WaitingProgram");
        }
    }
}