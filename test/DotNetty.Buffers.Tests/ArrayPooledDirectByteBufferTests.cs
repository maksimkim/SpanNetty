using System.Buffers;
using System.Text;
using DotNetty.Common.Utilities;

namespace DotNetty.Buffers.Tests
{
    public sealed class ArrayPooledDirectByteBufferTests : AbstractArrayPooledByteBufferTests
    {
        protected override IByteBuffer NewBuffer(int length, int maxCapacity) => ArrayPooledByteBufferAllocator.Default.DirectBuffer(length, maxCapacity);

        protected override void SetCharSequenceNoExpand(Encoding encoding)
        {   
            // by default ArrayPool buffers between 1 and 16 bytes are combined,
            // so requesting length of 1 will still result in 16 bytes array
            var array = ArrayPooled.DefaultArrayPool.Rent(1);
            var buf = ArrayPooledUnsafeDirectByteBuffer.NewInstance(ArrayPooled.Allocator, ArrayPooled.DefaultArrayPool, array, array.Length, array.Length);
            try
            {
                // char sequence is longer than rented array length
                buf.SetCharSequence(0, new StringCharSequence(TestCharSequence), encoding);
            }
            finally
            {
                buf.Release();
            }
        }
    }
}
