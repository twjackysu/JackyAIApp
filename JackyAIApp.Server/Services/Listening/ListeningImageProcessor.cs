using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JackyAIApp.Server.Services.Listening
{
    // ImageSharp rather than System.Drawing.Common, which only works on Windows (.NET 6+ throws
    // PlatformNotSupportedException elsewhere) and so would break this on the Linux App Service.
    // Fully managed, unlike SkiaSharp, whose native binaries for every platform added ~470MB to
    // the publish output — half the Free plan's 1GB of storage.
    public class ListeningImageProcessor : IListeningImageProcessor
    {
        public byte[] ResizeAndCompress(byte[] imageBytes, int targetWidth = 800, int targetHeight = 600, int targetMaxBytes = 300 * 1024)
        {
            Image<Rgba32> image;
            try
            {
                image = Image.Load<Rgba32>(imageBytes);
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
            {
                throw new ArgumentException("Not a decodable image.", nameof(imageBytes), ex);
            }

            using (image)
            {
                // Center-crop to the target aspect ratio (4:3 by default) so every question image
                // is uniform regardless of source aspect ratio (OpenAI's square 1024x1024 vs
                // whatever a manually-imported batch happens to produce), then scale to the exact
                // target size.
                var targetAspect = targetWidth / (double)targetHeight;
                var originalAspect = image.Width / (double)image.Height;

                int srcX, srcY, srcWidth, srcHeight;
                if (originalAspect > targetAspect)
                {
                    srcHeight = image.Height;
                    srcWidth = (int)Math.Round(image.Height * targetAspect);
                    srcX = (image.Width - srcWidth) / 2;
                    srcY = 0;
                }
                else
                {
                    srcWidth = image.Width;
                    srcHeight = (int)Math.Round(image.Width / targetAspect);
                    srcX = 0;
                    srcY = (image.Height - srcHeight) / 2;
                }

                image.Mutate(x => x
                    .Crop(new Rectangle(srcX, srcY, srcWidth, srcHeight))
                    .Resize(targetWidth, targetHeight, KnownResamplers.Bicubic)
                    // JPEG has no alpha channel; put transparent areas on white rather than
                    // letting them come out as whatever colour their invisible pixels hold.
                    .BackgroundColor(Color.White));

                // Step quality down until under the target size or we hit a quality floor.
                for (var quality = 85; ; quality -= 10)
                {
                    using var output = new MemoryStream();
                    image.SaveAsJpeg(output, new JpegEncoder { Quality = quality });
                    if (output.Length <= targetMaxBytes || quality <= 50)
                        return output.ToArray();
                }
            }
        }
    }
}
