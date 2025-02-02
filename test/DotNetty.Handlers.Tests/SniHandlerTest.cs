﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Handlers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Security;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using System.Runtime.InteropServices;
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

    public class SniHandlerTest : TestBase
    {
        static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
        static readonly Dictionary<string, ServerTlsSettings> SettingMap = new Dictionary<string, ServerTlsSettings>();

        static SniHandlerTest()
        {
            X509Certificate2 tlsCertificate = TestResourceHelper.GetTestCertificate();
            X509Certificate2 tlsCertificate2 = TestResourceHelper.GetTestCertificate2();

            //#if NET6_0_OR_GREATER
            //            SslProtocols serverProtocol = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? SslProtocols.Tls13 : SslProtocols.Tls12;
            //#else
            SslProtocols serverProtocol = SslProtocols.Tls12;
            //#endif
            SettingMap[tlsCertificate.GetNameInfo(X509NameType.DnsName, false)] = new ServerTlsSettings(tlsCertificate, false, false, serverProtocol);
            SettingMap[tlsCertificate2.GetNameInfo(X509NameType.DnsName, false)] = new ServerTlsSettings(tlsCertificate2, false, false, serverProtocol);
        }

        public SniHandlerTest(ITestOutputHelper output)
          : base(output)
        {
        }

        public static IEnumerable<object[]> GetTlsReadTestData()
        {
            var lengthVariations = new[]
            {
                new[] { 1 }
            };
            var boolToggle = new[] { false, true };
            //#if NET6_0_OR_GREATER
            //            var protocols = new[] { RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? SslProtocols.Tls13 : SslProtocols.Tls12 };
            //#else
            var protocols = new[] { SslProtocols.Tls12 };
            //#endif
            var writeStrategyFactories = new Func<IWriteStrategy>[]
            {
        () => new AsIsWriteStrategy()
            };

            return
                from frameLengths in lengthVariations
                from isClient in boolToggle
                from writeStrategyFactory in writeStrategyFactories
                from protocol in protocols
                from targetHost in SettingMap.Keys
                select new object[] { frameLengths, isClient, writeStrategyFactory(), protocol, targetHost };
        }


        [Theory]
        [MemberData(nameof(GetTlsReadTestData))]
        public async Task TlsRead(int[] frameLengths, bool isClient, IWriteStrategy writeStrategy, SslProtocols protocol, string targetHost)
        {
            this.Output.WriteLine($"frameLengths: {string.Join(", ", frameLengths)}");
            this.Output.WriteLine($"writeStrategy: {writeStrategy}");
            this.Output.WriteLine($"protocol: {protocol}");
            this.Output.WriteLine($"targetHost: {targetHost}");

            var executor = new DefaultEventExecutor();

            try
            {
                var writeTasks = new List<Task>();
                var pair = await SetupStreamAndChannelAsync(isClient, executor, writeStrategy, protocol, writeTasks, targetHost).WithTimeout(TimeSpan.FromSeconds(10));
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
                Assert.True(ByteBufferUtil.Equals(expectedBuffer, finalReadBuffer), $"---Expected:\n{ByteBufferUtil.PrettyHexDump(expectedBuffer)}\n---Actual:\n{ByteBufferUtil.PrettyHexDump(finalReadBuffer)}");

                if (!isClient)
                {
                    // check if snihandler got replaced with tls handler
                    Assert.Null(ch.Pipeline.Get<SniHandler>());
                    Assert.NotNull(ch.Pipeline.Get<TlsHandler>());
                }

                driverStream.Dispose();
                await ch.CloseAsync(); //closing channel causes TlsHandler.Flush() to send final empty buffer
                var _ = ch.ReadOutbound<EmptyByteBuffer>();
                Assert.False(ch.Finish());
            }
            finally
            {
                await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }
        }

        public static IEnumerable<object[]> GetTlsWriteTestData()
        {
            var lengthVariations = new[]
            {
                new[] { 1 }
            };
            var boolToggle = new[] { false, true };
            //#if NET6_0_OR_GREATER
            //            var protocols = new[] { RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? SslProtocols.Tls13 : SslProtocols.Tls12 };
            //#else
            var protocols = new[] { SslProtocols.Tls12 };
            //#endif

            return
                from frameLengths in lengthVariations
                from isClient in boolToggle
                from protocol in protocols
                from targetHost in SettingMap.Keys
                select new object[] { frameLengths, isClient, protocol, targetHost };
        }

        [Theory]
        [MemberData(nameof(GetTlsWriteTestData))]
        public async Task TlsWrite(int[] frameLengths, bool isClient, SslProtocols protocol, string targetHost)
        {
            this.Output.WriteLine("frameLengths: " + string.Join(", ", frameLengths));
            this.Output.WriteLine($"protocol: {protocol}");
            this.Output.WriteLine($"targetHost: {targetHost}");

            var writeStrategy = new AsIsWriteStrategy();
            var executor = new DefaultEventExecutor();

            try
            {
                var writeTasks = new List<Task>();
                var pair = await SetupStreamAndChannelAsync(isClient, executor, writeStrategy, protocol, writeTasks, targetHost);
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
                Assert.True(ByteBufferUtil.Equals(expectedBuffer, finalReadBuffer), $"---Expected:\n{ByteBufferUtil.PrettyHexDump(expectedBuffer)}\n---Actual:\n{ByteBufferUtil.PrettyHexDump(finalReadBuffer)}");

                if (!isClient)
                {
                    // check if snihandler got replaced with tls handler
                    Assert.Null(ch.Pipeline.Get<SniHandler>());
                    Assert.NotNull(ch.Pipeline.Get<TlsHandler>());
                }

                driverStream.Dispose();
                await ch.CloseAsync(); //closing channel causes TlsHandler.Flush() to send final empty buffer
                var _ = ch.ReadOutbound<EmptyByteBuffer>();
                Assert.False(ch.Finish());
            }
            finally
            {
                await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }
        }

        static async Task<Tuple<EmbeddedChannel, SslStream>> SetupStreamAndChannelAsync(bool isClient, IEventExecutor executor, IWriteStrategy writeStrategy, SslProtocols protocol, List<Task> writeTasks, string targetHost)
        {
            IChannelHandler tlsHandler = isClient ?
                (IChannelHandler)new TlsHandler(stream => new SslStream(stream, true, (sender, certificate, chain, errors) =>
                {
                    Assert.Equal(targetHost, certificate.Issuer.Replace("CN=", string.Empty));
                    return true;
                }), new ClientTlsSettings(protocol, false, new List<X509Certificate>(), targetHost)) :
                new SniHandler(stream => new SslStream(stream, true, (sender, certificate, chain, errors) => true), new ServerTlsSniSettings(CertificateSelector));
            //var ch = new EmbeddedChannel(new LoggingHandler("BEFORE"), tlsHandler, new LoggingHandler("AFTER"));
            var ch = new EmbeddedChannel(tlsHandler);

            if (!isClient)
            {
                // check if in the beginning snihandler exists in the pipeline, but not tls handler
                Assert.NotNull(ch.Pipeline.Get<SniHandler>());
                Assert.Null(ch.Pipeline.Get<TlsHandler>());
            }

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
            });

            var driverStream = new SslStream(mediationStream, true, (_1, _2, _3, _4) => true);
            var handshakeTimeout = TimeSpan.FromSeconds(5);
            if (isClient)
            {
                ServerTlsSettings serverTlsSettings = CertificateSelector(targetHost).Result;
                await Task.Run(() => driverStream.AuthenticateAsServerAsync(serverTlsSettings.Certificate, false, protocol | serverTlsSettings.EnabledProtocols, false).WithTimeout(handshakeTimeout));
            }
            else
            {
                await Task.Run(() => driverStream.AuthenticateAsClientAsync(targetHost, null, protocol, false)).WithTimeout(handshakeTimeout);
            }
                
            await ch.Pipeline.Get<TlsHandler>().HandshakeCompletion.WithTimeout(handshakeTimeout);
            writeTasks.Clear();

            return Tuple.Create(ch, driverStream);
        }

        static Task<ServerTlsSettings> CertificateSelector(string hostName)
        {
            Assert.NotNull(hostName);
            Assert.Contains(hostName, SettingMap.Keys);
            return Task.FromResult(SettingMap[hostName]);
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
    }
}
