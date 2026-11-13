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
        [Test, CancelAfter(10000)]
        public async Task WaitForExitAsyncCompletesWhenExitedHandlerThrows()
        {
            var processArguments = new ProcessArguments("dotnet", "--list-runtimes");
            processArguments.Exited += (_, _) => throw new InvalidOperationException("handler failure");

            using var instance = processArguments.Start();
            var result = await instance.WaitForExitAsync();

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
            IProcessResult exitedResult = null;
            processArguments.Exited += (_, result) => exitedResult = result;

            using var instance = processArguments.Start();
            using var cancel = new CancellationTokenSource(100);
            var result = await instance.WaitForExitAsync(cancel.Token);

            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(exitedResult, Is.SameAs(result));
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

            Assert.That(result1.ExitCode, Is.Not.EqualTo(0).And.Not.EqualTo(-100));
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

            var averageLifetimeMs = await MeasureAverageLifetimeMs(sleepMs, samples: 8);
            var perWaitTimeoutMs = Math.Max(4000, averageLifetimeMs * 10);
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
            var instance = processArguments.Start();

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
            return new ProcessArguments("sleep", seconds)
            {
                IgnoreEmptyLines = true, DataBufferCapacity = 50
            };
        }

        [Test, CancelAfter(30000)]
        public async Task OutputDataCanBeReadWhileTheProcessWrites()
        {
            using var instance = GetWaitingProcessArguments("lines", "100000").Start();
            var wait = instance.WaitForExitAsync();
            while (!wait.IsCompleted)
            {
                _ = instance.OutputData.Count;
                await Task.Delay(1);
            }

            var result = await wait;
            Assert.That(result.OutputData, Has.Count.EqualTo(100000));
        }

        [Test, CancelAfter(30000)]
        public void ExitedIsRaisedWhenStartAndWaitForExitDisposesTheInstance()
        {
            for (var i = 0; i < 20; i++)
            {
                var processArguments = GetWaitingProcessArguments("lines", "1");
                var exited = new TaskCompletionSource<IProcessResult>();
                processArguments.Exited += (_, result) => exited.TrySetResult(result);

                var result = processArguments.StartAndWaitForExit();

                Assert.That(exited.Task.Wait(5000), Is.True);
                Assert.That(exited.Task.Result, Is.SameAs(result));
            }
        }

        [Test, CancelAfter(10000)]
        public async Task ExitedHandlerCanUseItsInstance()
        {
            var processArguments = GetWaitingProcessArguments("lines", "1");
            var started = new TaskCompletionSource<IProcessInstance>();
            var exitCodes = new[] { -1, -1, -1 };
            processArguments.Exited += (_, _) =>
            {
                var self = started.Task.Result;
                exitCodes[0] = self.WaitForExit().ExitCode;
                exitCodes[1] = self.Kill().ExitCode;
                exitCodes[2] = self.WaitForExitAsync().GetAwaiter().GetResult().ExitCode;
            };

            using var instance = processArguments.Start();
            started.SetResult(instance);
            await instance.WaitForExitAsync();

            Assert.That(exitCodes, Is.EqualTo(new[] { 0, 0, 0 }));
        }

        [Test, CancelAfter(15000)]
        public async Task KillReturnsTheExitCodeOfTheKilledProcess()
        {
            using var instance = GetWaitingProcessArguments().Start();
            await Task.Delay(100);

            var result = instance.Kill();

            Assert.That(result.ExitCode, Is.Not.EqualTo(0).And.Not.EqualTo(-100));
        }

        [Test, CancelAfter(15000)]
        public async Task CancellationReturnsPromptlyWhenTheKilledTreeHeldTheOutputPipe()
        {
            var processArguments = GetWaitingProcessArguments("child", "20000", "wait");
            processArguments.KillEntireProcessTree = true;
            using var instance = processArguments.Start();
            using var cancel = new CancellationTokenSource(500);
            var stopwatch = Stopwatch.StartNew();

            var result = await instance.WaitForExitAsync(cancel.Token);

            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
        }

        [Test, CancelAfter(30000)]
        public async Task CancellationIsBoundedWhenAChildHoldsTheOutputPipe()
        {
            using var instance = GetWaitingProcessArguments("child", "20000", "wait").Start();
            using var cancel = new CancellationTokenSource(500);
            var stopwatch = Stopwatch.StartNew();

            var result = await instance.WaitForExitAsync(cancel.Token);

            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        [Test, CancelAfter(30000)]
        public async Task CancellationIsBoundedWhenAnOrphanedChildHoldsTheOutputPipe()
        {
            using var instance = GetWaitingProcessArguments("child", "20000").Start();
            using var cancel = new CancellationTokenSource(500);
            var stopwatch = Stopwatch.StartNew();

            await instance.WaitForExitAsync(cancel.Token);

            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        [Test, CancelAfter(10000)]
        public async Task CloseInputEndsAReaderOfStandardInput()
        {
            using var instance = GetWaitingProcessArguments("echo-stdin").Start();
            await instance.SendInputAsync("abc\n");
            instance.CloseInput();
            instance.CloseInput();

            var result = await instance.WaitForExitAsync();

            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.OutputData, Is.EqualTo(new[] { "abc" }));
            Assert.Throws<InvalidOperationException>(() => instance.SendInput("x"));
        }

        [Test, CancelAfter(10000)]
        public void ThrowingDataHandlerFaultsTheWait()
        {
            var processArguments = GetWaitingProcessArguments("lines", "3");
            var exited = false;
            processArguments.OutputDataReceived += (_, line) => throw new InvalidOperationException(line);
            processArguments.Exited += (_, _) => exited = true;

            using var instance = processArguments.Start();
            var exception = Assert.ThrowsAsync<AggregateException>(() => instance.WaitForExitAsync());

            Assert.That(exception!.InnerExceptions.Select(e => e.Message), Is.EqualTo(new[] { "1", "2", "3" }));
            Assert.That(instance.OutputData, Has.Count.EqualTo(3));
            Assert.That(exited, Is.True);
            Assert.Throws<AggregateException>(() => instance.WaitForExit());
        }

        [Test, CancelAfter(10000)]
        public void SingleDataHandlerExceptionIsRethrownAsIs()
        {
            var processArguments = GetWaitingProcessArguments("lines", "1");
            processArguments.OutputDataReceived += (_, line) => throw new InvalidOperationException(line);

            using var instance = processArguments.Start();

            Assert.That(() => instance.WaitForExit(), Throws.InvalidOperationException.With.Message.EqualTo("1"));
        }

        [Test]
        public void NegativeDataBufferCapacityIsRejected()
        {
            var processArguments = GetWaitingProcessArguments();

            Assert.Throws<ArgumentOutOfRangeException>(() => processArguments.DataBufferCapacity = -1);
        }

        [Test, CancelAfter(10000)]
        public void DisposedInstanceThrowsTheExceptionsOfVersion3()
        {
            var instance = GetWaitingProcessArguments("lines", "1").Start();
            instance.WaitForExit();
            instance.Dispose();

            Assert.That(() => instance.WaitForExit(), Throws.TypeOf<InstanceProcessAlreadyExitedException>().With.InnerException.TypeOf<ObjectDisposedException>());
            Assert.That(() => instance.Kill(), Throws.TypeOf<InstanceProcessAlreadyExitedException>().With.InnerException.TypeOf<ObjectDisposedException>());
            Assert.Throws<ObjectDisposedException>(() => instance.SendInput("x"));
            Assert.Throws<ObjectDisposedException>(() => instance.CloseInput());
            Assert.ThrowsAsync<ObjectDisposedException>(() => instance.WaitForExitAsync());
        }

        [OneTimeSetUp]
        public async Task Prepare()
        {
            await Instance.FinishAsync("dotnet", "publish ../../../../Instances.Tests.WaitingProgram -c Release -o ./waiting-program");
        }

        private static ProcessArguments GetWaitingProcessArguments(params string[] arguments)
        {
            return new ProcessArguments("dotnet", string.Join(" ", new[] { "./waiting-program/Instances.Tests.WaitingProgram.dll" }.Concat(arguments)));
        }
    }
}