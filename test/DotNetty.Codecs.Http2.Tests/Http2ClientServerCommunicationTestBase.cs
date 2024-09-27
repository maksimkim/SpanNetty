using System.Net;
using System.Threading.Tasks;
using DotNetty.Tests.Common;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using Xunit.Abstractions;

namespace DotNetty.Codecs.Http2.Tests
{
    public abstract class Http2ClientServerCommunicationTestBase : TestBase
    {
        protected IChannel _serverChannel;
        protected IChannel _clientChannel;
        
        protected ServerBootstrap _sb;
        protected Bootstrap _cb;
        
        protected virtual int Port => 0;
        
        protected Http2ClientServerCommunicationTestBase(ITestOutputHelper output)
            : base(output)
        {
        }

        protected virtual async Task StartBootstrap()
        {
            Output.WriteLine($"[Debug] Starting server-channel-start {_serverChannel?.Id}");
            _serverChannel = await StartServerChannel();
            Output.WriteLine($"[Debug] Finished server-channel-start {_serverChannel.Id}. State: active={_serverChannel.IsActive};open={_serverChannel.IsOpen}");
            
            Output.WriteLine($"[Debug] Starting client-channel-start {_clientChannel?.Id}");
            _clientChannel = await StartClientChannel();
            Output.WriteLine($"[Debug] Finished client-channel-start {_clientChannel.Id}. State: active={_clientChannel.IsActive};open={_clientChannel.IsOpen}");
        }

        protected virtual async Task<IChannel> StartServerChannel()
        {
            _serverChannel = await _sb.BindAsync(IPAddress.IPv6Loopback, Port);
            return _serverChannel;
        }
        
        protected virtual async Task<IChannel> StartClientChannel()
        {
            var port = ((IPEndPoint)_serverChannel.LocalAddress).Port;
            _clientChannel = await _cb.ConnectAsync(IPAddress.IPv6Loopback, port);
            return _clientChannel;
        }
    }
}