using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Xunit.Sdk;

namespace DotNetty.Common.Tests.Internal.Logging
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class BeforeTest : BeforeAfterTestAttribute
    {
        public override void Before(MethodInfo methodUnderTest)
        {
            var stackTrace = new StackTrace(fNeedFileInfo: true);
            var frames = stackTrace.GetFrames()?.Take(150)
                .Where(x => x is not null)
                .Select(x => $"{x.GetMethod()} {x.GetFileName()} at {x.GetFileLineNumber()}:{x.GetFileColumnNumber()}\n");
            var stackTraceStr = frames is not null ? string.Join("", frames) : "";
            Trace.WriteLine($"Starting test '{methodUnderTest.ReturnType} {methodUnderTest.Name}'; trace: {stackTraceStr}");
        }
    }
}