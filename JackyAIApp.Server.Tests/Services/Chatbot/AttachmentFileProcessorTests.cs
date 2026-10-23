using System.Text;
using System.Text.Json;
using iText.Kernel.Pdf;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Chatbot;
using JackyAIApp.Server.Tests.TestHelpers;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// Images and PDFs, which the model reads itself: what is kept of them, what is refused, and
    /// how they are sent.
    /// </summary>
    public class AttachmentFileProcessorTests
    {
        // The browser downsizes an image before uploading it (prepareChatImage.ts); the server only reads
        // its header, keeps it as it is, and refuses one the browser would not have sent.

        private static MemoryStream Stream(byte[] bytes) => new(bytes);

        [Theory]
        [InlineData(1568, 733)]    // the long edge at its cap
        [InlineData(1072, 1072)]   // the area just under 1.15 megapixels
        [InlineData(300, 200)]
        public void AnImageTheBrowserPrepared_IsKeptAsItIs(int width, int height)
        {
            var png = TestImages.Png(width, height);

            var result = AttachmentFileProcessor.PrepareImage(Stream(png));

            Assert.Equal(png, result.Bytes);
            Assert.Equal("image/png", result.MediaType);
            Assert.Null(result.PageCount);
        }

        [Fact]
        public void AJpeg_IsKeptAsAJpeg()
        {
            var jpeg = TestImages.Jpeg(1200, 900, exifBytes: 2000);

            var result = AttachmentFileProcessor.PrepareImage(Stream(jpeg));

            Assert.Equal(jpeg, result.Bytes);
            Assert.Equal("image/jpeg", result.MediaType);
        }

        public static TheoryData<string, byte[]> LargerThanTheBrowserMakesOne => new()
        {
            { "long edge", TestImages.Png(1569, 100) },
            { "area", TestImages.Jpeg(1200, 1000) },
            { "bytes", [.. TestImages.Png(100, 100), .. new byte[AttachmentFileProcessor.MaxImageBytes]] },
        };

        [Theory]
        [MemberData(nameof(LargerThanTheBrowserMakesOne))]
        public void AnImageLargerThanTheBrowserMakesOne_IsRefused(string _, byte[] image)
        {
            Assert.Throws<ImageTooLargeException>(() => AttachmentFileProcessor.PrepareImage(Stream(image)));
        }

        public static TheoryData<string, byte[]> NotAPngOrJpeg => new()
        {
            { "GIF", [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0] },
            { "WebP", [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8, 0, 0, 0, 0] },
            { "TIFF", [0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0, .. new byte[20]] },
            { "BMP", [.. "BM"u8, .. new byte[30]] },
            { "text renamed .png", "renamed.txt"u8.ToArray() },
        };

        [Theory]
        [MemberData(nameof(NotAPngOrJpeg))]
        public void AnythingButAPngOrJpeg_IsUnreadable(string _, byte[] content)
        {
            // Whatever the file is called: the content decides. The browser turns a GIF or a WebP into one
            // of the two before it is uploaded.
            Assert.Throws<UnreadableAttachmentException>(() => AttachmentFileProcessor.PrepareImage(Stream(content)));
        }

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
        public void ImagesAndPdfsInTheFilesApi_AreReferredToByFileId_NotSentAgain()
        {
            var message = new SQL.ChatMessage { ConversationId = "c", Role = "user", Content = "比較一下" };
            message.Attachments.Add(new SQL.ChatAttachment
            {
                Id = 1, UserId = "u", FileName = "走勢.png", Kind = AttachmentKinds.Image, Text = "",
                BlobName = "b1", MediaType = "image/png", AnthropicFileId = "file_img",
            });
            message.Attachments.Add(new SQL.ChatAttachment
            {
                Id = 2, UserId = "u", FileName = "年報.pdf", Kind = AttachmentKinds.Pdf, Text = "",
                BlobName = "b2", MediaType = "application/pdf", PageCount = 3, AnthropicFileId = "file_pdf",
            });
            // Bytes at hand too: the file_id still wins, so the request stays small.
            var files = new Dictionary<long, byte[]> { [1] = [1, 2, 3] };

            var blocks = Wire(message, files);

            Assert.Equal(4, blocks.Count);
            Assert.Equal("[附件圖片：走勢.png]", blocks[0].GetProperty("text").GetString());
            Assert.Equal("file", blocks[1].GetProperty("source").GetProperty("type").GetString());
            Assert.Equal("file_img", blocks[1].GetProperty("source").GetProperty("file_id").GetString());
            Assert.False(blocks[1].GetProperty("source").TryGetProperty("data", out _));
            Assert.Equal("document", blocks[2].GetProperty("type").GetString());
            Assert.Equal("年報.pdf", blocks[2].GetProperty("title").GetString());
            Assert.Equal("file", blocks[2].GetProperty("source").GetProperty("type").GetString());
            Assert.Equal("file_pdf", blocks[2].GetProperty("source").GetProperty("file_id").GetString());
        }

        [Theory]
        [InlineData(404, "File `file_011` not found.", true)]
        [InlineData(400, "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"File file_011 not found\"}}", true)]
        [InlineData(404, "model: claude-x not found", false)]
        [InlineData(400, "prompt is too long", false)]
        [InlineData(500, "file not found", false)]
        public void AFileIdTheFilesApiNoLongerHas_IsToldApartFromOtherErrors(int status, string message, bool missing) =>
            Assert.Equal(missing, ChatErrorCodes.IsMissingFile(status, message));

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
