using System;
using DotNetty.Common.Concurrency;
using DotNetty.Transport.Channels.Sockets;
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
            if (task is AbstractExecutorService.StateActionWithContextTaskQueueNode action)
            {
                var context = action.Context as TcpServerSocketChannel<TcpServerSocketChannel, TcpSocketChannelFactory>.TcpServerSocketChannelUnsafe;
                var state = action.State as SocketChannelAsyncOperation<TcpServerSocketChannel, TcpServerSocketChannel<TcpServerSocketChannel, TcpSocketChannelFactory>.TcpServerSocketChannelUnsafe>;

                runnable += $"contextType (is null = {context is null}) = {action.Context.GetType()}; stateType (is null = {state is null}) = {action.State.GetType()}";

                if (context is not null)
                {
                    runnable += $"\ncontext: "
                                + $"\n\tchannel id: {context?.Channel?.Id}"
                                + $"\n\tlocal address: {context?.Channel?.LocalAddress}"
                                + $"\n\tremote address: {context?.Channel?.RemoteAddress}"
                                + $"\n\tisActive: {context?.Channel?.IsActive}; isOpen: {context?.Channel?.IsOpen}; isRegistered: {context?.Channel?.IsRegistered}; isWritable: {context?.Channel?.IsWritable}"
                                + $"\n\toutboundBuffer: {context?.OutboundBuffer?.Size}"
                        ;
                }
                if (state is not null)
                {
                    runnable += "\nstate: "
                                + $"\n\toperation: {state?.LastOperation}; socketFlags: {state?.SocketFlags}; error: {state?.SocketError};"
                                + $"\n\tsocket connected: {state?.AcceptSocket?.Connected}; type: {state?.AcceptSocket?.SocketType};"
                                + $"\n\tlocal address: {state?.AcceptSocket?.LocalEndPoint}"
                                + $"\n\tremote address: {state?.AcceptSocket?.RemoteEndPoint}"
                                + $"\n\tconnectByNameError: {state?.ConnectByNameError}"
                        ;
                }
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