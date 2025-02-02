﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels.Sockets;

namespace DotNetty.Handlers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Security;
    using System.Runtime.InteropServices;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;
    using DotNetty.Buffers;
    using DotNetty.Common.Concurrency;
    using DotNetty.Common.Utilities;
    using DotNetty.Handlers.Tls;
    using DotNetty.Tests.Common;
    using DotNetty.Transport.Channels;
    using DotNetty.Transport.Channels.Embedded;
    using Xunit;
    using Xunit.Abstractions;

    public class TlsHandlerTest : TestBase
    {
        static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        public TlsHandlerTest(ITestOutputHelper output)
            : base(output)
        {
        }

        public static IEnumerable<object[]> GetTlsReadTestData()
        {
            var random = new Random(Environment.TickCount);
            var lengthVariations =
                new[]
                {
                    new[] { 1 },
                    new[] { 2, 8000, 300 },
                    new[] { 100, 0, 1000 },
                    new[] { 4 * 1024 - 10, 1, 0, 1 },
                    new[] { 0, 24000, 0, 1000 },
                    new[] { 0, 4000, 0 },
                    new[] { 16 * 1024 - 100 },
                    Enumerable.Repeat(0, 30).Select(_ => random.Next(0, 17000)).ToArray()
                };
            var boolToggle = new[] { false, true };
            var protocols = new List<Tuple<SslProtocols, SslProtocols>>();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                protocols.Add(Tuple.Create(SslProtocols.Tls12, SslProtocols.Tls12));
#if NET6_0_OR_GREATER
                //protocols.Add(Tuple.Create(SslProtocols.Tls13, SslProtocols.Tls13));
#endif
                protocols.Add(Tuple.Create(SslProtocols.Tls12 | SslProtocols.Tls, SslProtocols.Tls12 | SslProtocols.Tls11));
            }
            else
            {
                protocols.Add(Tuple.Create(SslProtocols.Tls12, SslProtocols.Tls12));
                protocols.Add(Tuple.Create(SslProtocols.Tls12 | SslProtocols.Tls11, SslProtocols.Tls12 | SslProtocols.Tls11));
            }
            var writeStrategyFactories = new Func<IWriteStrategy>[]
            {
                () => new AsIsWriteStrategy(),
                () => new BatchingWriteStrategy(1, TimeSpan.FromMilliseconds(20), true),
                () => new BatchingWriteStrategy(4096, TimeSpan.FromMilliseconds(20), true),
                () => new BatchingWriteStrategy(32 * 1024, TimeSpan.FromMilliseconds(20), false)
            };

            return
                from frameLengths in lengthVariations
                from isClient in boolToggle
                from writeStrategyFactory in writeStrategyFactories
                from protocol in protocols
                select new object[] { frameLengths, isClient, writeStrategyFactory(), protocol.Item1, protocol.Item2 };
        }


        [Theory]
        [MemberData(nameof(GetTlsReadTestData))]
        public async Task TlsRead(int[] frameLengths, bool isClient, IWriteStrategy writeStrategy, SslProtocols serverProtocol, SslProtocols clientProtocol)
        {
            this.Output.WriteLine($"frameLengths: {string.Join(", ", frameLengths)}");
            this.Output.WriteLine($"isClient: {isClient}");
            this.Output.WriteLine($"writeStrategy: {writeStrategy}");
            this.Output.WriteLine($"serverProtocol: {serverProtocol}");
            this.Output.WriteLine($"clientProtocol: {clientProtocol}");
            this.Output.WriteLine($"os: {Environment.OSVersion}");
            this.Output.WriteLine($"os sys: {Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.OperatingSystem}");
            this.Output.WriteLine($"os sys plat: {Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.OperatingSystemPlatform}");
            this.Output.WriteLine($"os sys ver: {Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.OperatingSystemVersion}");

            var executor = new DefaultEventExecutor();

            try
            {
                var writeTasks = new List<Task>();
                var pair = await SetupStreamAndChannelAsync(isClient, executor, writeStrategy, serverProtocol, clientProtocol, writeTasks).WithTimeout(TimeSpan.FromSeconds(10));
                EmbeddedChannel ch = pair.Item1;
                SslStream driverStream = pair.Item2;

                int randomSeed = Environment.TickCount;
                var random = new Random(randomSeed);
                IByteBuffer expectedBuffer = Unpooled.Buffer(16 * 1024);
                foreach (int len in frameLengths)
                {
                    var data = new byte[len];
                    random.NextBytes(data);
                    expectedBuffer.WriteBytes(data);
                    await driverStream.WriteAsync(data, 0, data.Length).WithTimeout(TimeSpan.FromSeconds(5));
                }
                await Task.WhenAll(writeTasks).WithTimeout(TimeSpan.FromSeconds(5));
                IByteBuffer finalReadBuffer = Unpooled.Buffer(16 * 1024);
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
                await ReadOutboundAsync(async () => ch.ReadInbound<IByteBuffer>(), expectedBuffer.ReadableBytes, finalReadBuffer, TestTimeout);
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
                bool isEqual = ByteBufferUtil.Equals(expectedBuffer, finalReadBuffer);
                if (!isEqual)
                {
                    Assert.True(isEqual, $"---Expected:\n{ByteBufferUtil.PrettyHexDump(expectedBuffer)}\n---Actual:\n{ByteBufferUtil.PrettyHexDump(finalReadBuffer)}");
                }
                driverStream.Dispose();
                await ch.CloseAsync(); //closing channel causes TlsHandler.Flush() to send final empty buffer
                _ = ch.ReadOutbound<EmptyByteBuffer>();
                Assert.False(ch.Finish());
            }
            finally
            {
                await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }
        }

        public static IEnumerable<object[]> GetTlsWriteTestData()
        {
            var random = new Random(Environment.TickCount);
            var lengthVariations =
                new[]
                {
                    new[] { 1 },
                    new[] { 2, 8000, 300 },
                    new[] { 100, 0, 1000 },
                    new[] { 4 * 1024 - 10, 1, -1, 0, -1, 1 },
                    new[] { 0, 24000, 0, -1, 1000 },
                    new[] { 0, 4000, 0 },
                    new[] { 16 * 1024 - 100 },
                    Enumerable.Repeat(0, 30).Select(_ => random.Next(0, 10) < 2 ? -1 : random.Next(0, 17000)).ToArray()
                };
            var boolToggle = new[] { false, true };
            var protocols = new List<Tuple<SslProtocols, SslProtocols>>();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                protocols.Add(Tuple.Create(SslProtocols.Tls12, SslProtocols.Tls12));
#if NET6_0_OR_GREATER
                //protocols.Add(Tuple.Create(SslProtocols.Tls13, SslProtocols.Tls13));
#endif
                protocols.Add(Tuple.Create(SslProtocols.Tls12 | SslProtocols.Tls, SslProtocols.Tls12 | SslProtocols.Tls11));
            }
            else
            {
                protocols.Add(Tuple.Create(SslProtocols.Tls12, SslProtocols.Tls12));
                protocols.Add(Tuple.Create(SslProtocols.Tls12 | SslProtocols.Tls11, SslProtocols.Tls12 | SslProtocols.Tls11));
            }

            return
                from frameLengths in lengthVariations
                from isClient in boolToggle
                from protocol in protocols
                select new object[] { frameLengths, isClient, protocol.Item1, protocol.Item2 };
        }

        [Theory]
        [MemberData(nameof(GetTlsWriteTestData))]
        public async Task TlsWrite(int[] frameLengths, bool isClient, SslProtocols serverProtocol, SslProtocols clientProtocol)
        {
            this.Output.WriteLine($"frameLengths: {string.Join(", ", frameLengths)}");
            this.Output.WriteLine($"isClient: {isClient}");
            this.Output.WriteLine($"serverProtocol: {serverProtocol}");
            this.Output.WriteLine($"clientProtocol: {clientProtocol}");

            var writeStrategy = new AsIsWriteStrategy();
            this.Output.WriteLine($"writeStrategy: {writeStrategy}");

            var executor = new DefaultEventExecutor();

            try
            {
                var writeTasks = new List<Task>();
                var pair = await SetupStreamAndChannelAsync(isClient, executor, writeStrategy, serverProtocol, clientProtocol, writeTasks);
                EmbeddedChannel ch = pair.Item1;
                SslStream driverStream = pair.Item2;

                int randomSeed = Environment.TickCount;
                var random = new Random(randomSeed);
                IByteBuffer expectedBuffer = Unpooled.Buffer(16 * 1024);
                foreach (IEnumerable<int> lengths in frameLengths.Split(x => x < 0))
                {
                    ch.WriteOutbound(lengths.Select(len =>
                    {
                        var data = new byte[len];
                        random.NextBytes(data);
                        expectedBuffer.WriteBytes(data);
                        return (object)Unpooled.WrappedBuffer(data);
                    }).ToArray());
                }

                IByteBuffer finalReadBuffer = Unpooled.Buffer(16 * 1024);
                var readBuffer = new byte[16 * 1024 * 10];
                await ReadOutboundAsync(
                    async () =>
                    {
                        int read = await driverStream.ReadAsync(readBuffer, 0, readBuffer.Length);
                        return Unpooled.WrappedBuffer(readBuffer, 0, read);
                    },
                    expectedBuffer.ReadableBytes, finalReadBuffer, TestTimeout);
                bool isEqual = ByteBufferUtil.Equals(expectedBuffer, finalReadBuffer);
                if (!isEqual)
                {
                    Assert.True(isEqual, $"---Expected:\n{ByteBufferUtil.PrettyHexDump(expectedBuffer)}\n---Actual:\n{ByteBufferUtil.PrettyHexDump(finalReadBuffer)}");
                }
                driverStream.Dispose();

                if (ch.Finish())
                {
                    var emptyByteBufferOutbound = ch.ReadOutbound<EmptyByteBuffer>();
                    Assert.NotNull(emptyByteBufferOutbound);
                }

                Assert.False(ch.Finish());
            }
            finally
            {
                await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }
        }

        static async Task<Tuple<EmbeddedChannel, SslStream>> SetupStreamAndChannelAsync(bool isClient, IEventExecutor executor, IWriteStrategy writeStrategy, SslProtocols serverProtocol, SslProtocols clientProtocol, List<Task> writeTasks)
        {
            X509Certificate2 tlsCertificate = TestResourceHelper.GetTestCertificate();
            string targetHost = tlsCertificate.GetNameInfo(X509NameType.DnsName, false);
            TlsHandler tlsHandler = isClient ?
                new TlsHandler(new ClientTlsSettings(clientProtocol, false, new List<X509Certificate> { tlsCertificate }, targetHost).AllowAnyServerCertificate()) :
                new TlsHandler(new ServerTlsSettings(tlsCertificate, false, false, serverProtocol).AllowAnyClientCertificate());
            //var ch = new EmbeddedChannel(new LoggingHandler("BEFORE"), tlsHandler, new LoggingHandler("AFTER"));
            var ch = new EmbeddedChannel(tlsHandler);

            IByteBuffer readResultBuffer = Unpooled.Buffer(4 * 1024);
            Func<ArraySegment<byte>, Task<int>> readDataFunc = async output =>
            {
                if (writeTasks.Count > 0)
                {
                    await Task.WhenAll(writeTasks).WithTimeout(TestTimeout);
                    writeTasks.Clear();
                }

                if (readResultBuffer.ReadableBytes < output.Count)
                {
                    if (ch.IsActive)
                    {
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
                        await ReadOutboundAsync(async () => ch.ReadOutbound<IByteBuffer>(), output.Count - readResultBuffer.ReadableBytes, readResultBuffer, TestTimeout, readResultBuffer.ReadableBytes != 0 ? 0 : 1);
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
                    }
                }
                int read = Math.Min(output.Count, readResultBuffer.ReadableBytes);
                readResultBuffer.ReadBytes(output.Array, output.Offset, read);
                return read;
            };
            var mediationStream = new MediationStream(readDataFunc, input =>
            {
                Task task = executor.SubmitAsync(() => writeStrategy.WriteToChannelAsync(ch, input)).Unwrap();
                writeTasks.Add(task);
                return task;
            }, () =>
            {
                ch.CloseAsync();
            });

            var driverStream = new SslStream(mediationStream, true, (_1, _2, _3, _4) => true);
            var handshakeTimeout = TimeSpan.FromSeconds(5);
            if (isClient)
            {
                await Task.Run(() => driverStream.AuthenticateAsServerAsync(tlsCertificate, false, serverProtocol, false)).WithTimeout(handshakeTimeout);
            }
            else
            {
                await Task.Run(() => driverStream.AuthenticateAsClientAsync(targetHost, null, clientProtocol, false)).WithTimeout(handshakeTimeout);
            }
            
            await tlsHandler.HandshakeCompletion.WithTimeout(handshakeTimeout);

            writeTasks.Clear();

            return Tuple.Create(ch, driverStream);
        }

        static Task ReadOutboundAsync(Func<Task<IByteBuffer>> readFunc, int expectedBytes, IByteBuffer result, TimeSpan timeout, int minBytes = -1)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int remaining = expectedBytes;
            if (minBytes < 0) minBytes = expectedBytes;
            if (minBytes > expectedBytes) throw new ArgumentOutOfRangeException("minBytes can not greater than expectedBytes");
            return AssertEx.EventuallyAsync(
                async () =>
                {
                    TimeSpan readTimeout = timeout - stopwatch.Elapsed;
                    if (readTimeout <= TimeSpan.Zero)
                    {
                        return false;
                    }

                    IByteBuffer output;
                    while (true)
                    {
                        output = await readFunc().WithTimeout(readTimeout);//inbound ? ch.ReadInbound<IByteBuffer>() : ch.ReadOutbound<IByteBuffer>();
                        if (output == null)
                            break;

                        if (!output.IsReadable())
                        {
                            output.Release(); //received empty message but that's not necessary the end of the data stream
                            continue;
                        }

                        remaining -= output.ReadableBytes;
                        minBytes -= output.ReadableBytes;
                        result.WriteBytes(output);
                        output.Release();

                        if (remaining <= 0)
                            return true;
                    }
                    return minBytes <= 0;
                },
                TimeSpan.FromMilliseconds(10),
                timeout);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NoAutoReadHandshakeProgresses(bool dropChannelActive)
        {
            var readHandler = new ReadRegisterHandler();
            var ch = new EmbeddedChannel(EmbeddedChannelId.Instance, false, false,
               readHandler,
               TlsHandler.Client("dotnetty.com", true),
               new ActivatingHandler(dropChannelActive)
            );

            ch.Configuration.IsAutoRead = false;
            ch.Register();
            Assert.False(ch.Configuration.IsAutoRead);
            Assert.True(ch.WriteOutbound(Unpooled.Empty));
            Assert.True(readHandler.ReadIssued);
            ch.CloseAsync();
        }

        class ReadRegisterHandler : ChannelHandlerAdapter
        {
            public bool ReadIssued { get; private set; }

            public override void Read(IChannelHandlerContext context)
            {
                this.ReadIssued = true;
                base.Read(context);
            }
        }

        class ActivatingHandler : ChannelHandlerAdapter
        {
            bool dropChannelActive;

            public ActivatingHandler(bool dropChannelActive)
            {
                this.dropChannelActive = dropChannelActive;
            }

            public override void ChannelActive(IChannelHandlerContext context)
            {
                if (!dropChannelActive)
                {
                    context.FireChannelActive();
                }
            }
        }
        
 
