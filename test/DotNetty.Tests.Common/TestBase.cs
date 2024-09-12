// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace DotNetty.Tests.Common
{
  using DotNetty.Common.Internal.Logging;
  using Xunit.Abstractions;

  public abstract class TestBase
  {
    protected readonly ITestOutputHelper Output;

    protected TestBase(ITestOutputHelper output)
    {
      this.Output = output;
      InternalLoggerFactory.DefaultFactory.AddProvider(new XUnitOutputLoggerProvider(output));
      System.Diagnostics.Trace.Listeners.Add(new XUnitTraceListener(output));
    }
  }
  
  class XUnitTraceListener : TraceListener
  {
    readonly ITestOutputHelper _output;

    public XUnitTraceListener(ITestOutputHelper outputHelper)
    {
      _output = outputHelper;
    }

    public override void Write(string message)
    {
      _output.WriteLine(message);
    }

    public override void WriteLine(string message)
    {
      _output.WriteLine(message);
    }
  }
}