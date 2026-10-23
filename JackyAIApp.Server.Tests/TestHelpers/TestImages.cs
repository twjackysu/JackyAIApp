using System.Buffers.Binary;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// PNG and JPEG files with a real header and no picture: the server only ever reads an image's header
    /// (<see cref="Server.Common.ImageHeader"/>), so that is all a test needs.
    /// </summary>
    public static class TestImages
    {
        public static byte[] Png(int width, int height)
        {
            byte[] bytes =
            [
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,   // signature
                0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
                0, 0, 0, 0, 0, 0, 0, 0,                          // width, height
                8, 6, 0, 0, 0,                                   // 8-bit RGBA
                0, 0, 0, 0,                                      // CRC (not checked)
                0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82,
            ];
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
            return bytes;
        }

        /// <param name="exifBytes">The size of an APP1 (EXIF) segment placed before the frame, as a phone writes one.</param>
        /// <param name="progressive">A progressive JPEG (SOF2) rather than a baseline one (SOF0).</param>
        public static byte[] Jpeg(int width, int height, int exifBytes = 0, bool progressive = false)
        {
            var bytes = new List<byte> { 0xFF, 0xD8 };
            bytes.AddRange([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
            if (exifBytes > 0)
            {
                bytes.AddRange([0xFF, 0xE1, (byte)((exifBytes + 2) >> 8), (byte)(exifBytes + 2)]);
                bytes.AddRange(new byte[exifBytes]);
            }
            bytes.AddRange([0xFF, progressive ? (byte)0xC2 : (byte)0xC0, 0x00, 0x11, 8,
                (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
                3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);
            bytes.AddRange([0xFF, 0xD9]);
            return [.. bytes];
        }
    }
}
