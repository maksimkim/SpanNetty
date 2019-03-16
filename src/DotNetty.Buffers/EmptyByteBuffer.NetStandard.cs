﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !NET40
namespace DotNetty.Buffers
{
    using System;
    using System.Buffers;

    partial class EmptyByteBuffer
    {
        public ReadOnlyMemory<byte> GetReadableMemory() => ReadOnlyMemory<byte>.Empty;
        public ReadOnlyMemory<byte> GetReadableMemory(int index, int count) => ReadOnlyMemory<byte>.Empty;

        public ReadOnlySpan<byte> GetReadableSpan() => ReadOnlySpan<byte>.Empty;
        public ReadOnlySpan<byte> GetReadableSpan(int index, int count) => ReadOnlySpan<byte>.Empty;

        public ReadOnlySequence<byte> GetSequence() => ReadOnlySequence<byte>.Empty;
        public ReadOnlySequence<byte> GetSequence(int index, int count) => ReadOnlySequence<byte>.Empty;

        public Memory<byte> FreeMemory => Memory<byte>.Empty;
        public Memory<byte> GetMemory(int sizeHintt = 0) => Memory<byte>.Empty;
        public Memory<byte> GetMemory(int index, int count) => Memory<byte>.Empty;

        public void Advance(int count) { }

        public Span<byte> Free => Span<byte>.Empty;
        public Span<byte> GetSpan(int sizeHintt = 0) => Span<byte>.Empty;
        public Span<byte> GetSpan(int index, int count) => Span<byte>.Empty;
    }
}
#endif