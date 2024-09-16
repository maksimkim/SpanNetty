
using DotNetty.Common.Tests.Internal.Logging;

namespace DotNetty.Codecs.Http2.Tests
{
    using System;
    using Xunit;

    /**
     * Tests for {@link Http2Settings}.
     */
    public class Http2SettingsTest
    {
        private Http2Settings settings;

        public Http2SettingsTest()
        {
            this.settings = new Http2Settings();
        }

        [Fact]
        [BeforeTest]
        public void StandardSettingsShouldBeNotSet()
        {
            Assert.Empty(settings);
            Assert.Null(settings.HeaderTableSize());
            Assert.Null(settings.InitialWindowSize());
            Assert.Null(settings.MaxConcurrentStreams());
            Assert.Null(settings.PushEnabled());
            Assert.Null(settings.MaxFrameSize());
            Assert.Null(settings.MaxHeaderListSize());
        }

        [Fact]
        [BeforeTest]
        public void StandardSettingsShouldBeSet()
        {
            settings.InitialWindowSize(1);
            settings.MaxConcurrentStreams(2);
            settings.PushEnabled(true);
            settings.HeaderTableSize(3);
            settings.MaxFrameSize(Http2CodecUtil.MaxFrameSizeUpperBound);
            settings.MaxHeaderListSize(4);
            Assert.Equal(1, (int)settings.InitialWindowSize());
            Assert.Equal(2L, (long)settings.MaxConcurrentStreams());
            Assert.True(settings.PushEnabled());
            Assert.Equal(3L, (long)settings.HeaderTableSize());
            Assert.Equal(Http2CodecUtil.MaxFrameSizeUpperBound, (int)settings.MaxFrameSize());
            Assert.Equal(4L, (long)settings.MaxHeaderListSize());
        }

        [Fact]
        [BeforeTest]
        public void NonStandardSettingsShouldBeSet()
        {
            char key = (char)0;
            settings.Put(key, 123L);
            Assert.True(settings.TryGetValue(key, out var result));
            Assert.Equal(123L, result);
        }

        [Fact]
        [BeforeTest]
        public void SettingsShouldSupportUnsignedShort()
        {
            char key = (char)(short.MaxValue + 1);
            settings.Put(key, 123L);
            Assert.True(settings.TryGetValue(key, out var result));
            Assert.Equal(123L, result);
        }

        [Fact]
        [BeforeTest]
        public void HeaderListSizeUnsignedInt()
        {
            settings.MaxHeaderListSize(Http2CodecUtil.MaxUnsignedInt);
            Assert.Equal(Http2CodecUtil.MaxUnsignedInt, (long)settings.MaxHeaderListSize());
        }

        [Fact]
        [BeforeTest]
        public void HeaderListSizeBoundCheck()
        {
            Assert.Throws<ArgumentException>(() => settings.MaxHeaderListSize(long.MaxValue));
        }

        [Fact]
        [BeforeTest]
        public void HeaderTableSizeUnsignedInt()
        {
            settings.Put(Http2CodecUtil.SettingsHeaderTableSize, Http2CodecUtil.MaxUnsignedInt);
            Assert.True(settings.TryGetValue(Http2CodecUtil.SettingsHeaderTableSize, out var result));
            Assert.Equal(Http2CodecUtil.MaxUnsignedInt, result);
        }

        [Fact]
        [BeforeTest]
        public void HeaderTableSizeBoundCheck()
        {
            Assert.Throws<ArgumentException>(() => settings.Put(Http2CodecUtil.SettingsHeaderTableSize, long.MaxValue));
        }

        [Fact]
        [BeforeTest]
        public void HeaderTableSizeBoundCheck2()
        {
            Assert.Throws<ArgumentException>(() => settings.Put(Http2CodecUtil.SettingsHeaderTableSize, -1));
        }
    }
}
