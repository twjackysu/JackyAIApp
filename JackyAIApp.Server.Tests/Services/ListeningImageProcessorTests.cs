using JackyAIApp.Server.Services.Listening;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// Runs in PR CI on ubuntu-latest, so it also proves the image pipeline works off Windows —
    /// the reason it moved from System.Drawing.Common to ImageSharp (the app now runs on a Linux
    /// App Service).
    /// </summary>
    public class ListeningImageProcessorTests
    {
        private readonly ListeningImageProcessor _processor = new();

        private static byte[] Png(int width, int height, Func<int, int, Rgba32> pixelAt)
        {
            using var image = new Image<Rgba32>(width, height);
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    image[x, y] = pixelAt(x, y);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        private static Image<Rgba32> Decode(byte[] bytes) => Image.Load<Rgba32>(bytes);

        [Fact]
        public void ResizeAndCompress_SquareSource_BecomesAJpegOfTheTargetSize()
        {
            var png = Png(1024, 1024, (_, _) => new Rgba32(70, 130, 180));

            var result = _processor.ResizeAndCompress(png);

            Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, result[..3]); // JPEG signature
            using var decoded = Decode(result);
            Assert.Equal(800, decoded.Width);
            Assert.Equal(600, decoded.Height);
        }

        [Fact]
        public void ResizeAndCompress_CropsTheCenter()
        {
            // A square with red bands at the very top and bottom: cropping 1024x1024 to 4:3 cuts
            // 128px off each of those edges, so none of the red should survive.
            var png = Png(1024, 1024, (_, y) => y < 100 || y >= 924 ? new Rgba32(255, 0, 0) : new Rgba32(0, 160, 0));

            using var decoded = Decode(_processor.ResizeAndCompress(png));

            foreach (var y in new[] { 2, 300, 597 })
            {
                var pixel = decoded[400, y];
                Assert.True(pixel.G > 100 && pixel.R < 80, $"row {y} is {pixel}, expected green");
            }
        }

        [Fact]
        public void ResizeAndCompress_TransparentAreas_BecomeWhite()
        {
            var png = Png(800, 600, (_, _) => new Rgba32(0, 0, 0, 0));

            using var decoded = Decode(_processor.ResizeAndCompress(png));

            var pixel = decoded[400, 300];
            Assert.True(pixel.R > 240 && pixel.G > 240 && pixel.B > 240, $"got {pixel}, expected white");
        }

        [Fact]
        public void ResizeAndCompress_NoisySource_LowersQualityToFitTheByteBudget()
        {
            var random = new Random(42);
            var png = Png(1024, 768, (_, _) => new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));

            var generous = _processor.ResizeAndCompress(png, targetMaxBytes: int.MaxValue);
            var tight = _processor.ResizeAndCompress(png, targetMaxBytes: generous.Length / 2);

            Assert.True(tight.Length < generous.Length, $"{tight.Length} vs {generous.Length}");
        }

        [Fact]
        public void ResizeAndCompress_NotAnImage_Throws() =>
            Assert.Throws<ArgumentException>(() => _processor.ResizeAndCompress([1, 2, 3, 4]));
    }
}
