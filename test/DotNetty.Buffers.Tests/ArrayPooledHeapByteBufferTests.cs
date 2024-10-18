using System.Buffers;
using System.Text;
using DotNetty.Common.Utilities;

namespace DotNetty.Buffers.Tests
{
    public sealed class ArrayPooledHeapByteBufferTests : AbstractArrayPooledByteBufferTests
    {
        protected override IByteBuffer NewBuffer(int length, int maxCapacity) => ArrayPooledByteBufferAllocator.Default.HeapBuffer(length, maxCapacity);

        protected override void SetCharSequenceNoExpand(Encoding encoding)
        {
            // by default ArrayPool buffers between 1 and 16 bytes are combined,
            // so requesting length of 1 will still result in 16 bytes array
            var array = ArrayPool<byte>.Shared.Rent(1);
            var buf = ArrayPooledHeapByteBuffer.NewInstance(ArrayPooled.Allocator, ArrayPooled.DefaultArrayPool, array, array.Length, array.Length);
            try
            {
                buf.SetCharSequence(0, new StringCharSequence(TestCharSequence), encoding);
            }
            finally
            {
                buf.Release();
            }
        }
    }
}