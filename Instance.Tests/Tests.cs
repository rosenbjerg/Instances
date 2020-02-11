using System;
using System.Linq;
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
            var instance = new Instances.Instance("dotnet", "run --project Nopes");
            var completionSource = new TaskCompletionSource<bool>();
            instance.Exited += (sender, args) => completionSource.TrySetResult(true);

            instance.Started = true;
            var result = completionSource.Task.Result;
            
            Assert.IsTrue(result);
        }
        [Test]
        public void PublishesExitedEventOnSuccess()
        {
            var instance = new Instances.Instance("dotnet", "--list-runtimes");
            var completionSource = new TaskCompletionSource<bool>();
            instance.Exited += (sender, args) => completionSource.TrySetResult(true);

            instance.Started = true;
            var result = completionSource.Task.Result;
            
            Assert.IsTrue(result);
        }
        [Test]
        public void PublishesErrorEvents()
        {
            var instance = new Instances.Instance("dotnet", "run --project Nopes");
            var dataReceived = false;
            instance.DataReceived += (sender, args) => dataReceived = args.Type == DataType.Error;
            
            instance.FinishedRunning().Wait();
            
            Assert.IsTrue(dataReceived);
        }
        [Test]
        public void PublishesDataEvents()
        {
            var instance = new Instances.Instance("dotnet", "--list-runtimes");
            var dataReceived = false;
            instance.DataReceived += (sender, args) => dataReceived = args.Type == DataType.Output;
            
            instance.FinishedRunning().Wait();
            
            Assert.IsTrue(dataReceived);
        }
        [Test]
        public void SecondErrorTest()
        {
            var instance = new Instances.Instance("dotnet", "run --project Nopes");
            
            instance.FinishedRunning().Wait();
            
            Assert.IsTrue(instance.ErrorData.First() == "The build failed. Fix the build errors and run again.");
        }
        [Test]
        public void BasicErrorTest()
        {
            var instance = new Instances.Instance("dotnet", "run --project Nopes");
            
            instance.FinishedRunning().Wait();
            
            Assert.IsNotEmpty(string.Join("\n", instance.ErrorData));
        }
        [Test]
        public void SecondOutputTest()
        {
            var instance = new Instances.Instance("dotnet", "--info");
            
            instance.FinishedRunning().Wait();
            
            Assert.IsTrue(instance.OutputData.First().StartsWith(".NET Core"));
        }
        [Test]
        public void BasicOutputTest()
        {
            var instance = new Instances.Instance("dotnet", "--version");
            
            instance.FinishedRunning().Wait();
            
            Assert.IsNotEmpty(string.Join("\n", instance.OutputData));
        }
    }
}