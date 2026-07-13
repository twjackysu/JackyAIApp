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
        public byte[] ResizeAndCompress(byte[] imageBytes, int targetWidth = 800, int targetHeight = 600, int targetMaxBytes = 300 * 1024)
        {
            using var ms = new MemoryStream(imageBytes);
            using var original = Image.FromStream(ms);

            // Center-crop to the target aspect ratio (4:3 by default) so every question image is
            // uniform regardless of source aspect ratio (OpenAI's square 1024x1024 vs whatever a
            // manually-imported batch happens to produce), then scale to the exact target size.
            var targetAspect = targetWidth / (double)targetHeight;
            var originalAspect = original.Width / (double)original.Height;

            int srcX, srcY, srcWidth, srcHeight;
            if (originalAspect > targetAspect)
            {
                srcHeight = original.Height;
                srcWidth = (int)Math.Round(original.Height * targetAspect);
                srcX = (original.Width - srcWidth) / 2;
                srcY = 0;
            }
            else
            {
                srcWidth = original.Width;
                srcHeight = (int)Math.Round(original.Width / targetAspect);
                srcX = 0;
                srcY = (original.Height - srcHeight) / 2;
            }

            using var resized = new Bitmap(targetWidth, targetHeight);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(original,
                    new Rectangle(0, 0, targetWidth, targetHeight),
                    new Rectangle(srcX, srcY, srcWidth, srcHeight),
                    GraphicsUnit.Pixel);
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
