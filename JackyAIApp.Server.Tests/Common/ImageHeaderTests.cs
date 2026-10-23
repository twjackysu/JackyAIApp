using JackyAIApp.Server.Common;
using JackyAIApp.Server.Tests.TestHelpers;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>A PNG's or JPEG's format and size, read from its header, and nothing taken for one that is not.</summary>
    public class ImageHeaderTests
    {
        [Fact]
        public void APng_IsReadFromItsIhdr()
        {
            Assert.Equal(new ImageHeader(ImageHeader.Png, 1568, 882), ImageHeader.Read(TestImages.Png(1568, 882)));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(5000, false)]   // an EXIF segment ahead of the frame, as phones write it
        [InlineData(0, true)]       // progressive
        public void AJpeg_IsReadFromItsStartOfFrame(int exifBytes, bool progressive)
        {
            Assert.Equal(new ImageHeader(ImageHeader.Jpeg, 1200, 900),
                ImageHeader.Read(TestImages.Jpeg(1200, 900, exifBytes, progressive)));
        }

        [Fact]
        public void PixelsCountsTheWholeArea_WithoutOverflowing()
        {
            Assert.Equal(1_048_576L, ImageHeader.Read(TestImages.Png(1024, 1024))!.Pixels);
            Assert.Equal(4_000_000_000L, new ImageHeader(ImageHeader.Png, 100_000, 40_000).Pixels);
        }

        public static TheoryData<string, byte[]> NotAPngOrJpeg => new()
        {
            { "empty", [] },
            { "text", "hello, world"u8.ToArray() },
            { "GIF", "GIF89a\x01\x00\x01\x00\x00\x00\x00"u8.ToArray() },
            { "WebP", [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8, 0, 0, 0, 0] },
            { "BMP", [.. "BM"u8, .. new byte[30]] },
            { "TIFF", [0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0, .. new byte[20]] },
            { "PNG cut short", TestImages.Png(10, 10)[..20] },
            { "PNG of no width", TestImages.Png(0, 10) },
            { "PNG whose first chunk is not IHDR", [.. TestImages.Png(10, 10)[..12], .. "tEXt"u8, .. TestImages.Png(10, 10)[16..]] },
            { "JPEG cut before its frame", TestImages.Jpeg(10, 10)[..12] },
            { "JPEG of no height", TestImages.Jpeg(10, 0) },
            { "JPEG scan before any frame", [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 0x3F, 0] },
            { "JPEG segment running past the end", [0xFF, 0xD8, 0xFF, 0xE1, 0x7F, 0xFF, 1, 2, 3] },
            { "JPEG with junk where a marker should be", [0xFF, 0xD8, 0x12, 0x34, 0x56, 0x78] },
        };

        [Theory]
        [MemberData(nameof(NotAPngOrJpeg))]
        public void AnythingElse_IsNoHeader(string _, byte[] bytes)
        {
            Assert.Null(ImageHeader.Read(bytes));
        }
    }
}