#if NET6_0_OR_GREATER
        [Fact]
        [Description("Channel event loop should not be blocked by hanging renegotiation")]
        public async Task TlsClientWriteWithRenegotiationOverSocketChannel()
        {
            SslProtocols tlsProto = SslProtocols.Tls12;
            var ioTimeout = TimeSpan.FromSeconds(5);
            var handshakeTimeout = TimeSpan.FromSeconds(5);
            X509Certificate2 tlsCertificate = TestResourceHelper.GetTestCertificate();
            string targetHost = tlsCertificate.GetNameInfo(X509NameType.DnsName, false);
                
            var serverListener = new TcpListener(IPAddress.IPv6Loopback, 0);
            serverListener.Start();
            this.Output.WriteLine("[Server] Started on {0}", serverListener.LocalEndpoint);
            var serverEndpoint = serverListener.LocalEndpoint;

            var negotiateTrigger = new TaskCompletionSource();

            var serverTask = Task.Run(async () =>
            {
                this.Output.WriteLine("[Server] Waiting for incoming connection on {0}", serverEndpoint);
                using var serverConnection = await serverListener.AcceptTcpClientAsync();
                this.Output.WriteLine("[Server] Incoming connection received: {0} => {1}", serverConnection.Client.RemoteEndPoint, serverEndpoint);
                await using var serverStream = serverConnection.GetStream();
                await using var tlsStream = new SslStream(serverStream, true, (_1, _2, _3, _4) => true);
                
                try
                {
                    this.Output.WriteLine("[Server] Waiting TLS handshake request");
                    await tlsStream.AuthenticateAsServerAsync(tlsCertificate, false, tlsProto, false).WithTimeout(handshakeTimeout);
                    this.Output.WriteLine("[Server] TLS handshake completed");
                    
                    await negotiateTrigger.Task;
                    try
                    {
                        this.Output.WriteLine("[Server] Negotiate client certificate started");
                        await tlsStream.NegotiateClientCertificateAsync();
                        this.Output.WriteLine("[Server] Negotiate client certificate completed");
                        Assert.NotNull(tlsStream.RemoteCertificate);
                    }
                    catch (InvalidOperationException ex) when (ex.Message == "Received data during renegotiation.")
                    {
                        this.Output.WriteLine("[Server] Negotiate client certificate failed: {0}", ex);
                        return;
                    }

                    //draining incoming data in case of successful renegotiation
                    var buf = new byte[1024];
                    int read;
                    do
                    {
                        read = await tlsStream.ReadAsync(buf, 0, buf.Length);
                        this.Output.WriteLine("[Server] {0} bytes received", read);
                    } while (read > 0);
                }
                finally
                {
                    tlsStream.Close();
                    serverStream.Close();
                    serverConnection.Close();
                }
            });
            
            var clientBootstrap = new Bootstrap();
            clientBootstrap.Group(new MultithreadEventLoopGroup()).Channel<TcpSocketChannel>();
            TlsHandler tlsHandler = new TlsHandler(new ClientTlsSettings(tlsProto, false, new List<X509Certificate> {tlsCertificate}, targetHost).AllowAnyServerCertificate());
            clientBootstrap.Handler(new ActionChannelInitializer<IChannel>(ch => ch.Pipeline.AddLast(tlsHandler)));
            IEventLoop channelLoop = null;

            var clientTask = Task.Run(async () =>
            {
                this.Output.WriteLine("[Client] Connecting to {0}", serverEndpoint);
                var ch = await clientBootstrap.ConnectAsync(serverEndpoint);
                this.Output.WriteLine("[Client] Connection established: {0}", ch);
                channelLoop = ch.EventLoop;

                await tlsHandler.HandshakeCompletion.WithTimeout(handshakeTimeout);
                this.Output.WriteLine("[Client] TLS handshake completed");

                var random = new Random(Environment.TickCount);
                int[] frameLengths = Enumerable.Repeat(0, 30).Select(_ => random.Next(0, 17000)).ToArray();

                try
                {
                    for (var i = 0; i < frameLengths.Length; i++)
                    {
                        var len = frameLengths[i];
                        var data = new byte[len];
                        random.NextBytes(data);
                        var buf = Unpooled.WrappedBuffer(data);
                        var writeTask = ch.WriteAndFlushAsync(buf, ch.NewPromise()).WithTimeout(ioTimeout);
                        negotiateTrigger.TrySetResult();
                        await writeTask;
                    }
                }
                catch (Exception ex)
                {
                    this.Output.WriteLine("[Client] write failed: {0}", ex);
                }

                this.Output.WriteLine("[Client] Connection closing: {0}", ch);
                await ch.CloseAsync();
                this.Output.WriteLine("[Client] Connection closed: {0}", ch);
            });

            try
            {
                await Task.WhenAll(serverTask, clientTask).WithTimeout(ioTimeout);
            }
            catch (TimeoutException)
            {
                this.Output.WriteLine($"Client or Server tasks didn't complete within {ioTimeout} timeout.");
            }
            finally
            {
                serverListener.Stop();  
                this.Output.WriteLine("[Server] Stopped");
            }
            
            Assert.NotNull(channelLoop);
            //assert event loop is not blocked
            await channelLoop.SubmitAsync(() => 1).WithTimeout(TimeSpan.FromSeconds(5));
        }        
#endif
    }
}
