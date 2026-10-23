using System.Buffers.Binary;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// The format and size of a PNG or JPEG, read from its header alone — nothing is decoded. The app
    /// never decodes an image it is given (a decoder is where image libraries' security bugs live): the
    /// browser downsizes a chat attachment before it is uploaded and the model reads it; this is how the
    /// server checks that what it keeps is the kind of image it says it is, and how big.
    /// </summary>
    public sealed record ImageHeader(string MediaType, int Width, int Height)
    {
        public const string Png = "image/png";
        public const string Jpeg = "image/jpeg";

        private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        /// <summary>The header of a PNG or JPEG, or null for any other format, or one too damaged to tell.</summary>
        public static ImageHeader? Read(ReadOnlySpan<byte> bytes) => ReadPng(bytes) ?? ReadJpeg(bytes);

        public long Pixels => (long)Width * Height;

        // The signature, then the IHDR chunk, which must come first: its length (4 bytes), "IHDR", and the
        // width and height (4 bytes each, big-endian).
        private static ImageHeader? ReadPng(ReadOnlySpan<byte> b)
        {
            if (b.Length < 24 || !b[..8].SequenceEqual(PngSignature) || !b.Slice(12, 4).SequenceEqual("IHDR"u8))
                return null;
            var width = BinaryPrimitives.ReadInt32BigEndian(b.Slice(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(b.Slice(20, 4));
            return width > 0 && height > 0 ? new ImageHeader(Png, width, height) : null;
        }

        // Start of image (FF D8), then segments — FF, a marker, and a big-endian length that counts itself —
        // until a start of frame (C0–CF but C4, C8, CC), whose payload is the precision (1 byte), then the
        // height and width (2 bytes each). Scan data before any frame, or the end of the image, is no JPEG.
        private static ImageHeader? ReadJpeg(ReadOnlySpan<byte> b)
        {
            if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8)
                return null;
            var i = 2;
            while (i + 4 <= b.Length)
            {
                if (b[i] != 0xFF)
                    return null;
                var marker = b[i + 1];
                if (marker == 0xFF)
                {
                    i++;   // a fill byte before the marker
                    continue;
                }
                if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                {
                    i += 2;   // markers without a segment
                    continue;
                }
                if (marker is 0xD8 or 0xD9 or 0xDA)
                    return null;

                var length = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(i + 2, 2));
                if (length < 2 || i + 2 + length > b.Length)
                    return null;
                if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                {
                    if (length < 8)
                        return null;
                    var height = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(i + 5, 2));
                    var width = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(i + 7, 2));
                    return width > 0 && height > 0 ? new ImageHeader(Jpeg, width, height) : null;
                }
                i += 2 + length;
            }
            return null;
        }
    }
}
