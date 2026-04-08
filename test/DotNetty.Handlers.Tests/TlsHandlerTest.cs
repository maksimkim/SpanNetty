// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Handlers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
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
                protocols.Add(Tuple.Create(SslProtocols.Tls, SslProtocols.Tls));
                protocols.Add(Tuple.Create(SslProtocols.Tls11, SslProtocols.Tls11));
                protocols.Add(Tuple.Create(SslProtocols.Tls12, SslProtocols.Tls12));
#if NETCOREAPP_3_0_GREATER
                //protocols.Add(Tuple.Create(SslProtocols.Tls13, SslProtocols.Tls13));
#endif
                protocols.Add(Tuple.Create(SslProtocols.Tls12 | SslProtocols.Tls, SslProtocols.Tls12 | SslProtocols.Tls11));
                protocols.Add(Tuple.Create(SslProtocols.Tls | SslProtocols.Tls12, SslProtocols.Tls | SslProtocols.Tls11));
            }
            else
            {
                protocols.Add(Tuple.Create(SslProtocols.Tls11, SslProtocols.Tls11));
                protocols.Add(Tuple.Create(SslProtocols.Tls12, SslProtocols.Tls12));
                protocols.Add(Tuple.Create(SslProtocols.Tls12 | SslProtocols.Tls11, SslProtocols.Tls12 | SslProtocols.Tls11));
                protocols.Add(Tuple.Create(SslProtocols.Tls11 | SslProtocols.Tls12, SslProtocols.Tls | SslProtocols.Tls11));
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
                protocols.Add(Tuple.Create(SslProtocols.Tls, SslProtocols.Tls));
                protocols.Add(Tuple.Create(SslProtocols.Tls11, SslProtocols.Tls11));
                protocols.Add(Tuple.Create(SslProtocols.Tls12, SslProtocols.Tls12));
#if NETCOREAPP_3_0_GREATER
                //protocols.Add(Tuple.Create(SslProtocols.Tls13, SslProtocols.Tls13));
#endif
                protocols.Add(Tuple.Create(SslProtocols.Tls12 | SslProtocols.Tls, SslProtocols.Tls12 | SslProtocols.Tls11));
                protocols.Add(Tuple.Create(SslProtocols.Tls | SslProtocols.Tls12, SslProtocols.Tls | SslProtocols.Tls11));
            }
            else
            {
                protocols.Add(Tuple.Create(SslProtocols.Tls11, SslProtocols.Tls11));
                protocols.Add(Tuple.Create(SslProtocols.Tls12, SslProtocols.Tls12));
                protocols.Add(Tuple.Create(SslProtocols.Tls12 | SslProtocols.Tls11, SslProtocols.Tls12 | SslProtocols.Tls11));
                protocols.Add(Tuple.Create(SslProtocols.Tls11 | SslProtocols.Tls12, SslProtocols.Tls | SslProtocols.Tls11));
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
                new TlsHandler(stream => new SslStream(stream, true, (sender, certificate, chain, errors) => true), new ClientTlsSettings(clientProtocol, false, new List<X509Certificate>(), targetHost)) :
                new TlsHandler(new ServerTlsSettings(tlsCertificate, false, false, serverProtocol));
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
            if (isClient)
            {
                await Task.Run(() => driverStream.AuthenticateAsServerAsync(tlsCertificate, false, serverProtocol, false)).WithTimeout(TimeSpan.FromSeconds(5));
            }
            else
            {
                await Task.Run(() => driverStream.AuthenticateAsClientAsync(targetHost, null, clientProtocol, false)).WithTimeout(TimeSpan.FromSeconds(5));
            }
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
                            output.Release();
                            return true;
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
               TlsHandler.Client("dotnetty.com"),
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

        /// <summary>
        /// Regression test for https://github.com/maksimkim/SpanNetty/issues/60
        /// Verifies that when the pending write queue is drained re-entrantly during
        /// Wrap (between the Current check and Remove call), the null return from
        /// Remove() is handled gracefully instead of throwing NullReferenceException.
        ///
        /// Uses a custom Stream wrapper around MediationStream to simulate the
        /// re-entrant HandleFailure → RemoveAndFailAll scenario: after SslStream
        /// encrypts data and writes ciphertext to MediationStream (which sets
        /// _lastContextWriteTask via FinishWrap), the wrapper drains the queue and
        /// clears _lastContextWriteTask. When Wrap continues, Remove() returns null
        /// and the unfixed code hits promise.TryComplete() on a null promise → NRE.
        /// </summary>
        [Fact]
        public async Task WrapRemoveNull_ShouldNotThrowNullReferenceException()
        {
            var executor = new DefaultEventExecutor();
            try
            {
                var writeTasks = new List<Task>();
                var writeStrategy = new AsIsWriteStrategy();

                X509Certificate2 tlsCertificate = TestResourceHelper.GetTestCertificate();
                string targetHost = tlsCertificate.GetNameInfo(X509NameType.DnsName, false);

                // Reflection fields for the re-entrant drain simulation
                var queueField = typeof(TlsHandler).GetField("_pendingUnencryptedWrites",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var lastTaskField = typeof(TlsHandler).GetField("_lastContextWriteTask",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                // Create a TlsHandler with a custom SslStream that wraps MediationStream
                // in a QueueDrainingStreamWrapper. The wrapper intercepts SslStream's
                // ciphertext writes and, when enabled, drains the pending write queue
                // to simulate re-entrant HandleFailure.
                QueueDrainingStreamWrapper streamWrapper = null;
                TlsHandler tlsHandler = new TlsHandler(
                    stream =>
                    {
                        streamWrapper = new QueueDrainingStreamWrapper(stream);
                        return new SslStream(streamWrapper, true, (sender, certificate, chain, errors) => true);
                    },
                    new ClientTlsSettings(SslProtocols.Tls12, false, new List<X509Certificate>(), targetHost));

                // Wire up the reflection targets so the wrapper can drain the queue
                streamWrapper.SetTarget(tlsHandler, queueField, lastTaskField);

                var ch = new EmbeddedChannel(tlsHandler);

                // -- Complete the TLS handshake --
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
#pragma warning disable CS1998
                            await ReadOutboundAsync(async () => ch.ReadOutbound<IByteBuffer>(), output.Count - readResultBuffer.ReadableBytes, readResultBuffer, TestTimeout, readResultBuffer.ReadableBytes != 0 ? 0 : 1);
#pragma warning restore CS1998
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
                }, () => { ch.CloseAsync(); });

                var driverStream = new SslStream(mediationStream, true, (_1, _2, _3, _4) => true);
                await Task.Run(() => driverStream.AuthenticateAsServerAsync(tlsCertificate, false, SslProtocols.Tls12, false))
                    .WithTimeout(TimeSpan.FromSeconds(10));
                writeTasks.Clear();

                // -- Handshake complete. Enable the re-entrant drain simulation. --
                streamWrapper.ShouldDrain = true;

                // Write + Flush triggers: TlsHandler.Write (adds to queue) →
                // TlsHandler.Flush → WrapAndFlush → Wrap → buf.ReadBytes(_sslStream, ...) →
                // SslStream encrypts → wrapper.Write → MediationStream.Write (FinishWrap sets
                // _lastContextWriteTask) → wrapper drains queue & clears _lastContextWriteTask →
                // back in Wrap: Remove() returns null, _lastContextWriteTask is null →
                // Without fix: promise.TryComplete() where promise is null → NRE
                // With fix: if (promise is null) { break; } → exits gracefully
                try
                {
                    ch.WriteOutbound(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
                }
                catch (Exception ex)
                {
                    Assert.False(
                        ContainsNullReferenceException(ex),
                        $"NRE from Wrap.Remove() should not occur: {ex}");
                }

                try
                {
                    ch.CheckException();
                }
                catch (Exception ex)
                {
                    Assert.False(
                        ContainsNullReferenceException(ex),
                        $"NRE stored in channel: {ex}");
                }

                Assert.True(streamWrapper.WasDrained,
                    "The queue should have been drained during the write");

                driverStream.Dispose();
            }
            finally
            {
                await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }
        }

        static bool ContainsNullReferenceException(Exception ex)
        {
            if (ex is NullReferenceException) return true;
            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.Flatten().InnerExceptions)
                {
                    if (inner is NullReferenceException) return true;
                }
            }
            return ex.InnerException is object && ContainsNullReferenceException(ex.InnerException);
        }

        /// <summary>
        /// Wraps MediationStream to simulate re-entrant queue drain during SslStream write.
        /// After forwarding the encrypted write to MediationStream (which calls FinishWrap
        /// and sets _lastContextWriteTask), it drains the pending write queue and clears
        /// _lastContextWriteTask — reproducing the effect of HandleFailure being called
        /// re-entrantly during an outbound write.
        /// </summary>
        sealed class QueueDrainingStreamWrapper : Stream
        {
            readonly Stream _inner;
            object _handler;
            System.Reflection.FieldInfo _queueField;
            System.Reflection.FieldInfo _lastTaskField;
            bool _drained;

            public bool ShouldDrain { get; set; }
            public bool WasDrained => _drained;

            public QueueDrainingStreamWrapper(Stream inner) { _inner = inner; }

            public void SetTarget(object handler, System.Reflection.FieldInfo queueField, System.Reflection.FieldInfo lastTaskField)
            {
                _handler = handler;
                _queueField = queueField;
                _lastTaskField = lastTaskField;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _inner.Write(buffer, offset, count);
                DrainIfNeeded();
            }

            private void DrainIfNeeded()
            {
                if (ShouldDrain && !_drained)
                {
                    _drained = true;
                    // Clear _lastContextWriteTask so Remove()'s null hits the else branch
                    // (promise.TryComplete()) instead of the ContinueWith path in LinkOutcome
                    _lastTaskField.SetValue(_handler, null);
                    // Drain the queue to make Remove() return null
                    var queue = (BatchingPendingWriteQueue)_queueField.GetValue(_handler);
                    queue.RemoveAndFailAll(new IOException("simulated connection failure"));
                }
            }

            // Required Stream overrides (forward to inner)
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);
            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }

