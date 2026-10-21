using System.Text;
using System.Text.Json;
using iText.Kernel.Pdf;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Chatbot;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// Images and PDFs, which the model reads itself: what is kept of them, what is refused, and
    /// how they are sent.
    /// </summary>
    public class AttachmentFileProcessorTests
    {
        [Fact]
        public void AnImageLargerThanTheModelReads_IsScaledDown_KeepingItsShape()
        {
            var result = AttachmentFileProcessor.PrepareImage(Png(4000, 2000));

            using var image = Image.Load(result.Bytes);
            Assert.True(Math.Max(image.Width, image.Height) <= AttachmentFileProcessor.MaxImageEdge);
            // At 2:1 the area is what binds, and it is used nearly in full.
            Assert.InRange(image.Width * image.Height,
                AttachmentFileProcessor.MaxImagePixels * 0.99, AttachmentFileProcessor.MaxImagePixels);
            Assert.Equal(2.0, (double)image.Width / image.Height, 1);
            Assert.Null(result.PageCount);
        }

        [Fact]
        public void ASmallImage_IsKeptAtItsSize()
        {
            var result = AttachmentFileProcessor.PrepareImage(Png(300, 200));

            using var image = Image.Load(result.Bytes);
            Assert.Equal((300, 200), (image.Width, image.Height));
        }

        [Fact]
        public void APng_StaysAPng_SoTextInAScreenshotStaysSharp()
        {
            Assert.Equal("image/png", AttachmentFileProcessor.PrepareImage(Png(10, 10)).MediaType);
        }

        [Fact]
        public void OtherImages_BecomeJpegs()
        {
            using var gif = new MemoryStream();
            using (var image = new Image<Rgba32>(10, 10)) image.SaveAsGif(gif);
            gif.Position = 0;

            var result = AttachmentFileProcessor.PrepareImage(gif);

            Assert.Equal("image/jpeg", result.MediaType);
            Assert.Equal("JPEG", Image.DetectFormat(result.Bytes).Name);
        }

        [Fact]
        public void AFileThatIsNotAnImage_IsUnreadable() =>
            Assert.Throws<UnreadableAttachmentException>(() =>
                AttachmentFileProcessor.PrepareImage(new MemoryStream(Encoding.UTF8.GetBytes("renamed.txt"))));

        [Fact]
        public void APdf_IsKeptAsItIs_WithItsPageCount()
        {
            var pdf = Pdf(3);

            var result = AttachmentFileProcessor.PreparePdf(new MemoryStream(pdf));

            Assert.Equal(pdf, result.Bytes);
            Assert.Equal("application/pdf", result.MediaType);
            Assert.Equal(3, result.PageCount);
        }

        [Fact]
        public void APdfOfMorePagesThanTheModelReads_IsRefused()
        {
            var ex = Assert.Throws<TooManyPagesException>(() =>
                AttachmentFileProcessor.PreparePdf(new MemoryStream(Pdf(AttachmentFileProcessor.MaxPdfPages + 1))));
            Assert.Equal(AttachmentFileProcessor.MaxPdfPages + 1, ex.Pages);
        }

        [Fact]
        public void APasswordProtectedPdf_IsUnreadable()
        {
            using var file = new MemoryStream();
            var properties = new WriterProperties().SetStandardEncryption(
                Encoding.ASCII.GetBytes("user"), Encoding.ASCII.GetBytes("owner"),
                EncryptionConstants.ALLOW_PRINTING, EncryptionConstants.ENCRYPTION_AES_128);
            using (var writer = new PdfWriter(file, properties))
            using (var doc = new PdfDocument(writer))
                doc.AddNewPage();

            Assert.Throws<UnreadableAttachmentException>(() =>
                AttachmentFileProcessor.PreparePdf(new MemoryStream(file.ToArray())));
        }

        [Fact]
        public void AFileThatIsNotAPdf_IsUnreadable() =>
            Assert.Throws<UnreadableAttachmentException>(() =>
                AttachmentFileProcessor.PreparePdf(new MemoryStream(Encoding.UTF8.GetBytes("%PDF- not really"))));

        [Theory]
        [InlineData(null, 2UL)]
        [InlineData(1, 3UL)]
        [InlineData(10, 3UL)]
        [InlineData(11, 4UL)]
        [InlineData(100, 12UL)]
        public void APdfCostsMoreTheMorePagesItHas(int? pages, ulong cost) =>
            Assert.Equal(cost, UsageCosts.ForChatAttachment(pages));

        [Fact]
        public void ImagesAndPdfs_AreSentAsTheFilesThemselves()
        {
            var message = new SQL.ChatMessage { ConversationId = "c", Role = "user", Content = "比較一下" };
            message.Attachments.Add(new SQL.ChatAttachment
            {
                Id = 1, UserId = "u", FileName = "走勢.png", Kind = AttachmentKinds.Image, Text = "",
                BlobName = "b1", MediaType = "image/png",
            });
            message.Attachments.Add(new SQL.ChatAttachment
            {
                Id = 2, UserId = "u", FileName = "年報.pdf", Kind = AttachmentKinds.Pdf, Text = "",
                BlobName = "b2", MediaType = "application/pdf", PageCount = 3,
            });
            var files = new Dictionary<long, byte[]> { [1] = [1, 2, 3], [2] = [4, 5, 6] };

            var blocks = Wire(message, files);

            Assert.Equal(4, blocks.Count);
            // The image is named first: an image block has no title of its own.
            Assert.Equal("[附件圖片：走勢.png]", blocks[0].GetProperty("text").GetString());
            Assert.Equal("image", blocks[1].GetProperty("type").GetString());
            Assert.Equal("base64", blocks[1].GetProperty("source").GetProperty("type").GetString());
            Assert.Equal("image/png", blocks[1].GetProperty("source").GetProperty("media_type").GetString());
            Assert.Equal(Convert.ToBase64String([1, 2, 3]), blocks[1].GetProperty("source").GetProperty("data").GetString());
            Assert.Equal("document", blocks[2].GetProperty("type").GetString());
            Assert.Equal("年報.pdf", blocks[2].GetProperty("title").GetString());
            Assert.Equal("application/pdf", blocks[2].GetProperty("source").GetProperty("media_type").GetString());
            Assert.Equal(Convert.ToBase64String([4, 5, 6]), blocks[2].GetProperty("source").GetProperty("data").GetString());
            Assert.Equal("比較一下", blocks[3].GetProperty("text").GetString());
        }

        [Fact]
        public void AFileThatIsGoneFromTheStore_IsReplacedByANoteSayingSo()
        {
            var message = new SQL.ChatMessage { ConversationId = "c", Role = "user", Content = "這張呢" };
            message.Attachments.Add(new SQL.ChatAttachment
            {
                Id = 1, UserId = "u", FileName = "舊圖.jpg", Kind = AttachmentKinds.Image, Text = "",
                BlobName = "gone", MediaType = "image/jpeg",
            });

            var blocks = Wire(message, new Dictionary<long, byte[]>());

            Assert.Equal(2, blocks.Count);
            Assert.Equal("[附件「舊圖.jpg」已無法讀取。]", blocks[0].GetProperty("text").GetString());
        }

        private static List<JsonElement> Wire(SQL.ChatMessage message, Dictionary<long, byte[]> files) =>
            JsonSerializer.SerializeToElement(AnthropicChatService.ToMessageParam(message, files))
                .GetProperty("content").EnumerateArray().ToList();

        private static MemoryStream Png(int width, int height)
        {
            var stream = new MemoryStream();
            using (var image = new Image<Rgba32>(width, height)) image.SaveAsPng(stream);
            stream.Position = 0;
            return stream;
        }

        private static byte[] Pdf(int pages)
        {
            using var file = new MemoryStream();
            using (var writer = new PdfWriter(file))
            using (var doc = new PdfDocument(writer))
                for (var i = 0; i < pages; i++) doc.AddNewPage();
            return file.ToArray();
        }
    }
}
