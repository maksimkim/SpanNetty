﻿
using DotNetty.Common.Tests.Internal.Logging;
using DotNetty.Tests.Common;
using Xunit.Abstractions;

namespace DotNetty.Codecs.Http2.Tests
{
    using System;
    using DotNetty.Common.Utilities;
    using Xunit;

    public class ReadOnlyHttp2HeadersTest : TestBase
    {
        [Fact]
        [BeforeTest]
        public void NotKeyValuePairThrows()
        {
            Assert.Throws<ArgumentException>(() => ReadOnlyHttp2Headers.Trailers(false, new AsciiString[] { null }));
        }

        [Fact]
        [BeforeTest]
        public void NullTrailersNotAllowed()
        {
            Assert.Throws<NullReferenceException>(() => ReadOnlyHttp2Headers.Trailers(false, (AsciiString[])null));
        }

        [Fact]
        [BeforeTest]
        public void NullHeaderNameNotChecked()
        {
            ReadOnlyHttp2Headers.Trailers(false, null, null);
        }

        [Fact]
        [BeforeTest]
        public void NullHeaderNameValidated()
        {
            Assert.Throws<Http2Exception>(() => ReadOnlyHttp2Headers.Trailers(true, null, new AsciiString("foo")));
        }

        [Fact]
        [BeforeTest]
        public void PseudoHeaderNotAllowedAfterNonPseudoHeaders()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                ReadOnlyHttp2Headers.Trailers(true, new AsciiString(":name"), new AsciiString("foo"),
                                              new AsciiString("othername"), new AsciiString("goo"),
                                              new AsciiString(":pseudo"), new AsciiString("val"));
            });
        }

        [Fact]
        [BeforeTest]
        public void NullValuesAreNotAllowed()
        {
            Assert.Throws<ArgumentException>(() => ReadOnlyHttp2Headers.Trailers(true, new AsciiString("foo"), null));
        }

        [Fact]
        [BeforeTest]
        public void EmptyHeaderNameAllowed()
        {
            ReadOnlyHttp2Headers.Trailers(false, AsciiString.Empty, new AsciiString("foo"));
        }

        [Fact]
        [BeforeTest]
        public void TestPseudoHeadersMustComeFirstWhenIteratingServer()
        {
            var headers = NewServerHeaders();
            DefaultHttp2HeadersTest.VerifyPseudoHeadersFirst(headers);
        }

        [Fact]
        [BeforeTest]
        public void TestPseudoHeadersMustComeFirstWhenIteratingClient()
        {
            var headers = NewClientHeaders();
            DefaultHttp2HeadersTest.VerifyPseudoHeadersFirst(headers);
        }

        [Fact]
        [BeforeTest]
        public void TestIteratorReadOnlyClient()
        {
            Assert.Throws<NotSupportedException>(() => TestIteratorReadOnly(NewClientHeaders()));
        }

        [Fact]
        [BeforeTest]
        public void TestIteratorReadOnlyServer()
        {
            Assert.Throws<NotSupportedException>(() => TestIteratorReadOnly(NewServerHeaders()));
        }

        [Fact]
        [BeforeTest]
        public void TestIteratorReadOnlyTrailers()
        {
            Assert.Throws<NotSupportedException>(() => TestIteratorReadOnly(NewTrailers()));
        }

        [Fact]
        [BeforeTest]
        public void TestIteratorEntryReadOnlyClient()
        {
            Assert.Throws<NotSupportedException>(() => TestIteratorEntryReadOnly(NewClientHeaders()));
        }

        [Fact]
        [BeforeTest]
        public void TestIteratorEntryReadOnlyServer()
        {
            Assert.Throws<NotSupportedException>(() => TestIteratorEntryReadOnly(NewServerHeaders()));
        }

        [Fact]
        [BeforeTest]
        public void TestIteratorEntryReadOnlyTrailers()
        {
            Assert.Throws<NotSupportedException>(() => TestIteratorEntryReadOnly(NewTrailers()));
        }

        [Fact]
        [BeforeTest]
        public void TestSize()
        {
            var headers = NewTrailers();
            Assert.Equal(OtherHeaders().Length / 2, headers.Size);
        }

        [Fact]
        [BeforeTest]
        public void TestIsNotEmpty()
        {
            var headers = NewTrailers();
            Assert.False(headers.IsEmpty);
        }

        [Fact]
        [BeforeTest]
        public void TestIsEmpty()
        {
            var headers = ReadOnlyHttp2Headers.Trailers(false);
            Assert.True(headers.IsEmpty);
        }

        [Fact]
        [BeforeTest]
        public void TestContainsName()
        {
            var headers = NewClientHeaders();
            Assert.True(headers.Contains((AsciiString)"Name1"));
            Assert.True(headers.Contains(PseudoHeaderName.Path.Value));
            Assert.False(headers.Contains(PseudoHeaderName.Status.Value));
            Assert.False(headers.Contains((AsciiString)"a missing header"));
        }

        [Fact]
        [BeforeTest]
        public void TestContainsNameAndValue()
        {
            var headers = NewClientHeaders();
            Assert.True(headers.Contains((AsciiString)"Name1", (AsciiString)"value1"));
            Assert.False(headers.Contains((AsciiString)"Name1", (AsciiString)"Value1"));
            Assert.True(headers.Contains((AsciiString)"name2", (AsciiString)"Value2", true));
            Assert.False(headers.Contains((AsciiString)"name2", (AsciiString)"Value2", false));
            Assert.True(headers.Contains(PseudoHeaderName.Path.Value, (AsciiString)"/foo"));
            Assert.False(headers.Contains(PseudoHeaderName.Status.Value, (AsciiString)"200"));
            Assert.False(headers.Contains((AsciiString)"a missing header", (AsciiString)"a missing value"));
        }

        [Fact]
        [BeforeTest]
        public void TestGet()
        {
            var headers = NewClientHeaders();
            Assert.True(AsciiString.ContentEqualsIgnoreCase((AsciiString)"value1", headers.Get((AsciiString)"Name1", null)));
            Assert.True(AsciiString.ContentEqualsIgnoreCase((AsciiString)"/foo",
                       headers.Get(PseudoHeaderName.Path.Value, null)));
            Assert.Null(headers.Get(PseudoHeaderName.Status.Value, null));
            Assert.Null(headers.Get((AsciiString)"a missing header", null));
        }

        [Fact]
        [BeforeTest]
        public void TestClientOtherValueIterator()
        {
            TestValueIteratorSingleValue(NewClientHeaders(), (AsciiString)"name2", (AsciiString)"value2");
        }

        [Fact]
        [BeforeTest]
        public void TestClientPsuedoValueIterator()
        {
            TestValueIteratorSingleValue(NewClientHeaders(), (AsciiString)":path", (AsciiString)"/foo");
        }

        [Fact]
        [BeforeTest]
        public void TestServerPsuedoValueIterator()
        {
            TestValueIteratorSingleValue(NewServerHeaders(), (AsciiString)":status", (AsciiString)"200");
        }

        [Fact]
        [BeforeTest]
        public void TestEmptyValueIterator()
        {
            var headers = NewServerHeaders();
            var values = headers.GetAll((AsciiString)"foo");
            Assert.Empty(values);
        }

        [Fact]
        [BeforeTest]
        public void TestIteratorMultipleValues()
        {
            var headers = ReadOnlyHttp2Headers.ServerHeaders(false, new AsciiString("200"), new AsciiString[] {
                new AsciiString("name2"), new AsciiString("value1"),
                new AsciiString("name1"), new AsciiString("value2"),
                new AsciiString("name2"), new AsciiString("value3")
            });
            var itr = headers.GetAll((AsciiString)"name2").GetEnumerator();
            Assert.True(itr.MoveNext());
            Assert.True(AsciiString.ContentEqualsIgnoreCase((AsciiString)"value1", itr.Current));
            Assert.True(itr.MoveNext());
            Assert.True(AsciiString.ContentEqualsIgnoreCase((AsciiString)"value3", itr.Current));
            Assert.False(itr.MoveNext());
        }

        private static void TestValueIteratorSingleValue(IHttp2Headers headers, ICharSequence name, ICharSequence value)
        {
            var itr = headers.GetAll(name).GetEnumerator();
            Assert.True(itr.MoveNext());
            Assert.True(AsciiString.ContentEqualsIgnoreCase(value, itr.Current));
            Assert.False(itr.MoveNext());
        }

        private static void TestIteratorReadOnly(IHttp2Headers headers)
        {
            var enumerator = headers.GetEnumerator();
            Assert.True(enumerator.MoveNext());
            headers.Remove(enumerator.Current.Key);
        }

        private static void TestIteratorEntryReadOnly(IHttp2Headers headers)
        {
            var enumerator = headers.GetEnumerator();
            Assert.True(enumerator.MoveNext());
            enumerator.Current.SetValue((AsciiString)"foo");
        }

        private static ReadOnlyHttp2Headers NewServerHeaders()
        {
            return ReadOnlyHttp2Headers.ServerHeaders(false, new AsciiString("200"), OtherHeaders());
        }

        private static ReadOnlyHttp2Headers NewClientHeaders()
        {
            return ReadOnlyHttp2Headers.ClientHeaders(false, new AsciiString("meth"), new AsciiString("/foo"),
                    new AsciiString("schemer"), new AsciiString("respect_my_authority"), OtherHeaders());
        }

        private static ReadOnlyHttp2Headers NewTrailers()
        {
            return ReadOnlyHttp2Headers.Trailers(false, OtherHeaders());
        }

        private static AsciiString[] OtherHeaders()
        {
            return new AsciiString[]
            {
                new AsciiString("name1"), new AsciiString("value1"),
                new AsciiString("name2"), new AsciiString("value2"),
                new AsciiString("name3"), new AsciiString("value3")
            };
        }

        public ReadOnlyHttp2HeadersTest(ITestOutputHelper output) : base(output)
        {
        }
    }
}
