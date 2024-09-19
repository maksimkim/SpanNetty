﻿using DotNetty.Common.Tests.Internal;
using DotNetty.Common.Tests.Internal.Logging;

namespace DotNetty.Codecs.Http2.Tests
{
    using System;
    using System.IO;
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Buffers;
    using Http;
    using Common.Concurrency;
    using Common.Utilities;
    using Handlers.Tls;
    using DotNetty.Tests.Common;
    using Transport.Bootstrapping;
    using Transport.Channels;
    using Transport.Channels.Sockets;
    using Transport.Libuv;
    using Moq;
    using Xunit;
    using Xunit.Abstractions;

    //public sealed class TlsLibuvDataCompressionHttp2Test : LibuvDataCompressionHttp2Test
    //{
    //    public TlsLibuvDataCompressionHttp2Test(ITestOutputHelper output) : base(output) { }

    //    protected override void SetInitialServerChannelPipeline(IChannel ch)
    //    {
    //        ch.Pipeline.AddLast(this.CreateTlsHandler(false));
    //        base.SetInitialServerChannelPipeline(ch);
    //    }

    //    protected override void SetInitialChannelPipeline(IChannel ch)
    //    {
    //        ch.Pipeline.AddLast("tls", this.CreateTlsHandler(true));
    //        base.SetInitialChannelPipeline(ch);
    //    }
    //}

    public class LibuvDataCompressionHttp2Test : AbstractDataCompressionHttp2Test
    {
        static LibuvDataCompressionHttp2Test()
        {
            Common.ResourceLeakDetector.Level = Common.ResourceLeakDetector.DetectionLevel.Disabled;
        }

        public LibuvDataCompressionHttp2Test(ITestOutputHelper output) : base(output) { }

        protected override void SetupServerBootstrap(ServerBootstrap bootstrap, string testName)
        {
            var dispatcher = new DispatcherEventLoopGroup();
            var bossGroup = dispatcher;
            var workGroup = new WorkerEventLoopGroup(dispatcher);
            bootstrap.Group(bossGroup, workGroup)
                     .Channel<TcpServerChannel>();
        }

        protected override void SetupBootstrap(Bootstrap bootstrap, string testName)
        {
            bootstrap.Group(new EventLoopGroup()).Channel<TcpChannel>();
        }
    }

    //public sealed class TlsSocketDataCompressionHttp2Test : SocketDataCompressionHttp2Test
    //{
    //    public TlsSocketDataCompressionHttp2Test(ITestOutputHelper output) : base(output) { }

    //    protected override void SetInitialServerChannelPipeline(IChannel ch)
    //    {
    //        ch.Pipeline.AddLast(this.CreateTlsHandler(false));
    //        base.SetInitialServerChannelPipeline(ch);
    //    }

    //    protected override void SetInitialChannelPipeline(IChannel ch)
    //    {
    //        ch.Pipeline.AddLast("tls", this.CreateTlsHandler(true));
    //        base.SetInitialChannelPipeline(ch);
    //    }
    //}

    public class SocketDataCompressionHttp2Test : AbstractDataCompressionHttp2Test
    {
        private readonly CustomRejectedExecutionHandler _parentLoopRejectionExecutionHandler;
        private readonly CustomRejectedExecutionHandler _childLoopRejectionExecutionHandler;
        
        private readonly CustomRejectedExecutionHandler _clientLoopRejectionExecutionHandler;
        
        public SocketDataCompressionHttp2Test(ITestOutputHelper output) : base(output)
        {
            _parentLoopRejectionExecutionHandler = new CustomRejectedExecutionHandler(output, "parent");
            _childLoopRejectionExecutionHandler = new CustomRejectedExecutionHandler(output, "child");
            
            _clientLoopRejectionExecutionHandler = new CustomRejectedExecutionHandler(output, "client");
        }

        protected override void SetupServerBootstrap(ServerBootstrap bootstrap, string testName)
        {
            SetRejectionHandlerTestName(testName);
            
            bootstrap
                .Group(
                    parentGroup: new MultithreadEventLoopGroup(1, _parentLoopRejectionExecutionHandler), 
                    childGroup: new MultithreadEventLoopGroup(_childLoopRejectionExecutionHandler))
                .Channel<TcpServerSocketChannel>();
        }

