
namespace DotNetty.Codecs.Http2.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Security;
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
    using Transport.Channels.Local;
    using Transport.Channels.Sockets;
    using Transport.Libuv;
    using Moq;
    using Xunit;
    using Xunit.Abstractions;

    public sealed class LibuvHttpToHttp2ConnectionHandlerTest : AbstractHttpToHttp2ConnectionHandlerTest
    {
        static LibuvHttpToHttp2ConnectionHandlerTest()
        {
            Common.ResourceLeakDetector.Level = Common.ResourceLeakDetector.DetectionLevel.Disabled;
        }

        public LibuvHttpToHttp2ConnectionHandlerTest(ITestOutputHelper output) : base(output) { }

        protected override void SetupServerBootstrap(ServerBootstrap bootstrap)
        {
            var dispatcher = new DispatcherEventLoopGroup();
            var bossGroup = dispatcher;
            var workGroup = new WorkerEventLoopGroup(dispatcher);
            bootstrap.Group(bossGroup, workGroup)
                     .Channel<TcpServerChannel>();
            //bootstrap.Handler(new DotNetty.Handlers.Logging.LoggingHandler("LSTN"));
        }

        protected override void SetupBootstrap(Bootstrap bootstrap)
        {
            bootstrap.Group(new EventLoopGroup()).Channel<TcpChannel>();
        }
    }

    public sealed class SocketHttpToHttp2ConnectionHandlerTest : AbstractHttpToHttp2ConnectionHandlerTest
    {
        public SocketHttpToHttp2ConnectionHandlerTest(ITestOutputHelper output) : base(output) { }

        protected override void SetupServerBootstrap(ServerBootstrap bootstrap)
        {
            bootstrap.Group(new MultithreadEventLoopGroup(1), new MultithreadEventLoopGroup())
                     .Channel<TcpServerSocketChannel>();
        }

        protected override void SetupBootstrap(Bootstrap bootstrap)
        {
            bootstrap.Group(new MultithreadEventLoopGroup()).Channel<TcpSocketChannel>();
        }
    }

    public sealed class LocalHttpToHttp2ConnectionHandlerTest : AbstractHttpToHttp2ConnectionHandlerTest
    {
        public LocalHttpToHttp2ConnectionHandlerTest(ITestOutputHelper output) : base(output) { }

        protected override void SetupServerBootstrap(ServerBootstrap bootstrap)
        {
            bootstrap.Group(new DefaultEventLoopGroup(1), new DefaultEventLoopGroup())
                     .Channel<LocalServerChannel>();
        }

        protected override void SetupBootstrap(Bootstrap bootstrap)
        {
            bootstrap.Group(new DefaultEventLoopGroup()).Channel<LocalChannel>();
        }

        protected override async Task StartBootstrap()
        {
            _serverChannel = await _sb.BindAsync(new LocalAddress("HttpToHttp2ConnectionHandlerTest"));
            _clientChannel = await _cb.ConnectAsync(_serverChannel.LocalAddress);
        }
    }

    /**
     * Testing the {@link HttpToHttp2ConnectionHandler} for {@link IFullHttpRequest} objects into HTTP/2 frames
     */
    [Collection("BootstrapEnv")]
    public abstract class AbstractHttpToHttp2ConnectionHandlerTest : Http2ClientServerCommunicationTestBase, IDisposable
    {
        private const int WAIT_TIME_SECONDS = 5;

        private Mock<IHttp2FrameListener> _clientListener;
        private Mock<IHttp2FrameListener> _serverListener;
        
        private volatile IChannel _serverConnectedChannel;
        private CountdownEvent _requestLatch;
        private CountdownEvent _serverSettingsAckLatch;
        private CountdownEvent _trailersLatch;
        private Http2TestUtil.FrameCountDown _serverFrameCountDown;

        public AbstractHttpToHttp2ConnectionHandlerTest(ITestOutputHelper output)
            : base(output)
        {
            _clientListener = new Mock<IHttp2FrameListener>();
            _serverListener = new Mock<IHttp2FrameListener>();
        }

        public void Dispose()
        {
            if (_clientChannel != null)
            {
                _clientChannel.CloseAsync().GetAwaiter().GetResult();
                _clientChannel = null;
            }
            if (_serverChannel != null)
            {
                _serverChannel.CloseAsync().GetAwaiter().GetResult();
                _serverChannel = null;
            }
            var serverConnectedChannel = _serverConnectedChannel;
            if (serverConnectedChannel != null && serverConnectedChannel.IsActive)
            {
                serverConnectedChannel.CloseAsync().GetAwaiter().GetResult();
                _serverConnectedChannel = null;
            }
            try
            {
                Task.WaitAll(
                    _sb.Group().ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5)),
                    _sb.ChildGroup().ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5)),
                    _cb.Group().ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5)));
            }
            catch
            {
                // Ignore RejectedExecutionException(on Azure DevOps)
            }
        }

        [Fact]
        public void HeadersOnlyRequest()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Get,
                "http://my-user_name@www.example.org:5555/example");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpHeaderNames.Host, "my-user_name@www.example.org:5555");
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Scheme, "http");
            httpHeaders.Add(AsciiString.Of("foo"), AsciiString.Of("goo"));
            httpHeaders.Add(AsciiString.Of("foo"), AsciiString.Of("goo2"));
            httpHeaders.Add(AsciiString.Of("foo2"), AsciiString.Of("goo2"));
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("GET"),
                Path = new AsciiString("/example"),
                Authority = new AsciiString("www.example.org:5555"),
                Scheme = new AsciiString("http")
            };
            http2Headers.Add(new AsciiString("foo"), new AsciiString("goo"));
            http2Headers.Add(new AsciiString("foo"), new AsciiString("goo2"));
            http2Headers.Add(new AsciiString("foo2"), new AsciiString("goo2"));

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void MultipleCookieEntriesAreCombined()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Get,
                "http://my-user_name@www.example.org:5555/example");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpHeaderNames.Host, "my-user_name@www.example.org:5555");
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Scheme, "http");
            httpHeaders.Set(HttpHeaderNames.Cookie, "a=b; c=d; e=f");
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("GET"),
                Path = new AsciiString("/example"),
                Authority = new AsciiString("www.example.org:5555"),
                Scheme = new AsciiString("http")
            };
            http2Headers.Add(HttpHeaderNames.Cookie, new AsciiString("a=b"));
            http2Headers.Add(HttpHeaderNames.Cookie, new AsciiString("c=d"));
            http2Headers.Add(HttpHeaderNames.Cookie, new AsciiString("e=f"));

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }



        [Fact]
        public void OriginFormRequestTargetHandled()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Get, "/where?q=now&f=then#section1");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Scheme, "http");
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("GET"),
                Path = new AsciiString("/where?q=now&f=then#section1"),
                Scheme = new AsciiString("http")
            };

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void OriginFormRequestTargetHandledFromUrlencodedUri()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(
                   Http.HttpVersion.Http11, HttpMethod.Get, "/where%2B0?q=now%2B0&f=then%2B0#section1%2B0");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Scheme, "http");
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("GET"),
                Path = new AsciiString("/where%2B0?q=now%2B0&f=then%2B0#section1%2B0"),
                Scheme = new AsciiString("http")
            };

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void AbsoluteFormRequestTargetHandledFromHeaders()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Get, "/pub/WWW/TheProject.html");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpHeaderNames.Host, "foouser@www.example.org:5555");
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Path, "ignored_path");
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Scheme, "https");
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("GET"),
                Path = new AsciiString("/pub/WWW/TheProject.html"),
                Authority = new AsciiString("www.example.org:5555"),
                Scheme = new AsciiString("https")
            };

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void AbsoluteFormRequestTargetHandledFromRequestTargetUri()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Get,
                   "http://foouser@www.example.org:5555/pub/WWW/TheProject.html");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("GET"),
                Path = new AsciiString("/pub/WWW/TheProject.html"),
                Authority = new AsciiString("www.example.org:5555"),
                Scheme = new AsciiString("http")
            };

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void AuthorityFormRequestTargetHandled()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Connect, "http://www.example.com:80");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("CONNECT"),
                Path = new AsciiString("/"),
                //Authority = new AsciiString("www.example.com:80"), // Uri 忽略默认端口 80
                Authority = new AsciiString("www.example.com"),
                Scheme = new AsciiString("http")
            };

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void AsterikFormRequestTargetHandled()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Options, "*");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpHeaderNames.Host, "www.example.com:80");
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Scheme, "http");
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("OPTIONS"),
                Path = new AsciiString("*"),
                Authority = new AsciiString("www.example.com:80"),
                Scheme = new AsciiString("http")
            };

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void HostIPv6FormRequestTargetHandled()
        {
            // Valid according to
            // https://tools.ietf.org/html/rfc7230#section-2.7.1 -> https://tools.ietf.org/html/rfc3986#section-3.2.2
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Get, "/");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpHeaderNames.Host, "[::1]:80");
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Scheme, "http");
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("GET"),
                Path = new AsciiString("/"),
                Authority = new AsciiString("[::1]:80"),
                Scheme = new AsciiString("http")
            };

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void HostFormRequestTargetHandled()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Get, "/");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpHeaderNames.Host, "localhost:80");
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Scheme, "http");
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("GET"),
                Path = new AsciiString("/"),
                Authority = new AsciiString("localhost:80"),
                Scheme = new AsciiString("http")
            };

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void HostIPv4FormRequestTargetHandled()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Get, "/");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpHeaderNames.Host, "1.2.3.4:80");
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.Scheme, "http");
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("GET"),
                Path = new AsciiString("/"),
                Authority = new AsciiString("1.2.3.4:80"),
                Scheme = new AsciiString("http")
            };

            var writePromise = NewPromise();
            VerifyHeadersOnly(http2Headers, writePromise, _clientChannel.WriteAndFlushAsync(request, writePromise));
        }

        [Fact]
        public void NoSchemeRequestTargetHandled()
        {
            BootstrapEnv(2, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Get, "/");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.StreamId, 5);
            httpHeaders.Set(HttpHeaderNames.Host, "localhost");
            var writePromise = NewPromise();
            var writeFuture = _clientChannel.WriteAndFlushAsync(request, writePromise);

            Task.WaitAny(writePromise.Task, Task.Delay(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            //Assert.True(writePromise.Task.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));

            Assert.True(writePromise.IsCompleted);
            Assert.False(writePromise.IsSuccess);
            Assert.True(writeFuture.IsCompleted);
            Assert.False(writeFuture.IsSuccess());
        }

        [Fact]
        public void RequestWithBody()
        {
            string text = "foooooogoooo";
            List<string> receivedBuffers = new List<string>();
            _serverListener
                .Setup(x => x.OnDataRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.IsAny<IByteBuffer>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == true)))
                .Returns<IChannelHandlerContext, int, IByteBuffer, int, bool>((ctx, id, buf, p, e) =>
                {
                    lock (receivedBuffers)
                    {
                        receivedBuffers.Add(buf.ToString(Encoding.UTF8));
                    }
                    return 0;
                });
            BootstrapEnv(3, 1, 0);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Post,
                   "http://your_user-name123@www.example.org:5555/example",
                   Unpooled.CopiedBuffer(text, Encoding.UTF8));
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.Set(HttpHeaderNames.Host, "www.example-origin.org:5555");
            httpHeaders.Add(AsciiString.Of("foo"), AsciiString.Of("goo"));
            httpHeaders.Add(AsciiString.Of("foo"), AsciiString.Of("goo2"));
            httpHeaders.Add(AsciiString.Of("foo2"), AsciiString.Of("goo2"));
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("POST"),
                Path = new AsciiString("/example"),
                Authority = new AsciiString("www.example-origin.org:5555"),
                Scheme = new AsciiString("http")
            };
            http2Headers.Add(new AsciiString("foo"), new AsciiString("goo"));
            http2Headers.Add(new AsciiString("foo"), new AsciiString("goo2"));
            http2Headers.Add(new AsciiString("foo2"), new AsciiString("goo2"));
            var writePromise = NewPromise();
            var writeFuture = _clientChannel.WriteAndFlushAsync(request, writePromise);

            Assert.True(writePromise.Task.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(writePromise.IsSuccess);
            Assert.True(writeFuture.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(writeFuture.IsSuccess());
            AwaitRequests();
            _serverListener.Verify(
                x => x.OnHeadersRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.Is<IHttp2Headers>(v => v.Equals(http2Headers)),
                    It.Is<int>(v => v == 0),
                    It.IsAny<short>(),
                    It.IsAny<bool>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == false)));
            _serverListener.Verify(
                x => x.OnDataRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.IsAny<IByteBuffer>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == true)));
            lock (receivedBuffers)
            {
                Assert.Single(receivedBuffers);
                Assert.Equal(text, receivedBuffers[0]);
            }
        }

        [Fact]
        public void RequestWithBodyAndTrailingHeaders()
        {
            string text = "foooooogoooo";
            List<string> receivedBuffers = new List<string>();
            _serverListener
                .Setup(x => x.OnDataRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.IsAny<IByteBuffer>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == false)))
                .Returns<IChannelHandlerContext, int, IByteBuffer, int, bool>((ctx, id, buf, p, e) =>
                {
                    lock (receivedBuffers)
                    {
                        receivedBuffers.Add(buf.ToString(Encoding.UTF8));
                    }
                    return 0;
                });
            BootstrapEnv(4, 1, 1);
            IFullHttpRequest request = new DefaultFullHttpRequest(Http.HttpVersion.Http11, HttpMethod.Post,
                   "http://your_user-name123@www.example.org:5555/example",
                   Unpooled.CopiedBuffer(text, Encoding.UTF8));
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.Set(HttpHeaderNames.Host, "www.example.org:5555");
            httpHeaders.Add(AsciiString.Of("foo"), AsciiString.Of("goo"));
            httpHeaders.Add(AsciiString.Of("foo"), AsciiString.Of("goo2"));
            httpHeaders.Add(AsciiString.Of("foo2"), AsciiString.Of("goo2"));
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("POST"),
                Path = new AsciiString("/example"),
                Authority = new AsciiString("www.example.org:5555"),
                Scheme = new AsciiString("http")
            };
            http2Headers.Add(new AsciiString("foo"), new AsciiString("goo"));
            http2Headers.Add(new AsciiString("foo"), new AsciiString("goo2"));
            http2Headers.Add(new AsciiString("foo2"), new AsciiString("goo2"));

            request.TrailingHeaders.Add(AsciiString.Of("trailing"), AsciiString.Of("bar"));

            IHttp2Headers http2TrailingHeaders = new DefaultHttp2Headers
            {
                { new AsciiString("trailing"), new AsciiString("bar") }
            };

            var writePromise = NewPromise();
            var writeFuture = _clientChannel.WriteAndFlushAsync(request, writePromise);

            Assert.True(writePromise.Task.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(writePromise.IsSuccess);
            Assert.True(writeFuture.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(writeFuture.IsSuccess());
            AwaitRequests();
            _serverListener.Verify(
                x => x.OnHeadersRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.Is<IHttp2Headers>(v => v.Equals(http2Headers)),
                    It.Is<int>(v => v == 0),
                    It.IsAny<short>(),
                    It.IsAny<bool>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == false)));
            _serverListener.Verify(
                x => x.OnDataRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.IsAny<IByteBuffer>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == false)));
            _serverListener.Verify(
                x => x.OnHeadersRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.Is<IHttp2Headers>(v => v.Equals(http2TrailingHeaders)),
                    It.Is<int>(v => v == 0),
                    It.IsAny<short>(),
                    It.IsAny<bool>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == true)));
            lock (receivedBuffers)
            {
                Assert.Single(receivedBuffers);
                Assert.Equal(text, receivedBuffers[0]);
            }
        }

        [Fact]
        public void ChunkedRequestWithBodyAndTrailingHeaders()
        {
            string text = "foooooo";
            string text2 = "goooo";
            List<string> receivedBuffers = new List<string>();
            _serverListener
                .Setup(x => x.OnDataRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.IsAny<IByteBuffer>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == false)))
                .Returns<IChannelHandlerContext, int, IByteBuffer, int, bool>((ctx, id, buf, p, e) =>
                {
                    lock (receivedBuffers)
                    {
                        receivedBuffers.Add(buf.ToString(Encoding.UTF8));
                    }
                    return 0;
                });
            BootstrapEnv(4, 1, 1);
            IHttpRequest request = new DefaultHttpRequest(Http.HttpVersion.Http11, HttpMethod.Post,
                   "http://your_user-name123@www.example.org:5555/example");
            HttpHeaders httpHeaders = request.Headers;
            httpHeaders.Set(HttpHeaderNames.Host, "www.example.org:5555");
            httpHeaders.Add(HttpHeaderNames.TransferEncoding, "chunked");
            httpHeaders.Add(AsciiString.Of("foo"), AsciiString.Of("goo"));
            httpHeaders.Add(AsciiString.Of("foo"), AsciiString.Of("goo2"));
            httpHeaders.Add(AsciiString.Of("foo2"), AsciiString.Of("goo2"));
            var http2Headers = new DefaultHttp2Headers()
            {
                Method = new AsciiString("POST"),
                Path = new AsciiString("/example"),
                Authority = new AsciiString("www.example.org:5555"),
                Scheme = new AsciiString("http")
            };
            http2Headers.Add(new AsciiString("foo"), new AsciiString("goo"));
            http2Headers.Add(new AsciiString("foo"), new AsciiString("goo2"));
            http2Headers.Add(new AsciiString("foo2"), new AsciiString("goo2"));

            DefaultHttpContent httpContent = new DefaultHttpContent(Unpooled.CopiedBuffer(text, Encoding.UTF8));
            ILastHttpContent lastHttpContent = new DefaultLastHttpContent(Unpooled.CopiedBuffer(text2, Encoding.UTF8));

            lastHttpContent.TrailingHeaders.Add(AsciiString.Of("trailing"), AsciiString.Of("bar"));

            IHttp2Headers http2TrailingHeaders = new DefaultHttp2Headers
            {
                { new AsciiString("trailing"), new AsciiString("bar") }
            };

            var writePromise = NewPromise();
            var writeFuture = _clientChannel.WriteAsync(request, writePromise);
            var contentPromise = NewPromise();
            var contentFuture = _clientChannel.WriteAsync(httpContent, contentPromise);
            var lastContentPromise = NewPromise();
            var lastContentFuture = _clientChannel.WriteAsync(lastHttpContent, lastContentPromise);

            _clientChannel.Flush();

            Assert.True(writePromise.Task.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(writePromise.IsSuccess);
            Assert.True(writeFuture.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(writeFuture.IsSuccess());

            Assert.True(contentPromise.Task.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(contentPromise.IsSuccess);
            Assert.True(contentFuture.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(contentFuture.IsSuccess());

            Assert.True(lastContentPromise.Task.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(lastContentPromise.IsSuccess);
            Assert.True(lastContentFuture.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(lastContentFuture.IsSuccess());

            AwaitRequests();
            _serverListener.Verify(
                x => x.OnHeadersRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.Is<IHttp2Headers>(v => v.Equals(http2Headers)),
                    It.Is<int>(v => v == 0),
                    It.IsAny<short>(),
                    It.IsAny<bool>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == false)));
            _serverListener.Verify(
                x => x.OnDataRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.IsAny<IByteBuffer>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == false)));
            _serverListener.Verify(
                x => x.OnHeadersRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 3),
                    It.Is<IHttp2Headers>(v => v.Equals(http2TrailingHeaders)),
                    It.Is<int>(v => v == 0),
                    It.IsAny<short>(),
                    It.IsAny<bool>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == true)));
            lock (receivedBuffers)
            {
                Assert.Single(receivedBuffers);
                Assert.Equal(text + text2, receivedBuffers[0]);
            }
        }

        protected abstract void SetupServerBootstrap(ServerBootstrap bootstrap);

        protected abstract void SetupBootstrap(Bootstrap bootstrap);

        protected virtual void SetInitialServerChannelPipeline(IChannel ch)
        {
            _serverConnectedChannel = ch;
            var p = ch.Pipeline;
            _serverFrameCountDown =
                    new Http2TestUtil.FrameCountDown(_serverListener.Object, _serverSettingsAckLatch,
                            _requestLatch, null, _trailersLatch);
            //p.AddLast(new DotNetty.Handlers.Logging.LoggingHandler("CONN"));
            p.AddLast((new HttpToHttp2ConnectionHandlerBuilder()
            {
                IsServer = true,
                FrameListener = _serverFrameCountDown
            }).Build());
        }

        protected virtual void SetInitialChannelPipeline(IChannel ch)
        {
            var p = ch.Pipeline;
            p.AddLast((new HttpToHttp2ConnectionHandlerBuilder()
            {
                IsServer = false,
                FrameListener = _clientListener.Object,
                GracefulShutdownTimeout = TimeSpan.Zero
            }).Build());
        }

        protected TlsHandler CreateTlsHandler(bool isClient)
        {
            X509Certificate2 tlsCertificate = TestResourceHelper.GetTestCertificate();
            string targetHost = tlsCertificate.GetNameInfo(X509NameType.DnsName, false);
            TlsHandler tlsHandler = isClient ?
                new TlsHandler(new ClientTlsSettings(targetHost).AllowAnyServerCertificate()):
                new TlsHandler(new ServerTlsSettings(tlsCertificate).AllowAnyClientCertificate());
            return tlsHandler;
        }

        private void BootstrapEnv(int requestCountDown, int serverSettingsAckCount, int trailersCount)
        {
            var prefaceWrittenLatch = new CountdownEvent(1);
            var serverChannelLatch = new CountdownEvent(1);
            _requestLatch = new CountdownEvent(requestCountDown);
            _serverSettingsAckLatch = new CountdownEvent(serverSettingsAckCount);
            _trailersLatch = trailersCount == 0 ? null : new CountdownEvent(trailersCount);

            _sb = new ServerBootstrap();
            _cb = new Bootstrap();

            SetupServerBootstrap(_sb);
            _sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                SetInitialServerChannelPipeline(ch);
                serverChannelLatch.SafeSignal();
            }));

            SetupBootstrap(_cb);
            _cb.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                SetInitialChannelPipeline(ch);
                ch.Pipeline.AddLast(new TestChannelHandlerAdapter(Output, prefaceWrittenLatch));
            }));
            
            StartBootstrap().GetAwaiter().GetResult();

            Assert.True(prefaceWrittenLatch.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(serverChannelLatch.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        }

        private void VerifyHeadersOnly(IHttp2Headers expected, IPromise writePromise, Task writeFuture)
        {
            Assert.True(writePromise.Task.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(writePromise.IsSuccess);
            Assert.True(writeFuture.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            Assert.True(writeFuture.IsSuccess());
            AwaitRequests();
            _serverListener.Verify(
                x => x.OnHeadersRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.Is<int>(v => v == 5),
                    It.Is<IHttp2Headers>(v => expected.Equals(v)),
                    //It.Is<IHttp2Headers>(v => HeadersEquals(expected, v)),
                    It.Is<int>(v => v == 0),
                    It.IsAny<short>(),
                    It.IsAny<bool>(),
                    It.Is<int>(v => v == 0),
                    It.Is<bool>(v => v == true)));
            _serverListener.Verify(
                x => x.OnDataRead(
                    It.IsAny<IChannelHandlerContext>(),
                    It.IsAny<int>(),
                    It.IsAny<IByteBuffer>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }

        private static bool HeadersEquals(IHttp2Headers expected, IHttp2Headers actual)
        {
            var result = expected.Equals(actual);
            return result;
        }

        private void AwaitRequests()
        {
            Assert.True(_requestLatch.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            if (_trailersLatch != null)
            {
                Assert.True(_trailersLatch.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
            }
            Assert.True(_serverSettingsAckLatch.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        }

        private IChannelHandlerContext Ctx()
        {
            return _clientChannel.Pipeline.FirstContext();
        }

        private IPromise NewPromise()
        {
            return Ctx().NewPromise();
        }
    }
}
