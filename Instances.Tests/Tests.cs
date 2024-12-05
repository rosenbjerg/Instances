using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Instances.Exceptions;
using NUnit.Framework;
using NUnit.Framework.Legacy;

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
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        }
        [Test]
        public void StaticFinishSuccessTest()
        {
            var outputReceived = false;
            var processResult = Instance.Finish("dotnet", "--list-runtimes", delegate { outputReceived = true; });
            Assert.That(outputReceived, Is.True);
            Assert.That(processResult.ExitCode, Is.EqualTo(0));
        }
        [Test]
        public void StaticFinishErrorTest()
        {
            var outputReceived = false;
            var processResult = Instance.Finish("dotnet", "run --project Nopes", delegate { outputReceived = true; });
            Assert.That(outputReceived, Is.True);
            Assert.That(processResult.ExitCode, Is.Not.EqualTo(0));
        }
        [Test]
        public async Task AsyncStaticFinishSuccessTest()
        {
            var outputReceived = false;
            var processResult = await Instance.FinishAsync("dotnet", "--list-runtimes", default, delegate { outputReceived = true; });
            Assert.That(outputReceived, Is.True);
            Assert.That(processResult.ExitCode, Is.EqualTo(0));
        }
        [Test]
        public async Task AsyncStaticFinishErrorTest()
        {
            var outputReceived = false;
            var processResult = await Instance.FinishAsync("dotnet", "run --project Nopes", default, delegate { outputReceived = true; });
            Assert.That(outputReceived, Is.True);
            Assert.That(processResult.ExitCode, Is.Not.EqualTo(0));
        }
        [Test]
        public async Task PublishesExitedEventOnSuccess()
        {
            var processArguments = new ProcessArguments("dotnet", "--list-runtimes");
            var completionSource = new TaskCompletionSource<IProcessResult>();
            processArguments.Exited += (_, args) => completionSource.TrySetResult(args);

            processArguments.Start();
            var result = await completionSource.Task;
            
            Assert.That(result.ExitCode, Is.EqualTo(0));
        }
        [Test]
        public void PublishesErrorEvents()
        {
            var processArguments = new ProcessArguments("dotnet", "run --project Nopes");
            var dataReceived = false;
            processArguments.ErrorDataReceived += (_, _) => dataReceived = true;

            using var instance = processArguments.Start();
            instance.WaitForExit();
            
            Assert.That(dataReceived, Is.True);
        }
        [Test]
        public async Task PublishesDataEvents()
        {
            var processArguments = new ProcessArguments("dotnet", "--list-runtimes");
            var dataReceived = false;
            processArguments.OutputDataReceived += (_, _) => dataReceived = true;
            
            using var instance = processArguments.Start();
            await instance.WaitForExitAsync();
            
            Assert.That(dataReceived, Is.True);
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
            
            Assert.That(linesExcludingNewline, Is.LessThan(linesIncludingNewline));
        }
        [Test]
        public void SecondErrorTest()
        {
            var processArguments = new ProcessArguments("dotnet", "run --project Nopes") { IgnoreEmptyLines = true };

            using var instance = processArguments.Start();
            instance.WaitForExit();
            Assert.That(instance.ErrorData.First() == "The build failed. Fix the build errors and run again.", Is.True);
        }
        [Test]
        public void ResultMatchesInstance()
        {
            var processArguments = new ProcessArguments("dotnet", "--help") { IgnoreEmptyLines = false };

            using var instance = processArguments.Start();
            var result = instance.WaitForExit();

            Assert.That(result.ExitCode, Is.EqualTo(0));
            CollectionAssert.AreEqual(instance.ErrorData, result.ErrorData);
            CollectionAssert.AreEqual(instance.OutputData, result.OutputData);
        }
        [Test]
        public async Task BasicErrorTest()
        {
            var processArguments = new ProcessArguments("dotnet", "run --project Nopes");
            
            using var instance = processArguments.Start();
            var result = await instance.WaitForExitAsync();
            
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            CollectionAssert.IsNotEmpty(instance.ErrorData);
        }
        [Test]
        public async Task SecondOutputTest()
        {
            using var instance = Instance.Start("dotnet", "--help");
            var result = await instance.WaitForExitAsync();
            
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.OutputData.Any(line => line.Contains("run")), Is.True);
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
            Assert.That(result.OutputData.Count, Is.EqualTo(3));
            Assert.That(result.ErrorData, Is.Empty);
        }

        [Test]
        public void ThrowsOnFileNotFound()
        {
            Assert.Throws<InstanceFileNotFoundException>(() =>
            {
                Instance.Finish("akjsdhfaklsjdhfasldkjh", "--version");
            });
        }
        
        [Test, CancelAfter(10000)]
        public async Task VerifyCancellationStopsProcess()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var started = DateTime.UtcNow;
            var instance = processArguments.Start();
            var cancel = new CancellationTokenSource();
            cancel.CancelAfter(100);
            await instance.WaitForExitAsync(cancel.Token);
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.That(elapsed, Is.GreaterThan(0.09));
        }

        [Test, CancelAfter(10000)]
        public async Task VerifyCancellationAlreadyExitedProcess()
        {
            var processArguments = GetWaitingProcessArguments();

            var instance = processArguments.Start();
            await instance.SendInputAsync("ok");

            using var tokenSource = new CancellationTokenSource();
            var result = await instance.WaitForExitAsync(tokenSource.Token);

            Assert.DoesNotThrow(() => tokenSource.Cancel());
            Assert.That(result.ExitCode, Is.EqualTo(0));
        }
        
        [Test, CancelAfter(10000)]
        public void VerifyKillStopsProcess()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var started = DateTime.UtcNow;
            var instance = processArguments.Start();
            Task.Delay(100).ContinueWith(_ => instance.Kill());
            instance.WaitForExit();
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.That(elapsed, Is.GreaterThan(0.09));
        }
        
        [Test, CancelAfter(10000)]
        public async Task DoubleKillReturnsSameResult()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var instance = processArguments.Start();
            await Task.Delay(100);
            var result1 = instance.Kill();
            var result2 = instance.Kill();
            
            Assert.That(result1.ExitCode, Is.EqualTo(result2.ExitCode));
            CollectionAssert.AreEqual(result1.OutputData, result2.OutputData);
            CollectionAssert.AreEqual(result1.ErrorData, result2.ErrorData);
        }
        
        [Test, CancelAfter(10000)]
        public async Task DoubleWaitForExitReturnsSameResult()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var instance = processArguments.Start();
            Task.Delay(100).ContinueWith(_ => instance.SendInput("ok"));
            var result1 = instance.WaitForExit();
            var result2 = instance.WaitForExit();
            
            Assert.That(result1.ExitCode, Is.EqualTo(result2.ExitCode));
            CollectionAssert.AreEqual(result1.OutputData, result2.OutputData);
            CollectionAssert.AreEqual(result1.ErrorData, result2.ErrorData);
        }
        
        [Test, CancelAfter(10000)]
        public async Task DoubleWaitForExitAsyncReturnsSameResult()
        {
            var processArguments = GetWaitingProcessArguments();
             
            var instance = processArguments.Start();
            Task.Delay(100).ContinueWith(_ => instance.SendInput("ok"));
            var result1 = await instance.WaitForExitAsync();
            var result2 = await instance.WaitForExitAsync();
            
            Assert.That(result1.ExitCode, Is.EqualTo(result2.ExitCode));
            CollectionAssert.AreEqual(result1.OutputData, result2.OutputData);
            CollectionAssert.AreEqual(result1.ErrorData, result2.ErrorData);
        }
        
        [Test, CancelAfter(10000)]
        public async Task VerifySendInputBehaviour()
        {
            var processArguments = GetWaitingProcessArguments();

            var started = DateTime.UtcNow;
            var instance = processArguments.Start();

            Task.Delay(100).ContinueWith(_ => instance.SendInput("ok"));
            await instance.WaitForExitAsync();
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.That(elapsed, Is.GreaterThan(0.09));
        }
        
        [Test, CancelAfter(10000)]
        public async Task VerifySendInputAsyncBehaviour()
        {
            var processArguments = GetWaitingProcessArguments();

            var started = DateTime.UtcNow;
            var instance = processArguments.Start();
            
            Task.Delay(100).ContinueWith(_ => instance.SendInputAsync("ok"));
            await instance.WaitForExitAsync();
        
            var elapsed = DateTime.UtcNow.Subtract(started).TotalSeconds;
            Assert.That(elapsed, Is.GreaterThan(0.09));
        }

        [OneTimeSetUp]
        public async Task Prepare()
        {
            await Instance.FinishAsync("dotnet", "publish ../../../../Instances.Tests.WaitingProgram -c Release -o ./waiting-program");
        }

        private static ProcessArguments GetWaitingProcessArguments()
        {
            return new ProcessArguments("dotnet", "./waiting-program/Instances.Tests.WaitingProgram.dll");
        }
    }
}