        protected override void SetupBootstrap(Bootstrap bootstrap, string testName)
        {
            SetRejectionHandlerTestName(testName);
            
            bootstrap
                .Group(new MultithreadEventLoopGroup(_clientLoopRejectionExecutionHandler))
                .Channel<TcpSocketChannel>();
        }

        void SetRejectionHandlerTestName(string testName)
        {
            _parentLoopRejectionExecutionHandler.TestName = testName;
            _childLoopRejectionExecutionHandler.TestName = testName;
            _clientLoopRejectionExecutionHandler.TestName = testName;
        }
    }

    // [Collection("BootstrapEnv")] // no collection means re-setup for every test
    public abstract class AbstractDataCompressionHttp2Test : TestBase, IDisposable
    {
        private static readonly AsciiString GET = new AsciiString("GET");
        private static readonly AsciiString POST = new AsciiString("POST");
        private static readonly AsciiString PATH = new AsciiString("/some/path");

        private Mock<IHttp2FrameListener> serverListener;
        private Mock<IHttp2FrameListener> clientListener;

        private IHttp2ConnectionEncoder clientEncoder;
        private ServerBootstrap sb;
        private Bootstrap cb;
        private IChannel serverChannel;
        private IChannel clientChannel;
        private volatile IChannel serverConnectedChannel;
        private CountdownEvent serverLatch;
        private IHttp2Connection serverConnection;
        private IHttp2Connection clientConnection;
        private Http2ConnectionHandler clientHandler;
        private MemoryStream serverOut;

