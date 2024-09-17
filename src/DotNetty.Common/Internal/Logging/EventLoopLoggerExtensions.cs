﻿using System.Runtime.CompilerServices;
using DotNetty.Common.Concurrency;

namespace DotNetty.Common.Internal.Logging
{
    public static class EventLoopLoggerExtensions
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ExecutionStateChange(this IInternalLogger logger, XThread thread, int oldState, int newState, string location = "") 
        {
            logger.Debug($"[{location}] Loop {thread.Name}; oldState: {GetState(oldState)}; newState: {GetState(newState)}");
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