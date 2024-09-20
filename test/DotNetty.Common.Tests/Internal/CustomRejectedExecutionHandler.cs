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
        
        public string TestName { get; set; }
        
        public CustomRejectedExecutionHandler(ITestOutputHelper testOutputHelper, string name)
        {
            _testOutputHelper = testOutputHelper;
            _name = name;
        }
        
        public void Rejected(IRunnable task, SingleThreadEventExecutor executor)
        {
            var rejectionMessage = GetErrorMessage(task, executor);
            
            _testOutputHelper.WriteLine(rejectionMessage);
            throw new CustomEventLoopTerminatedException(rejectionMessage);
        }

        string GetErrorMessage(IRunnable task, SingleThreadEventExecutor executor)
        {
            string runnable = "[Runnable] ";
            if (task is AbstractExecutorService.StateActionWithContextTaskQueueNode stateAction)
            {
                runnable += $"context: {stateAction.Context.GetType()}, state: {stateAction.State.GetType()}";
            }
            
            return $"[{TestName}] Rejected task from eventLoop '{_name}', id={executor.GetInnerThreadName()}, state='{executor.State}'. ExceptionCounter = {++_exceptionCounter}. {runnable}";
        }
    }

    public class CustomEventLoopTerminatedException : Exception
    {
        public CustomEventLoopTerminatedException(string message)
            : base(message)
        {
        }
    }
}