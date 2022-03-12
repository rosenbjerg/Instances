using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Instances.Exceptions;
using NUnit.Framework;

namespace Instances.Tests
{
    public class Tests
    {
        [Test]
        public void PublishesExitedEventOnError()
        {
            var arguments = new ProcessArguments("dotnet", "run --project Nopes");
            var completionSource = new TaskCompletionSource<IProcessResult>();
            arguments.Exited += (_, args) => completionSource.TrySetResult(args);

            arguments.Start();
            var result = completionSource.Task.GetAwaiter().GetResult();
            
            Assert.NotZero(result.ExitCode);
        }
        [Test]
        public void StaticFinishSuccessTest()
        {
            var outputReceived = false;
            var processResult = Instance.Finish("dotnet", "--list-runtimes", delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.Zero(processResult.ExitCode);
        }
        [Test]
        public void StaticFinishErrorTest()
        {
            var outputReceived = false;
            var processResult = Instance.Finish("dotnet", "run --project Nopes", delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.NotZero(processResult.ExitCode);
        }
        [Test]
        public async Task AsyncStaticFinishSuccessTest()
        {
            var outputReceived = false;
            var processResult = await Instance.FinishAsync("dotnet", "--list-runtimes", default, delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.Zero(processResult.ExitCode);
        }
        [Test]
        public async Task AsyncStaticFinishErrorTest()
        {
            var outputReceived = false;
            var processResult = await Instance.FinishAsync("dotnet", "run --project Nopes", default, delegate { outputReceived = true; });
            Assert.AreEqual(true, outputReceived);
            Assert.NotZero(processResult.ExitCode);
        }
        [Test]
        public async Task PublishesExitedEventOnSuccess()
        {
            var processArguments = new ProcessArguments("dotnet", "--list-runtimes");
            var completionSource = new TaskCompletionSource<IProcessResult>();
            processArguments.Exited += (_, args) => completionSource.TrySetResult(args);

            processArguments.Start();
            var result = await completionSource.Task;
            
            Assert.Zero(result.ExitCode);
        }
        [Test]
        public void PublishesErrorEvents()
        {
            var processArguments = new ProcessArguments("dotnet", "run --project Nopes");
            var dataReceived = false;
            processArguments.ErrorDataReceived += (_, _) => dataReceived = true;

            using var instance = processArguments.Start();
            instance.WaitForExit();
            
            Assert.IsTrue(dataReceived);
        }
        [Test]
        public async Task PublishesDataEvents()
        {
            var processArguments = new ProcessArguments("dotnet", "--list-runtimes");
            var dataReceived = false;
            processArguments.OutputDataReceived += (_, _) => dataReceived = true;
            
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

            Assert.Zero(result.ExitCode);
            CollectionAssert.AreEqual(instance.ErrorData, result.ErrorData);
            CollectionAssert.AreEqual(instance.OutputData, result.OutputData);
        }
        [Test]
        public async Task BasicErrorTest()
        {
            var processArguments = new ProcessArguments("dotnet", "run --project Nopes");
            
            using var instance = processArguments.Start();
            var result = await instance.WaitForExitAsync();
            
            Assert.NotZero(result.ExitCode);
            CollectionAssert.IsNotEmpty(instance.ErrorData);
        }
        [Test]
        public async Task SecondOutputTest()
        {
            using var instance = Instance.Start("dotnet", "--help");
            var result = await instance.WaitForExitAsync();
            
            Assert.Zero(result.ExitCode);
            Assert.IsTrue(result.OutputData.Any(line => line.Contains("run")));
            CollectionAssert.IsEmpty(instance.ErrorData);
        }
        [Test]
        public void BasicOutputTest()
        {
            var processArguments = new ProcessArguments("dotnet", "--version");
            
            var result = processArguments.StartAndWaitForExit();
            
            CollectionAssert.IsNotEmpty(result.OutputData);
            CollectionAssert.IsEmpty(result.ErrorData);
        }
        
        [Test]
        public async Task BufferCapacitiesCapsOutput()
        {
            var processArguments = new ProcessArguments("dotnet", "--help") { DataBufferCapacity = 3 };
            var result = await processArguments.StartAndWaitForExitAsync();
            Assert.AreEqual(3, result.OutputData.Count);
            Assert.IsEmpty(result.ErrorData);
        }

        [Test]
        public void ThrowsOnFileNotFound()
        {
            Assert.Throws<InstanceFileNotFoundException>(() =>
            {
                Instance.Finish("akjsdhfaklsjdhfasldkjh", "--version");
            });
        }
        
        [Test, Timeout(10000)]
        public async Task VerifyCancellationStopsProcess()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var started = DateTime.UtcNow;
            var instance = processArguments.Start();
            var cancel = new CancellationTokenSource();
            cancel.CancelAfter(500);
            await instance.WaitForExitAsync(cancel.Token);
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.Greater(elapsed, 0.49);
        }
        
        [Test, Timeout(10000)]
        public async Task VerifyKillStopsProcess()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var started = DateTime.UtcNow;
            var instance = processArguments.Start();
            Task.Delay(500).ContinueWith(_ => instance.Kill());
            await instance.WaitForExitAsync();
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.Greater(elapsed, 0.49);
        }
        
        [Test, Timeout(10000)]
        public async Task DoubleKillThrowsException()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var instance = processArguments.Start();
            await Task.Delay(200);
            instance.Kill();
            await Task.Delay(200);
            Assert.Throws<InstanceProcessAlreadyExitedException>(() => instance.Kill());
        }
        
        [Test, Timeout(10000)]
        public async Task DoubleWaitForExitThrowsException()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var instance = processArguments.Start();
            Task.Delay(500).ContinueWith(_ => instance.SendInput("ok"));
            instance.WaitForExit();
            await Task.Delay(100);
            Assert.Throws<InstanceProcessAlreadyExitedException>(() => instance.WaitForExit());
        }
        
        [Test, Timeout(10000)]
        public async Task DoubleWaitForExitAsyncThrowsException()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var instance = processArguments.Start();
            Task.Delay(500).ContinueWith(_ => instance.SendInput("ok"));
            await instance.WaitForExitAsync();
            await Task.Delay(100);
            Assert.ThrowsAsync<InstanceProcessAlreadyExitedException>(() => instance.WaitForExitAsync());
        }
        
        [Test, Timeout(10000)]
        public async Task VerifySendInputBehaviour()
        {
            var processArguments = GetWaitingProcessArguments();

            var started = DateTime.UtcNow;
            var instance = processArguments.Start();

            Task.Delay(500).ContinueWith(_ => instance.SendInput("ok"));
            await instance.WaitForExitAsync();
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.Greater(elapsed, 0.49);
        }
        
        [Test, Timeout(10000)]
        public async Task VerifySendInputAsyncBehaviour()
        {
            var processArguments = GetWaitingProcessArguments();

            var started = DateTime.UtcNow;
            var instance = processArguments.Start();
            
            Task.Delay(500).ContinueWith(_ => instance.SendInputAsync("ok"));
            await instance.WaitForExitAsync();
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.Greater(elapsed, 0.49);
        }

        [OneTimeSetUp]
        public async Task Prepare()
        {
            await Instance.FinishAsync("dotnet", "build ../../../../Instances.Tests.WaitingProgram");
        }

        private static ProcessArguments GetWaitingProcessArguments()
        {
            return new ProcessArguments("dotnet", "run --project ../../../../Instances.Tests.WaitingProgram --no-build");
        }
    }
}