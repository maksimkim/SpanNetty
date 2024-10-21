using System.Buffers;

namespace DotNetty.Buffers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Xunit;
    using static ArrayPooled;

    /// <summary>
    /// Tests channel buffers
    /// </summary>
    public class ArrayPooledTests : IDisposable
    {
        static readonly IByteBuffer[] EmptyByteBuffer = new IByteBuffer[0];
        static readonly byte[][] EmptyBytes2D = new byte[0][];
        static readonly byte[] EmptyBytes = { };

        readonly Queue<IByteBuffer> freeLaterQueue;

        public ArrayPooledTests()
        {
            this.freeLaterQueue = new Queue<IByteBuffer>();
        }

        IByteBuffer FreeLater(IByteBuffer buf)
        {
            this.freeLaterQueue.Enqueue(buf);
            return buf;
        }

        public void Dispose()
        {
            for (; ; )
            {
                IByteBuffer buf = null;
                if (this.freeLaterQueue.Count > 0)
                {
                    buf = this.freeLaterQueue.Dequeue();
                }
                if (buf == null)
                {
                    break;
                }

                if (buf.ReferenceCount > 0)
                {
                    buf.Release(buf.ReferenceCount);
                }
            }
        }

        [Fact]
        public void CompositeWrappedBuffer()
        {
            IByteBuffer header = Buffer(12);
            IByteBuffer payload = Buffer(512);

            header.WriteBytes(new byte[12]);
            payload.WriteBytes(new byte[512]);

            IByteBuffer buffer = WrappedBuffer(header, payload);

            Assert.Equal(12, header.ReadableBytes);
            Assert.Equal(512, payload.ReadableBytes);

            Assert.Equal(12 + 512, buffer.ReadableBytes);
            Assert.Equal(2, buffer.IoBufferCount);

            buffer.Release();
        }

        [Fact]
        public void HashCode()
        {
            var map = new Dictionary<IByteBuffer, int> // buffer -> expected hashcode for the buffer data
            {
                { WrappedBuffer(EmptyBytes), 1 },
                { new byte[] { 1 }.WrapWithRenting(length: 1), 32 },
                { new byte[] { 2 }.WrapWithRenting(length: 1), 33 },
                { new byte[] { 0, 1 }.WrapWithRenting(length: 2), 962 },
                { new byte[] { 1, 2 }.WrapWithRenting(length: 2), 994 },
                { new byte[] { 0, 1, 2, 3, 4, 5 }.WrapWithRenting(length: 6), 63504931 },
                { new byte[] { 6, 7, 8, 9, 0, 1 }.WrapWithRenting(length: 6), unchecked((int)97180294697L) },
                { new byte[] { 255, 255, 255, 0xE1 }.WrapWithRenting(length: 4), 1 }
            };

            foreach (var item in map)
            {
                var buffer = item.Key;
                var expectedHashCode = item.Value;
                Assert.Equal(expectedHashCode, ByteBufferUtil.HashCode(buffer));
                buffer.Release();
            }
        }

        [Fact]
        public void Equals1()
        {
            // Different length.
            IByteBuffer a = new byte[] { 1 }.WrapWithRenting(0, 1);
            IByteBuffer b = new byte[] { 1, 2 }.WrapWithRenting(0, 2);
            Assert.False(ByteBufferUtil.Equals(a, b));
            a.Release();
            b.Release();

            // Same content, same firstIndex, short length.
            a = new byte[] { 1, 2, 3 }.WrapWithRenting(length: 3);
            b = new byte[] { 1, 2, 3 }.WrapWithRenting(length: 3);
            Assert.True(ByteBufferUtil.Equals(a, b));
            a.Release();
            b.Release();

            // Same content, different firstIndex, short length.
            a = new byte[] { 1, 2, 3 }.WrapWithRenting(length: 3);
            b = new byte[] { 0, 1, 2, 3, 4 }.WrapWithRenting(offset: 1, length: 3);
            Assert.True(ByteBufferUtil.Equals(a, b));
            a.Release();
            b.Release();

            // Different content, same firstIndex, short length.
            a = new byte[] { 1, 2, 3 }.WrapWithRenting(length: 3);
            b = new byte[] { 1, 2, 4 }.WrapWithRenting(length: 3);
            Assert.False(ByteBufferUtil.Equals(a, b));
            a.Release();
            b.Release();

            // Different content, different firstIndex, short length.
            a = new byte[] { 1, 2, 3 }.WrapWithRenting(length: 3);
            b = new byte[] { 0, 1, 2, 4, 5 }.WrapWithRenting(offset: 1, length: 3);
            Assert.False(ByteBufferUtil.Equals(a, b));
            a.Release();
            b.Release();

            // Same content, same firstIndex, long length.
            a = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }.WrapWithRenting();
            b = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }.WrapWithRenting();
            Assert.True(ByteBufferUtil.Equals(a, b));
            a.Release();
            b.Release();

            // Same content, different firstIndex, long length.
            a = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }.WrapWithRenting(length: 10);
            b = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }.WrapWithRenting(offset: 1, length: 10);
            Assert.True(ByteBufferUtil.Equals(a, b));
            a.Release();
            b.Release();

            // Different content, same firstIndex, long length.
            a = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }.WrapWithRenting();
            b = new byte[] { 1, 2, 3, 4, 6, 7, 8, 5, 9, 10 }.WrapWithRenting();
            Assert.False(ByteBufferUtil.Equals(a, b));
            a.Release();
            b.Release();

            // Different content, different firstIndex, long length.
            a = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }.WrapWithRenting(length: 10);
            b = new byte[] { 0, 1, 2, 3, 4, 6, 7, 8, 5, 9, 10, 11 }.WrapWithRenting(offset: 1, length: 10);
            Assert.False(ByteBufferUtil.Equals(a, b));
            a.Release();
            b.Release();
        }

        [Fact]
        public void Compare()
        {
            IList<IByteBuffer> expected = new List<IByteBuffer>();
            
            expected.Add(new byte[] { 1 }.WrapWithRenting(length: 1));
            expected.Add(new byte[] { 1, 2 }.WrapWithRenting(length: 2));
            expected.Add(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }.WrapWithRenting(length: 10));
            expected.Add(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }.WrapWithRenting(length: 12));
            expected.Add(new byte[] { 2 }.WrapWithRenting(length: 1));
            expected.Add(new byte[] { 2, 3 }.WrapWithRenting(length: 2));
            expected.Add(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }.WrapWithRenting(length: 10));
            expected.Add(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }.WrapWithRenting(length: 12));
            expected.Add(new byte[] { 2, 3, 4 }.WrapWithRenting(offset: 1, length: 1));
            expected.Add(new byte[] { 1, 2, 3, 4 }.WrapWithRenting(offset: 2, length: 2));
            expected.Add(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }.WrapWithRenting(offset: 1, length: 10));
            expected.Add(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }.WrapWithRenting(offset: 2, length: 12));
            expected.Add(new byte[] { 2, 3, 4, 5 }.WrapWithRenting(offset: 2, length: 1));
            expected.Add(new byte[] { 1, 2, 3, 4, 5 }.WrapWithRenting(offset: 3, length: 2));
            expected.Add(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }.WrapWithRenting(offset: 2, length: 10));
            expected.Add(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }.WrapWithRenting(offset: 3, length: 12));

            for (int i = 0; i < expected.Count; i++)
            {
                for (int j = 0; j < expected.Count; j++)
                {
                    if (i == j)
                    {
                        Assert.Equal(0, ByteBufferUtil.Compare(expected[i], expected[j]));
                    }
                    else if (i < j)
                    {
                        Assert.True(ByteBufferUtil.Compare(expected[i], expected[j]) < 0);
                    }
                    else
                    {
                        Assert.True(ByteBufferUtil.Compare(expected[i], expected[j]) > 0);
                    }
                }
            }
            foreach (IByteBuffer buffer in expected)
            {
                buffer.Release();
            }
        }

        [Fact]
        public void ShouldReturnEmptyBufferWhenLengthIsZero()
        {
            AssertSameAndRelease(Empty, WrappedBuffer(EmptyBytes));
            AssertSameAndRelease(Empty, WrappedBuffer(new byte[8], 0, 0));
            AssertSameAndRelease(Empty, WrappedBuffer(new byte[8], 8, 0));
            AssertSameAndRelease(Empty, WrappedBuffer(Empty));
            //AssertSameAndRelease(Empty, WrappedBuffer(EmptyBytes2D));
            //AssertSameAndRelease(Empty, WrappedBuffer(new[] { EmptyBytes }));
            AssertSameAndRelease(Empty, WrappedBuffer(EmptyByteBuffer));
            AssertSameAndRelease(Empty, WrappedBuffer(new [] { Buffer(0) }));
            AssertSameAndRelease(Empty, WrappedBuffer(Buffer(0), Buffer(0)));
            AssertSameAndRelease(Empty, CopiedBuffer(EmptyBytes));
            AssertSameAndRelease(Empty, CopiedBuffer(new byte[8], 0, 0));
            AssertSameAndRelease(Empty, CopiedBuffer(new byte[8], 8, 0));
            AssertSameAndRelease(Empty, CopiedBuffer(Empty));
            //Assert.Same(Empty, CopiedBuffer(EmptyBytes2D));
            //AssertSameAndRelease(Empty, CopiedBuffer(new[] { EmptyBytes }));
            AssertSameAndRelease(Empty, CopiedBuffer(EmptyByteBuffer));
            AssertSameAndRelease(Empty, CopiedBuffer(new [] { Buffer(0) }));
            AssertSameAndRelease(Empty, CopiedBuffer(Buffer(0), Buffer(0)));
        }

        [Fact]
        public void Compare2()
        {
            Assert.True(ByteBufferUtil.Compare(
                    WrappedBuffer(new[] { (byte)0xFF, (byte)0xFF, (byte)0xFF, (byte)0xFF }),
                    WrappedBuffer(new [] { (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00 }))
                > 0);

            Assert.True(ByteBufferUtil.Compare(
                    WrappedBuffer(new [] { (byte)0xFF }),
                    WrappedBuffer(new [] { (byte)0x00 }))
                > 0);
        }

        [Fact]
        public void ShouldAllowEmptyBufferToCreateCompositeBuffer()
        {
            IByteBuffer buf = WrappedBuffer(Empty, WrappedBuffer(new byte[16]), Empty);
            try
            {
                Assert.Equal(16, buf.Capacity);
            }
            finally
            {
                buf.Release();
            }
        }

        [Fact]
        public void WrappedBuffers()
        {
            Assert.Equal(
                new byte[] { 1, 2, 3 }.WrapWithRenting(length: 3),
                WrappedBuffer(new [] { new byte[] { 1, 2, 3 }.WrapWithRenting(length: 3) })
            );

            Assert.Equal(
                new byte[] { 1, 2, 3 }.WrapWithRenting(length: 3),
                this.FreeLater(WrappedBuffer(
                    new byte[] { 1 }.WrapWithRenting(length: 1),
                    new byte[] { 2 }.WrapWithRenting(length: 1), 
                    new byte[] { 3 }.WrapWithRenting(length: 1)
                ))
            );
        }

        [Fact]
        public void SingleWrappedByteBufReleased()
        {
            IByteBuffer buf = Buffer(12).WriteByte(0);
            IByteBuffer wrapped = WrappedBuffer(buf);
            Assert.True(wrapped.Release());
            Assert.Equal(0, buf.ReferenceCount);
        }

        [Fact]
        public void SingleUnReadableWrappedByteBufReleased()
        {
            IByteBuffer buf = Buffer(12);
            IByteBuffer wrapped = WrappedBuffer(buf);
            Assert.False(wrapped.Release()); // Empty Buffer cannot be released
            Assert.Equal(0, buf.ReferenceCount);
        }

        [Fact]
        public void MultiByteBufReleased()
        {
            IByteBuffer buf1 = Buffer(12).WriteByte(0);
            IByteBuffer buf2 = Buffer(12).WriteByte(0);
            IByteBuffer wrapped = WrappedBuffer(16, buf1, buf2);
            Assert.True(wrapped.Release());
            Assert.Equal(0, buf1.ReferenceCount);
            Assert.Equal(0, buf2.ReferenceCount);
        }

        [Fact]
        public void MultiUnReadableByteBufReleased()
        {
            IByteBuffer buf1 = Buffer(12);
            IByteBuffer buf2 = Buffer(12);
            IByteBuffer wrapped = WrappedBuffer(16, buf1, buf2);
            Assert.False(wrapped.Release()); // Empty Buffer cannot be released
            Assert.Equal(0, buf1.ReferenceCount);
            Assert.Equal(0, buf2.ReferenceCount);
        }

        [Fact]
        public void CopiedBufferUtf8()
        {
            CopiedBufferCharSequence("Some UTF_8 like äÄ∏ŒŒ", Encoding.UTF8);
        }

        [Fact]
        public void CopiedBufferAscii()
        {
            CopiedBufferCharSequence("Some US_ASCII", Encoding.ASCII);
        }

        [Fact]
        public void CopiedBufferSomeOtherCharset()
        {
            CopiedBufferCharSequence("Some ISO_8859_1", Encoding.GetEncoding("iso-8859-1"));
        }

        private static void CopiedBufferCharSequence(string sequence, Encoding charset)
        {
            IByteBuffer copied = Unpooled.CopiedBuffer(sequence, charset);
            try
            {
                Assert.Equal(sequence, copied.ToString(charset));
            }
            finally
            {
                copied.Release();
            }
        }

        [Fact]
        public void CopiedBuffers()
        {
            Assert.Equal(
                WrappedBuffer(new byte[] { 1, 2, 3 }),
                CopiedBuffer(new [] { WrappedBuffer(new byte[] { 1, 2, 3 }) }));

            Assert.Equal(
                WrappedBuffer(new byte[] { 1, 2, 3 }),
                CopiedBuffer(
                    WrappedBuffer(new byte[] { 1 }),
                    WrappedBuffer(new byte[] { 2 }), 
                    WrappedBuffer(new byte[] { 3 })));
        }

        [Fact]
        public void HexDump()
        {
            Assert.Equal("", ByteBufferUtil.HexDump(Empty));

            IByteBuffer buffer = new byte[] { 0x12, 0x34, 0x56 }.WrapWithRenting(length: 3);
            Assert.Equal("123456", ByteBufferUtil.HexDump(buffer));
            buffer.Release();

            buffer = WrappedBuffer(new byte[]
            {
                0x12, 0x34, 0x56, 0x78,
                0x90, 0xAB, 0xCD, 0xEF
            }.WrapWithRenting(length: 8));
            Assert.Equal("1234567890abcdef", ByteBufferUtil.HexDump(buffer));
        }

        [Fact]
        public void WrapSingleInt()
        {
            IByteBuffer buffer = CopyInt(42);
            //Assert.Equal(4, buffer.Capacity);
            Assert.Equal(42, buffer.ReadInt());
            Assert.False(buffer.IsReadable());
        }

        [Fact]
        public void WrapInt()
        {
            IByteBuffer buffer = CopyInt(1, 4);
            //Assert.Equal(8, buffer.Capacity);
            Assert.Equal(1, buffer.ReadInt());
            Assert.Equal(4, buffer.ReadInt());
            Assert.False(buffer.IsReadable());
            
            Assert.Equal(0, CopyInt(null).Capacity);
            Assert.Equal(0, CopyInt(new int[] { }).Capacity);
        }

        [Fact]
        public void WrapSingleShort()
        {
            IByteBuffer buffer = CopyShort(42);
            //Assert.Equal(2, buffer.Capacity);
            Assert.Equal(42, buffer.ReadShort());
            Assert.False(buffer.IsReadable());
        }

        [Fact]
        public void WrapShortFromShortArray()
        {
            IByteBuffer buffer = CopyShort((short)1, (short)4);
            //Assert.Equal(4, buffer.Capacity);
            Assert.Equal(1, buffer.ReadShort());
            Assert.Equal(4, buffer.ReadShort());
            Assert.False(buffer.IsReadable());

            Assert.Equal(0, CopyShort(default(short[])).Capacity);
            Assert.Equal(0, CopyShort(new short[] { }).Capacity);
        }

        [Fact]
        public void WrapShortFromIntArray()
        {
            IByteBuffer buffer = CopyShort(1, 4);
            //Assert.Equal(4, buffer.Capacity);
            Assert.Equal(1, buffer.ReadShort());
            Assert.Equal(4, buffer.ReadShort());
            Assert.False(buffer.IsReadable());

            Assert.Equal(0, CopyShort(default(int[])).Capacity);
            Assert.Equal(0, CopyShort(new int[] { }).Capacity);
        }

        [Fact]
        public void WrapSingleMedium()
        {
            IByteBuffer buffer = CopyMedium(42);
            //Assert.Equal(3, buffer.Capacity);
            Assert.Equal(42, buffer.ReadMedium());
            Assert.False(buffer.IsReadable());
        }

        [Fact]
        public void WrapMedium()
        {
            IByteBuffer buffer = CopyMedium(1, 4);
            //Assert.Equal(6, buffer.Capacity);
            Assert.Equal(1, buffer.ReadMedium());
            Assert.Equal(4, buffer.ReadMedium());
            Assert.False(buffer.IsReadable());
            buffer.Release();

            Assert.Equal(0, CopyMedium(null).Capacity);
            Assert.Equal(0, CopyMedium(new int[] { }).Capacity);
        }

        [Fact]
        public void WrapSingleLong()
        {
            IByteBuffer buffer = CopyLong(42);
            //Assert.Equal(8, buffer.Capacity);
            Assert.Equal(42, buffer.ReadLong());
            Assert.False(buffer.IsReadable());
            buffer.Release();
        }

        [Fact]
        public void WrapLong()
        {
            IByteBuffer buffer = CopyLong(1, 4);
            //Assert.Equal(16, buffer.Capacity);
            Assert.Equal(1, buffer.ReadLong());
            Assert.Equal(4, buffer.ReadLong());
            Assert.False(buffer.IsReadable());
            buffer.Release();

            Assert.Equal(0, CopyLong(null).Capacity);
            Assert.Equal(0, CopyLong(new long[] { }).Capacity);
        }

        [Fact]
        public void WrapSingleFloat()
        {
            IEqualityComparer<float> comparer = new ApproximateComparer(0.01);
            IByteBuffer buffer = CopyFloat(42);
            //Assert.Equal(4, buffer.Capacity);
            Assert.Equal(42, buffer.ReadFloat(), comparer);
            Assert.False(buffer.IsReadable());
        }

        [Fact]
        public void WrapFloat()
        {
            IEqualityComparer<float> comparer = new ApproximateComparer(0.01);
            IByteBuffer buffer = CopyFloat(1, 4);
            //Assert.Equal(8, buffer.Capacity);
            Assert.Equal(1, buffer.ReadFloat(), comparer);
            Assert.Equal(4, buffer.ReadFloat(), comparer);
            Assert.False(buffer.IsReadable());

            Assert.Equal(0, CopyFloat(null).Capacity);
            Assert.Equal(0, CopyFloat(new float[] { }).Capacity);
        }

        [Fact]
        public void WrapSingleDouble()
        {
            IEqualityComparer<double> comparer = new ApproximateComparer(0.01);
            IByteBuffer buffer = CopyDouble(42);
            //Assert.Equal(8, buffer.Capacity);
            Assert.Equal(42, buffer.ReadDouble(), comparer);
            Assert.False(buffer.IsReadable());
        }

        [Fact]
        public void WrapDouble()
        {
            IEqualityComparer<double> comparer = new ApproximateComparer(0.01);
            IByteBuffer buffer = CopyDouble(1, 4);
            //Assert.Equal(16, buffer.Capacity);
            Assert.Equal(1, buffer.ReadDouble(), comparer);
            Assert.Equal(4, buffer.ReadDouble(), comparer);
            Assert.False(buffer.IsReadable());

            Assert.Equal(0, CopyDouble(null).Capacity);
            Assert.Equal(0, CopyDouble(new double[] { }).Capacity);
        }

        [Fact]
        public void WrapBoolean()
        {
            IByteBuffer buffer = CopyBoolean(true, false);
            //Assert.Equal(2, buffer.Capacity);
            Assert.True(buffer.ReadBoolean());
            Assert.False(buffer.ReadBoolean());
            Assert.False(buffer.IsReadable());

            Assert.Equal(0, CopyBoolean(null).Capacity);
            Assert.Equal(0, CopyBoolean(new bool[] { }).Capacity);
        }

        [Fact]
        public void SkipBytesNegativeLength()
        {
            IByteBuffer buf = this.FreeLater(Buffer(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => buf.SkipBytes(-1));
        }

        [Fact]
        public void WrapByteBufArrayStartsWithNonReadable()
        {
            IByteBuffer buffer1 = Buffer(8);
            IByteBuffer buffer2 = Buffer(8).WriteZero(8); // Ensure the IByteBuffer is readable.
            IByteBuffer buffer3 = Buffer(8);
            IByteBuffer buffer4 = Buffer(8).WriteZero(8); // Ensure the IByteBuffer is readable.

            IByteBuffer wrapped = WrappedBuffer(buffer1, buffer2, buffer3, buffer4);
            Assert.Equal(16, wrapped.ReadableBytes);
            Assert.True(wrapped.Release());
            Assert.Equal(0, buffer1.ReferenceCount);
            Assert.Equal(0, buffer2.ReferenceCount);
            Assert.Equal(0, buffer3.ReferenceCount);
            Assert.Equal(0, buffer4.ReferenceCount);
            Assert.Equal(0, wrapped.ReferenceCount);
        }

        static void AssertSameAndRelease(IByteBuffer expected, IByteBuffer actual)
        {
            Assert.Same(expected, actual);
            expected.Release();
            actual.Release();
        }
    }
    
    static class ArrayPoolingHelpers
    {
        public static IByteBuffer WrapWithRenting(this byte[] arr, int offset = 0, int? length = null)
        {
            var rentedArr = arr.RentThisData();
            length ??= arr.Length;
            return WrappedBuffer(rentedArr, offset, length.Value);
        }
        
        private static byte[] RentThisData(this byte[] arr)
        {          
            var rentedArr = DefaultArrayPool.Rent(arr.Length);
            for (int i = 0; i < arr.Length; i++)
            {
                rentedArr[i] = arr[i];
            }

            return rentedArr;
        } 
    }
}
