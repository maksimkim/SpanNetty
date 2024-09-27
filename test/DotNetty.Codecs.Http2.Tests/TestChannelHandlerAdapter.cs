using System.Threading;
using DotNetty.Transport.Channels;
using Xunit.Abstractions;

namespace DotNetty.Codecs.Http2.Tests
{
    internal sealed class TestChannelHandlerAdapter : ChannelHandlerAdapter
    {
        readonly ITestOutputHelper _output;
        readonly CountdownEvent _prefaceWrittenLatch;

        public TestChannelHandlerAdapter(ITestOutputHelper output, CountdownEvent countdown)
        {
            _output = output;
            _prefaceWrittenLatch = countdown;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            _output.WriteLine($"[Debug] Signalling user event triggered latch for channel {ctx.Channel.Id}, state active = {ctx.Channel.IsActive}");
                
            if (ReferenceEquals(evt, Http2ConnectionPrefaceAndSettingsFrameWrittenEvent.Instance))
            {
                _prefaceWrittenLatch.SafeSignal();
                ctx.Pipeline.Remove(this);
            }
        }
    }
}