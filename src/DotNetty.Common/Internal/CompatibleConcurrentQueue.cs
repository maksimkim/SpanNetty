﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Common.Internal
{
    using System.Collections.Concurrent;

    public class CompatibleConcurrentQueue<T> : ConcurrentQueue<T>, IQueue<T>
    {
        public bool TryEnqueue(T element)
        {
            Enqueue(element);
            return true;
        }

        void IQueue<T>.Clear()
        {
            //T item;
            while (TryDequeue(out _)) { }
        }

        public bool NonEmpty => !IsEmpty;
    }
}