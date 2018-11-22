﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Common
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using DotNetty.Common.Internal;
    using DotNetty.Common.Internal.Logging;
    using DotNetty.Common.Utilities;

    public class ResourceLeakDetector
    {
        const string PropLevel = "io.netty.leakDetection.level";
        const DetectionLevel DefaultLevel = DetectionLevel.Simple;

        const string PropTargetRecords = "io.netty.leakDetection.targetRecords";
        const int DefaultTargetRecords = 4;

        static readonly int TargetRecords;

        /// <summary>
        ///    Represents the level of resource leak detection.
        /// </summary>
        public enum DetectionLevel
        {
            /// <summary>
            ///     Disables resource leak detection.
            /// </summary>
            Disabled,

            /// <summary>
            ///     Enables simplistic sampling resource leak detection which reports there is a leak or not,
            ///     at the cost of small overhead (default).
            /// </summary>
            Simple,

            /// <summary>
            ///     Enables advanced sampling resource leak detection which reports where the leaked object was accessed
            ///     recently at the cost of high overhead.
            /// </summary>
            Advanced,

            /// <summary>
            ///     Enables paranoid resource leak detection which reports where the leaked object was accessed recently,
            ///     at the cost of the highest possible overhead (for testing purposes only).
            /// </summary>
            Paranoid
        }

        static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance<ResourceLeakDetector>();

        static ResourceLeakDetector()
        {
            // If new property name is present, use it
            string levelStr = SystemPropertyUtil.Get(PropLevel, DefaultLevel.ToString());
            if (!Enum.TryParse(levelStr, true, out DetectionLevel level))
            {
                level = DefaultLevel;
            }

            TargetRecords = SystemPropertyUtil.GetInt(PropTargetRecords, DefaultTargetRecords);
            Level = level;

            if (Logger.DebugEnabled)
            {
                Logger.Debug("-D{}: {}", PropLevel, level.ToString().ToLower());
                Logger.Debug("-D{}: {}", PropTargetRecords, TargetRecords);
            }
        }

        // Should be power of two.
        const int DefaultSamplingInterval = 128;

        /// Returns <c>true</c> if resource leak detection is enabled.
        public static bool Enabled => Level > DetectionLevel.Disabled;

        /// <summary>
        ///     Gets or sets resource leak detection level
        /// </summary>
        public static DetectionLevel Level { get; set; }

        readonly ConditionalWeakTable<object, GCNotice> gcNotificationMap = new ConditionalWeakTable<object, GCNotice>();
        readonly ConcurrentDictionary<string, bool> reportedLeaks = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        readonly string resourceType;
        readonly int samplingInterval;

        public ResourceLeakDetector(string resourceType)
            : this(resourceType, DefaultSamplingInterval)
        {
        }

        public ResourceLeakDetector(string resourceType, int samplingInterval)
        {
            if (null == resourceType) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.resourceType); }
            if (samplingInterval <= 0) { ThrowHelper.ThrowArgumentException_Positive(samplingInterval, ExceptionArgument.samplingInterval); }

            this.resourceType = resourceType;
            this.samplingInterval = samplingInterval;
        }

        public static ResourceLeakDetector Create<T>() => new ResourceLeakDetector(StringUtil.SimpleClassName<T>());

        /// <summary>
        ///     Creates a new <see cref="IResourceLeakTracker" /> which is expected to be closed
        ///     when the
        ///     related resource is deallocated.
        /// </summary>
        /// <returns>the <see cref="IResourceLeakTracker" /> or <c>null</c></returns>
        public IResourceLeakTracker Track(object obj)
        {
            DetectionLevel level = Level;
            if (level == DetectionLevel.Disabled)
            {
                return null;
            }

            if (level < DetectionLevel.Paranoid)
            {
                if ((PlatformDependent.GetThreadLocalRandom().Next(this.samplingInterval)) == 0)
                {
                    return new DefaultResourceLeak(this, obj);
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return new DefaultResourceLeak(this, obj);
            }
        }

        void ReportLeak(DefaultResourceLeak resourceLeak)
        {
            string records = resourceLeak.ToString();
            if (this.reportedLeaks.TryAdd(records, true))
            {
                if (records.Length == 0)
                {
                    this.ReportUntracedLeak(this.resourceType);
                }
                else
                {
                    this.ReportTracedLeak(this.resourceType, records);
                }
            }
        }

        protected void ReportTracedLeak(string type, string records)
        {
            Logger.Error(
                "LEAK: {}.Release() was not called before it's garbage-collected. " +
                "See http://netty.io/wiki/reference-counted-objects.html for more information.{}",
                type, records);
        }

        protected void ReportUntracedLeak(string type)
        {
            Logger.Error("LEAK: {}.release() was not called before it's garbage-collected. " +
                "Enable advanced leak reporting to find out where the leak occurred. " +
                "To enable advanced leak reporting, " +
                "specify the JVM option '-D{}={}' or call {}.setLevel() " +
                "See http://netty.io/wiki/reference-counted-objects.html for more information.",
                type, PropLevel, DetectionLevel.Advanced.ToString().ToLower(), StringUtil.SimpleClassName(this));
        }

        sealed class DefaultResourceLeak : IResourceLeakTracker
        {
            readonly ResourceLeakDetector owner;

            RecordEntry head;
            long droppedRecords;

            public DefaultResourceLeak(ResourceLeakDetector owner, object referent)
            {
                Debug.Assert(referent != null);

                this.owner = owner;
                if (owner.gcNotificationMap.TryGetValue(referent, out GCNotice existingNotice))
                {
                    existingNotice.Rearm(this);
                }
                else
                {
                    owner.gcNotificationMap.Add(referent, new GCNotice(this, referent));
                }
                this.head = RecordEntry.Bottom;
            }

            public void Record() => this.Record0(null);

            public void Record(object hint) => this.Record0(hint);

            void Record0(object hint)
            {
                // Check TARGET_RECORDS > 0 here to avoid similar check before remove from and add to lastRecords
                if (TargetRecords > 0)
                {
                    string stackTrace = Environment.StackTrace;

                    var thisHead = Volatile.Read(ref this.head);
                    RecordEntry oldHead;
                    RecordEntry prevHead;
                    RecordEntry newHead;
                    bool dropped;
                    do
                    {
                        if ((prevHead = oldHead = thisHead) == null)
                        {
                            // already closed.
                            return;
                        }
                        int numElements = thisHead.Pos + 1;
                        if (numElements >= TargetRecords)
                        {
                            int backOffFactor = Math.Min(numElements - TargetRecords, 30);
                            dropped = PlatformDependent.GetThreadLocalRandom().Next(1 << backOffFactor) != 0;
                            if (dropped)
                            {
                                prevHead = thisHead.Next;
                            }
                        }
                        else
                        {
                            dropped = false;
                        }
                        newHead = hint != null ? new RecordEntry(prevHead, stackTrace, hint) : new RecordEntry(prevHead, stackTrace);
                        thisHead = Interlocked.CompareExchange(ref this.head, newHead, thisHead);
                    }
                    while (thisHead != oldHead);
                    if (dropped)
                    {
                        Interlocked.Increment(ref this.droppedRecords);
                    }
                }
            }

            public bool Close(object trackedObject)
            {
                if (this.owner.gcNotificationMap.TryGetValue(trackedObject, out GCNotice notice))
                {
                    // The close is called by byte buffer release, in this case
                    // we suppress the GCNotice finalize to prevent false positive
                    // report where the byte buffer instance gets reused by thread
                    // local cache and the existing GCNotice finalizer still holds 
                    // the same byte buffer instance.
                    GC.SuppressFinalize(notice);

                    Debug.Assert(this.owner.gcNotificationMap.Remove(trackedObject));
                    Interlocked.Exchange(ref this.head, null);
                    return true;
                }

                return false;
            }

            // This is called from GCNotice finalizer 
            internal void CloseFinal(object trackedObject)
            {
                if (this.owner.gcNotificationMap.Remove(trackedObject)
                    && Volatile.Read(ref this.head) != null)
                {
                    this.owner.ReportLeak(this);
                }
            }

            public override string ToString()
            {
                RecordEntry oldHead = Interlocked.Exchange(ref this.head, null);
                if (oldHead == null)
                {
                    // Already closed
                    return string.Empty;
                }

                long dropped = Interlocked.Read(ref this.droppedRecords);
                int duped = 0;

                int present = oldHead.Pos + 1;
                // Guess about 2 kilobytes per stack trace
                var buf = new StringBuilder(present * 2048);
                buf.Append(StringUtil.Newline);
                buf.Append("Recent access records: ").Append(StringUtil.Newline);

                int i = 1;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (; oldHead != RecordEntry.Bottom; oldHead = oldHead.Next)
                {
                    string s = oldHead.ToString();
                    if (seen.Add(s))
                    {
                        if (oldHead.Next == RecordEntry.Bottom)
                        {
                            buf.Append("Created at:").Append(StringUtil.Newline).Append(s);
                        }
                        else
                        {
                            buf.Append('#').Append(i++).Append(':').Append(StringUtil.Newline).Append(s);
                        }
                    }
                    else
                    {
                        duped++;
                    }
                }

                if (duped > 0)
                {
                    buf.Append(": ")
                        .Append(duped)
                        .Append(" leak records were discarded because they were duplicates")
                        .Append(StringUtil.Newline);
                }

                if (dropped > 0)
                {
                    buf.Append(": ")
                        .Append(dropped)
                        .Append(" leak records were discarded because the leak record count is targeted to ")
                        .Append(TargetRecords)
                        .Append(". Use system property ")
                        .Append(PropTargetRecords)
                        .Append(" to increase the limit.")
                        .Append(StringUtil.Newline);
                }

                buf.Length = buf.Length - StringUtil.Newline.Length;
                return buf.ToString();
            }
        }

        // Record
        sealed class RecordEntry
        {
            internal static readonly RecordEntry Bottom = new RecordEntry();

            readonly string hintString;
            internal readonly RecordEntry Next;
            internal readonly int Pos;
            readonly string stackTrace;

            internal RecordEntry(RecordEntry next, string stackTrace, object hint)
            {
                // This needs to be generated even if toString() is never called as it may change later on.
                this.hintString = hint is IResourceLeakHint leakHint ? leakHint.ToHintString() : null;
                this.Next = next;
                this.Pos = next.Pos + 1;
                this.stackTrace = stackTrace;
            }

            internal RecordEntry(RecordEntry next, string stackTrace)
            {
                this.hintString = null;
                this.Next = next;
                this.Pos = next.Pos + 1;
                this.stackTrace = stackTrace;
            }

            // Used to terminate the stack
            RecordEntry()
            {
                this.hintString = null;
                this.Next = null;
                this.Pos = -1;
                this.stackTrace = string.Empty;
            }

            public override string ToString()
            {
                var buf = new StringBuilder(2048);
                if (this.hintString != null)
                {
                    buf.Append("\tHint: ").Append(this.hintString).Append(StringUtil.Newline);
                }

                // TODO: Use StackTrace class and support excludedMethods NETStandard2.0
                // Append the stack trace.
                buf.Append(this.stackTrace).Append(StringUtil.Newline);
                return buf.ToString();
            }
        }

        class GCNotice
        {
            // ConditionalWeakTable
            //
            // Lifetimes of keys and values:
            //
            //    Inserting a key and value into the dictonary will not
            //    prevent the key from dying, even if the key is strongly reachable
            //    from the value.
            //
            //    Prior to ConditionalWeakTable, the CLR did not expose
            //    the functionality needed to implement this guarantee.
            //
            //    Once the key dies, the dictionary automatically removes
            //    the key/value entry.
            //
            DefaultResourceLeak leak;
            object referent;

            public GCNotice(DefaultResourceLeak leak, object referent)
            {
                this.leak = leak;
                this.referent = referent;
            }

            ~GCNotice()
            {
                object trackedObject = this.referent;
                this.referent = null;
                this.leak.CloseFinal(trackedObject);
            }

            public void Rearm(DefaultResourceLeak newLeak)
            {
                DefaultResourceLeak oldLeak = Interlocked.Exchange(ref this.leak, newLeak);
                oldLeak.CloseFinal(this.referent);
            }
        }
    }
}