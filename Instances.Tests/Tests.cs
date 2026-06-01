using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
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
            // A failing process is expected to emit something; which stream it lands on
            // varies across dotnet SDK versions, so accept output on either stdout or stderr.
            var processResult = Instance.Finish("dotnet", "run --project Nopes",
                delegate { outputReceived = true; },
                delegate { outputReceived = true; });
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
            // See StaticFinishErrorTest: accept failing output on either stream.
            var processResult = await Instance.FinishAsync("dotnet", "run --project Nopes", default,
                delegate { outputReceived = true; },
                delegate { outputReceived = true; });
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
            var result = instance.WaitForExit();
            // The exact stderr wording differs between SDK versions; assert that a failing
            // process captures error output and reports a non-zero exit code.
            Assert.That(instance.ErrorData, Is.Not.Empty);
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
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

        // Regression test for https://github.com/rosenbjerg/Instances/issues/10:
        // a deadlock when a process exits at almost the same moment its cancellation
        // token fires. ReceiveExit disposed the CancellationTokenRegistration while the
        // BCL held lock(process); the cancellation callback's HasExited needed that same
        // lock -> circular wait.
        //
        // The race window is tiny (single-digit microseconds per attempt), so this is a
        // volume-driven stress test: short-lived processes are raced against a cancellation
        // timed near their natural exit, in oversubscribed parallel batches, for a fixed
        // hunting budget. A WaitForExitAsync that never completes within perWaitTimeoutMs is
        // a reproduced deadlock and fails the test. On the fixed library no attempt ever
        // hangs, so the test runs the full budget and passes; on the buggy library it fails
        // (usually within the first second or two on affected hardware).
        [Test, CancelAfter(120000)]
        public async Task WaitForExitAsyncDoesNotDeadlockWhenCancellationRacesExit()
        {
            const int sleepMs = 40;
            const int reproBudgetMs = 20000;
            const int perWaitTimeoutMs = 4000;

            var averageLifetimeMs = await MeasureAverageLifetimeMs(sleepMs, samples: 8);
            // Fire cancellation across a window that ends just before the average exit, so
            // attempts where the process exits early collide with the cancellation callback.
            var minOffsetMs = Math.Max(1, (int)(averageLifetimeMs * 0.7));
            var maxOffsetMs = Math.Max(minOffsetMs + 1, averageLifetimeMs);
            var batchSize = Math.Max(4, Environment.ProcessorCount * 3);

            var budget = Stopwatch.StartNew();
            while (budget.ElapsedMilliseconds < reproBudgetMs)
            {
                var batch = Enumerable.Range(0, batchSize)
                    .Select(_ => RaceCancellationAgainstExit(sleepMs, minOffsetMs, maxOffsetMs, perWaitTimeoutMs))
                    .ToArray();
                var deadlocks = await Task.WhenAll(batch);
                if (deadlocks.Any(d => d))
                    Assert.Fail("WaitForExitAsync deadlocked when cancellation raced process exit (issue #10).");
            }
        }

        // Returns true if WaitForExitAsync failed to complete within timeoutMs (deadlock).
        private static async Task<bool> RaceCancellationAgainstExit(int sleepMs, int minOffsetMs, int maxOffsetMs, int timeoutMs)
        {
            var offset = Random.Shared.Next(minOffsetMs, maxOffsetMs + 1);
            var processArguments = GetShortLivedProcessArguments(sleepMs);

            var cts = new CancellationTokenSource();
            IProcessInstance instance;
            try
            {
                instance = processArguments.Start();
            }
            catch
            {
                cts.Dispose();
                return false;
            }

            cts.CancelAfter(offset);
            var waitTask = instance.WaitForExitAsync(cts.Token);

            var finished = await Task.WhenAny(waitTask, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (finished != waitTask)
            {
                // Deadlock: WaitForExitAsync never completed. Leak instance and cts on
                // purpose -- disposing either would block on the stuck callback.
                return true;
            }

            try { _ = await waitTask.ConfigureAwait(false); }
            catch { /* cancellation / already-exited races are expected and are not deadlocks */ }
            instance.Dispose();
            cts.Dispose();
            return false;
        }

        private static async Task<int> MeasureAverageLifetimeMs(int sleepMs, int samples)
        {
            long totalMs = 0;
            for (var i = 0; i < samples; i++)
            {
                var sw = Stopwatch.StartNew();
                using var instance = GetShortLivedProcessArguments(sleepMs).Start();
                await instance.WaitForExitAsync().ConfigureAwait(false);
                sw.Stop();
                totalMs += sw.ElapsedMilliseconds;
            }
            return (int)(totalMs / samples);
        }

        private static ProcessArguments GetShortLivedProcessArguments(int sleepMs)
        {
            var ms = Math.Max(1, sleepMs);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new ProcessArguments("powershell", $"-NoProfile -Command \"Start-Sleep -Milliseconds {ms}\"")
                {
                    IgnoreEmptyLines = true, DataBufferCapacity = 50
                };
            }
            var seconds = (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
            return new ProcessArguments("/bin/sleep", seconds)
            {
                IgnoreEmptyLines = true, DataBufferCapacity = 50
            };
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