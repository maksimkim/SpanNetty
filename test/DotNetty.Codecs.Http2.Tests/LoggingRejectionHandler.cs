using System.Net.Sockets;
using DotNetty.Common.Concurrency;
using Xunit.Abstractions;

namespace DotNetty.Codecs.Http2.Tests;

public class LoggingRejectionHandler : IRejectedExecutionHandler
{
    private readonly ITestOutputHelper _output;

    public LoggingRejectionHandler(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Rejected(IRunnable task, SingleThreadEventExecutor executor)
    {
        if (task is AbstractExecutorService.StateActionWithContextTaskQueueNode node)
        {
            var socketEventArgs = node.State as SocketAsyncEventArgs;
            _output.WriteLine($"Callback action scheduling rejected. Task type: {task.GetType()}, State type: {node.State?.GetType()}, Socket operation: {socketEventArgs?.LastOperation}, Socket error: {socketEventArgs?.SocketError}");
        }
                
        throw new RejectedExecutionException();
    }
}