#if NETCOREAPP || NETSTANDARD_2_0_GREATER
            public override System.Threading.Tasks.ValueTask<int> ReadAsync(System.Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
                => _inner.ReadAsync(buffer, cancellationToken);

            public override void Write(System.ReadOnlySpan<byte> buffer)
            {
                _inner.Write(buffer);
                DrainIfNeeded();
            }

            public override System.Threading.Tasks.ValueTask WriteAsync(System.ReadOnlyMemory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
            {
                var result = _inner.WriteAsync(buffer, cancellationToken);
                DrainIfNeeded();
                return result;
            }
#endif

            public override Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            {
                var task = _inner.WriteAsync(buffer, offset, count, cancellationToken);
                DrainIfNeeded();
                return task;
            }

#if !NETCOREAPP1_1
            public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) => _inner.BeginRead(buffer, offset, count, callback, state);
            public override int EndRead(IAsyncResult asyncResult) => _inner.EndRead(asyncResult);
            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                // On .NET Framework, SslStream.Write uses BeginWrite/EndWrite internally
                var result = _inner.BeginWrite(buffer, offset, count, callback, state);
                DrainIfNeeded();
                return result;
            }
            public override void EndWrite(IAsyncResult asyncResult) => _inner.EndWrite(asyncResult);
#endif

            protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
        }

    }
}
