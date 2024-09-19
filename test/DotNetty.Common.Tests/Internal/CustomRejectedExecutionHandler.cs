using System;
using DotNetty.Common.Concurrency;
using Xunit.Abstractions;

namespace DotNetty.Common.Tests.Internal
{
    public class CustomRejectedExecutionHandler : IRejectedExecutionHandler
    {
        private int _exceptionCounter = 1;

        private readonly ITestOutputHelper _testOutputHelper;
        private readonly string _name;
        
        public CustomRejectedExecutionHandler(ITestOutputHelper testOutputHelper, string name)
        {
            _testOutputHelper = testOutputHelper;
            _name = name;
        }
        
        public void Rejected(IRunnable task, SingleThreadEventExecutor executor)
        {
            _testOutputHelper.WriteLine($"Rejected task from eventLoop '{_name}', id={executor.GetInnerThreadName()}, state='{executor.State}'. ExceptionCounter = {_exceptionCounter}");
            throw new CustomEventLoopTerminatedException(_exceptionCounter++);
        }
    }

    public class CustomEventLoopTerminatedException : Exception
    {
        public CustomEventLoopTerminatedException(int exceptionCounter)
            : this($"{nameof(SingleThreadEventExecutor)} terminated", exceptionCounter)
        {
        }
        
        public CustomEventLoopTerminatedException(string message, int? exceptionCounter = null)
            : base($"exceptionCounter: {exceptionCounter}; " + message)
        {
        }
    }
}