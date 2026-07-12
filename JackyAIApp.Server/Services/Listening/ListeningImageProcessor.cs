using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace JackyAIApp.Server.Services.Listening
{
    // Windows-only APIs (System.Drawing.Common) — fine here since the app only ever runs on the
    // Windows App Service; CI (ubuntu-latest) builds this project but never invokes the method.
    [SupportedOSPlatform("windows")]
    public class ListeningImageProcessor : IListeningImageProcessor
    {
        public byte[] ResizeAndCompress(byte[] imageBytes, int maxDimension = 1024, int targetMaxBytes = 300 * 1024)
        {
            using var ms = new MemoryStream(imageBytes);
            using var original = Image.FromStream(ms);

            var scale = Math.Min(1.0, (double)maxDimension / Math.Max(original.Width, original.Height));
            var targetWidth = (int)Math.Round(original.Width * scale);
            var targetHeight = (int)Math.Round(original.Height * scale);

            using var resized = new Bitmap(targetWidth, targetHeight);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(original, 0, 0, targetWidth, targetHeight);
            }

            var jpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

            // Step quality down until under the target size or we hit a quality floor.
            for (var quality = 85; quality >= 50; quality -= 10)
            {
                using var outMs = new MemoryStream();
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                resized.Save(outMs, jpegCodec, encoderParams);
                if (outMs.Length <= targetMaxBytes || quality == 50)
                {
                    return outMs.ToArray();
                }
            }

            // Unreachable, but keeps the compiler happy.
            using var fallback = new MemoryStream();
            using var fallbackParams = new EncoderParameters(1);
            fallbackParams.Param[0] = new EncoderParameter(Encoder.Quality, 50);
            resized.Save(fallback, jpegCodec, fallbackParams);
            return fallback.ToArray();
        }
    }
}