        public AbstractDataCompressionHttp2Test(ITestOutputHelper output)
            : base(output)
        {
            serverListener = new Mock<IHttp2FrameListener>();
            clientListener = new Mock<IHttp2FrameListener>();

            serverListener
                .Setup(x => x.OnHeadersRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.IsAny<int>(),
                    It.IsAny<IHttp2Headers>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>()))
                .Callback<IChannelHandlerContext, int, IHttp2Headers, int, bool>((ctx, id, h, p, endOfStream) =>
                {
                    if (endOfStream)
                    {
                        serverConnection.Stream(id).Close();
                    }
                });
            serverListener
                .Setup(x => x.OnHeadersRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.IsAny<int>(),
                    It.IsAny<IHttp2Headers>(),
                    It.IsAny<int>(),
                    It.IsAny<short>(),
                    It.IsAny<bool>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>()))
                .Callback<IChannelHandlerContext, int, IHttp2Headers, int, short, bool, int, bool>((ctx, id, h, sd, w, e, p, endOfStream) =>
                {
                    if (endOfStream)
                    {
                        serverConnection.Stream(id).Close();
                    }
                });
        }

        public void Dispose()
        {
            try
            {
                Output.WriteLine($"StartingDispose of {GetType().FullName}");
                
                if (serverChannel != null)
                {
                    serverChannel.CloseAsync().GetAwaiter().GetResult();
                    serverChannel = null;
                }
                
                if (clientChannel != null)
                {
                    clientChannel.CloseAsync().GetAwaiter().GetResult();
                    clientChannel = null;
                }

                var serverConnectedChannel = this.serverConnectedChannel;
                if (serverConnectedChannel != null)
                {
                    serverConnectedChannel.CloseAsync().GetAwaiter().GetResult();
                    this.serverConnectedChannel = null;
                }

                try
                {
                    // closing one by one in the same order that channels are closed
                    sb.Group().ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).GetAwaiter().GetResult();
                    sb.ChildGroup().ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).GetAwaiter().GetResult();
                    cb.Group().ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Output.WriteLine($"FailedShutdown of {GetType().FullName}: " + ex);
                }

                serverOut?.Close();
                Output.WriteLine($"FinishedDispose of {GetType().FullName}");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"FailedDispose of {GetType().FullName}: " + ex);
            }
        }

        [Fact]
        [BeforeTest]
        public async Task JustHeadersNoData()
        {
            await BootstrapEnv(0, nameof(JustHeadersNoData));
            IHttp2Headers headers = new DefaultHttp2Headers() { Method = GET, Path = PATH };
            headers.Set(HttpHeaderNames.ContentEncoding, HttpHeaderValues.Gzip);

            Http2TestUtil.RunInChannel(clientChannel, () =>
            {
                clientEncoder.WriteHeadersAsync(CtxClient(), 3, headers, 0, true, NewPromiseClient());
                clientHandler.Flush(CtxClient());
            });
            AwaitServer();
            serverListener.Verify(
                x => x.OnHeadersRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.Is<IHttp2Headers>(v => v.Equals(headers)),
                    It.Is<int>(v => v == 0),
                    It.Is<short>(v => v == Http2CodecUtil.DefaultPriorityWeight),
                    It.Is<bool>(v => v == false),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == true)));
        }

        [Fact]
        [BeforeTest]
        public async Task GzipEncodingSingleEmptyMessage()
        {
            string text = "";
            var data = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text));
            await BootstrapEnv(data.ReadableBytes, nameof(GzipEncodingSingleEmptyMessage));
            try
            {
                IHttp2Headers headers = new DefaultHttp2Headers() { Method = POST, Path = PATH };
                headers.Set(HttpHeaderNames.ContentEncoding, HttpHeaderValues.Gzip);

                Http2TestUtil.RunInChannel(clientChannel, () =>
                {
                    clientEncoder.WriteHeadersAsync(CtxClient(), 3, headers, 0, false, NewPromiseClient());
                    clientEncoder.WriteDataAsync(CtxClient(), 3, (IByteBuffer)data.Retain(), 0, true, NewPromiseClient());
                    clientHandler.Flush(CtxClient());
                });
                AwaitServer();
                Assert.Equal(text, Encoding.UTF8.GetString(serverOut.ToArray()));
            }
            finally
            {
                data.Release();
            }
        }

        [Fact]
        [BeforeTest]
        public async Task GzipEncodingSingleMessage()
        {
            string text = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbbbbbbbbbbbbbbccccccccccccccccccccccc";
            var data = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text));
            await BootstrapEnv(data.ReadableBytes, nameof(GzipEncodingSingleMessage));
            try
            {
                IHttp2Headers headers = new DefaultHttp2Headers() { Method = POST, Path = PATH };
                headers.Set(HttpHeaderNames.ContentEncoding, HttpHeaderValues.Gzip);

                Http2TestUtil.RunInChannel(clientChannel, () =>
                {
                    clientEncoder.WriteHeadersAsync(CtxClient(), 3, headers, 0, false, NewPromiseClient());
                    clientEncoder.WriteDataAsync(CtxClient(), 3, (IByteBuffer)data.Retain(), 0, true, NewPromiseClient());
                    clientHandler.Flush(CtxClient());
                });
                AwaitServer();
                Assert.Equal(text, Encoding.UTF8.GetString(serverOut.ToArray()));
            }
            finally
            {
                data.Release();
            }
        }

        [Fact]
        [BeforeTest]
        public async Task GzipEncodingMultipleMessages()
        {
            string text1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbbbbbbbbbbbbbbccccccccccccccccccccccc";
            string text2 = "dddddddddddddddddddeeeeeeeeeeeeeeeeeeeffffffffffffffffffff";
            var data1 = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text1));
            var data2 = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text2));
            await BootstrapEnv(data1.ReadableBytes + data2.ReadableBytes, nameof(GzipEncodingMultipleMessages));
            try
            {
                IHttp2Headers headers = new DefaultHttp2Headers() { Method = POST, Path = PATH };
                headers.Set(HttpHeaderNames.ContentEncoding, HttpHeaderValues.Gzip);

                Http2TestUtil.RunInChannel(clientChannel, () =>
                {
                    clientEncoder.WriteHeadersAsync(CtxClient(), 3, headers, 0, false, NewPromiseClient());
                    clientEncoder.WriteDataAsync(CtxClient(), 3, (IByteBuffer)data1.Retain(), 0, false, NewPromiseClient());
                    clientEncoder.WriteDataAsync(CtxClient(), 3, (IByteBuffer)data2.Retain(), 0, true, NewPromiseClient());
                    clientHandler.Flush(CtxClient());
                });
                AwaitServer();
                Assert.Equal(text1 + text2, Encoding.UTF8.GetString(serverOut.ToArray()));
            }
            finally
            {
                data1.Release();
                data2.Release();
            }
        }

        [Fact]
        [BeforeTest]
        public async Task DeflateEncodingWriteLargeMessage()
        {
            int BUFFER_SIZE = 1 << 12;
            byte[] bytes = new byte[BUFFER_SIZE];
            new Random().NextBytes(bytes);
            await BootstrapEnv(BUFFER_SIZE, nameof(DeflateEncodingWriteLargeMessage));
            var data = Unpooled.WrappedBuffer(bytes);
            try
            {
                IHttp2Headers headers = new DefaultHttp2Headers() { Method = POST, Path = PATH };
                headers.Set(HttpHeaderNames.ContentEncoding, HttpHeaderValues.Gzip);

                Http2TestUtil.RunInChannel(clientChannel, () =>
                {
                    clientEncoder.WriteHeadersAsync(CtxClient(), 3, headers, 0, false, NewPromiseClient());
                    clientEncoder.WriteDataAsync(CtxClient(), 3, (IByteBuffer)data.Retain(), 0, true, NewPromiseClient());
                    clientHandler.Flush(CtxClient());
                });
                AwaitServer();
                Assert.Equal(data.ResetReaderIndex().ToString(Encoding.UTF8), Encoding.UTF8.GetString(serverOut.ToArray()));
            }
            finally
            {
                data.Release();
            }
        }

        protected abstract void SetupServerBootstrap(ServerBootstrap bootstrap, string testName);

        protected abstract void SetupBootstrap(Bootstrap bootstrap, string testName);

        protected virtual void SetInitialServerChannelPipeline(IChannel ch)
        {
            serverConnectedChannel = ch;
            var p = ch.Pipeline;
            IHttp2FrameWriter frameWriter = new DefaultHttp2FrameWriter();
            serverConnection.Remote.FlowController = new DefaultHttp2RemoteFlowController(serverConnection);
            serverConnection.Local.FlowController = new DefaultHttp2LocalFlowController(serverConnection).FrameWriter(frameWriter);
            IHttp2ConnectionEncoder encoder = new CompressorHttp2ConnectionEncoder(
                    new DefaultHttp2ConnectionEncoder(serverConnection, frameWriter));
            IHttp2ConnectionDecoder decoder =
                    new DefaultHttp2ConnectionDecoder(serverConnection, encoder, new DefaultHttp2FrameReader());
            Http2ConnectionHandler connectionHandler = new Http2ConnectionHandlerBuilder()
            {
                FrameListener = new DelegatingDecompressorFrameListener(serverConnection, serverListener.Object)
            }
                .Codec(decoder, encoder).Build();
            p.AddLast(connectionHandler);
        }

        protected virtual void SetInitialChannelPipeline(IChannel ch)
        {
            var p = ch.Pipeline;
            IHttp2FrameWriter frameWriter = new DefaultHttp2FrameWriter();
            clientConnection.Remote.FlowController = new DefaultHttp2RemoteFlowController(clientConnection);
            clientConnection.Local.FlowController = new DefaultHttp2LocalFlowController(clientConnection).FrameWriter(frameWriter);
            clientEncoder = new CompressorHttp2ConnectionEncoder(
                    new DefaultHttp2ConnectionEncoder(clientConnection, frameWriter));

            IHttp2ConnectionDecoder decoder =
                    new DefaultHttp2ConnectionDecoder(clientConnection, clientEncoder,
                            new DefaultHttp2FrameReader());
            clientHandler = new Http2ConnectionHandlerBuilder()
            {
                FrameListener = new DelegatingDecompressorFrameListener(clientConnection, clientListener.Object),
                // By default tests don't wait for server to gracefully shutdown streams
                GracefulShutdownTimeout = TimeSpan.Zero
            }
                .Codec(decoder, clientEncoder).Build();
            p.AddLast(clientHandler);
        }

        protected TlsHandler CreateTlsHandler(bool isClient)
        {
            X509Certificate2 tlsCertificate = TestResourceHelper.GetTestCertificate();
            string targetHost = tlsCertificate.GetNameInfo(X509NameType.DnsName, false);
            TlsHandler tlsHandler = isClient ?
                new TlsHandler(new ClientTlsSettings(targetHost).AllowAnyServerCertificate()) :
                new TlsHandler(new ServerTlsSettings(tlsCertificate).AllowAnyClientCertificate());
            return tlsHandler;
        }

        protected virtual int Port => 0;

        private async Task BootstrapEnv(int serverOutSize, string testName)
        {
            var prefaceWrittenLatch = new CountdownEvent(1);
            serverOut = new MemoryStream(serverOutSize);
            serverLatch = new CountdownEvent(1);
            sb = new ServerBootstrap();
            cb = new Bootstrap();

            // Streams are created before the normal flow for this test, so these connection must be initialized up front.
            serverConnection = new DefaultHttp2Connection(true);
            clientConnection = new DefaultHttp2Connection(false);

            serverConnection.AddListener(new TestHttp2ConnectionAdapter(serverLatch));

            serverListener
                .Setup(x => x.OnDataRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.IsAny<int>(),
                    It.IsAny<IByteBuffer>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>()))
                .Returns<IChannelHandlerContext, int, IByteBuffer, int, bool>((ctx, id, buf, padding, end) =>
                {
                    int processedBytes = buf.ReadableBytes + padding;

                    buf.ReadBytes(serverOut, buf.ReadableBytes);
                    if (end)
                    {
                        serverConnection.Stream(id).Close();
                    }
                    return processedBytes;
                });
            var serverChannelLatch = new CountdownEvent(1);

            SetupServerBootstrap(sb, testName);
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                SetInitialServerChannelPipeline(ch);
                serverChannelLatch.SafeSignal();
            }));

            SetupBootstrap(cb, testName);
            cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                SetInitialChannelPipeline(ch);
                ch.Pipeline.AddLast(new TestChannelHandlerAdapter(prefaceWrittenLatch));
            }));

            var loopback = IPAddress.IPv6Loopback;
            serverChannel = await sb.BindAsync(loopback, Port);

            var port = ((IPEndPoint)serverChannel.LocalAddress).Port;
            var ccf = cb.ConnectAsync(loopback, port);
            clientChannel = await ccf;
            Assert.True(prefaceWrittenLatch.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(serverChannelLatch.Wait(TimeSpan.FromSeconds(10)));
        }

        sealed class TestChannelHandlerAdapter : ChannelHandlerAdapter
        {
            readonly CountdownEvent prefaceWrittenLatch;

            public TestChannelHandlerAdapter(CountdownEvent countdown) => prefaceWrittenLatch = countdown;

            public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
            {
                if (ReferenceEquals(evt, Http2ConnectionPrefaceAndSettingsFrameWrittenEvent.Instance))
                {
                    prefaceWrittenLatch.SafeSignal();
                    ctx.Pipeline.Remove(this);
                }
            }
        }

        class TestHttp2ConnectionAdapter : Http2ConnectionAdapter
        {
            readonly CountdownEvent serverLatch;

            public TestHttp2ConnectionAdapter(CountdownEvent serverLatch)
            {
                this.serverLatch = serverLatch;
            }

            public override void OnStreamClosed(IHttp2Stream stream)
            {
                serverLatch.SafeSignal();
            }
        }

        private void AwaitServer()
        {
            Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(5)));
            serverOut.Flush();
        }

        private IChannelHandlerContext CtxClient()
        {
            return clientChannel.Pipeline.FirstContext();
        }

        private IPromise NewPromiseClient()
        {
            return CtxClient().NewPromise();
        }
    }
}
