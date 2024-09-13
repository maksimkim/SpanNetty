using System;
using System.Diagnostics;
using System.Reflection;
using Xunit.Sdk;

namespace DotNetty.Common.Tests.Internal.Logging
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class BeforeTest : BeforeAfterTestAttribute
    {
        public override void Before(MethodInfo methodUnderTest)
        {
            Trace.WriteLine($"Starting test '{methodUnderTest.ReturnType} {methodUnderTest.Name}'");
        }
    }
}