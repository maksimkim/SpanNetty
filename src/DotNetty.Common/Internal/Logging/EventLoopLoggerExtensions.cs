using System.Runtime.CompilerServices;
using Thread = DotNetty.Common.Concurrency.XThread;

namespace DotNetty.Common.Internal.Logging
{
    public static class EventLoopLoggerExtensions
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ExecutionStateChange(this IInternalLogger logger, Thread thread, int oldState, int newState) 
        {
            logger.Debug($"thread: {thread.Id}; oldState: {GetState(oldState)}; newState: {GetState(newState)}");
        }

        private static string GetState(int state) => state switch
        {
            1 => "NotStartedState",
            2 => "StartedState",
            3 => "ShuttingDownState",
            4 => "ShutdownState",
            5 => "TerminatedState",
        };
    }
}