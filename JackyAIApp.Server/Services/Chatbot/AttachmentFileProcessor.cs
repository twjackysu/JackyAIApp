using iText.Kernel.Pdf;
using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <param name="Bytes">What is stored and sent to the model.</param>
    /// <param name="MediaType">The media type of <paramref name="Bytes"/>.</param>
    /// <param name="PageCount">Pages of a PDF; null for an image.</param>
    public sealed record PreparedAttachmentFile(byte[] Bytes, string MediaType, int? PageCount);

    /// <summary>An image larger than the browser makes one (<see cref="AttachmentFileProcessor.PrepareImage"/>).</summary>
    public sealed class ImageTooLargeException(int width, int height, long bytes)
        : Exception($"The image is {width}x{height}, {bytes} bytes.");

    /// <summary>A PDF with more pages than the model is sent in one request.</summary>
    public sealed class TooManyPagesException(int pages) : Exception($"The PDF has {pages} pages.")
    {
        public int Pages { get; } = pages;
    }

    /// <summary>
    /// Readies an image or a PDF to be kept and sent to the model as it is — the model reads both
    /// itself (a PDF as its text and as a picture of each page), so unlike Word or Excel nothing is
    /// read out of them here. Each is checked to be what its name says first.
    /// </summary>
    public static class AttachmentFileProcessor
    {
        /// <summary>Largest image or PDF upload accepted.</summary>
        public const long MaxBytes = 10 * 1024 * 1024;

        /// <summary>Pages per PDF: what the Messages API reads of a PDF in one request.</summary>
        public const int MaxPdfPages = 100;

        /// <summary>
        /// The size the browser downsizes an image to before uploading it (prepareChatImage.ts): the long
        /// edge is capped here, and the area at about 1.15 megapixels — the model's standard resolution,
        /// which keeps an image's cost at about 1,500 tokens.
        /// </summary>
        public const int MaxImageEdge = 1568;
        public const int MaxImagePixels = 1_150_000;

        /// <summary>Largest image kept: the model takes 10 MB of base64 per image, about 7.5 MB of file.</summary>
        public const long MaxImageBytes = 7 * 1024 * 1024;

        /// <summary>
        /// An image as the browser prepared it — downsized to <see cref="MaxImageEdge"/> and
        /// <see cref="MaxImagePixels"/>, and a PNG or a JPEG — checked from its header and kept as it is.
        /// Nothing is decoded on the server: an image decoder is where image libraries' security bugs are,
        /// and the model reads the image itself.
        /// </summary>
        /// <exception cref="UnreadableAttachmentException">Not a PNG or a JPEG.</exception>
        /// <exception cref="ImageTooLargeException">Larger than the browser makes one.</exception>
        public static PreparedAttachmentFile PrepareImage(Stream content)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var header = ImageHeader.Read(bytes) ?? throw new UnreadableAttachmentException("Not a PNG or a JPEG.");
            if (bytes.Length > MaxImageBytes || Math.Max(header.Width, header.Height) > MaxImageEdge || header.Pixels > MaxImagePixels)
                throw new ImageTooLargeException(header.Width, header.Height, bytes.Length);
            return new PreparedAttachmentFile(bytes, header.MediaType, null);
        }

        /// <exception cref="UnreadableAttachmentException">Not a PDF, damaged, or password protected.</exception>
        /// <exception cref="TooManyPagesException">More than <see cref="MaxPdfPages"/> pages.</exception>
        public static PreparedAttachmentFile PreparePdf(Stream content)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            var bytes = buffer.ToArray();

            int pages;
            try
            {
                using var reader = new PdfReader(new MemoryStream(bytes));
                using var pdf = new PdfDocument(reader);
                // Even one that opens without a password is refused: the model is not given passwords,
                // and the site's PDF unlocker can remove it first.
                if (reader.IsEncrypted())
                    throw new UnreadableAttachmentException("The PDF is encrypted.");
                pages = pdf.GetNumberOfPages();
            }
            catch (UnreadableAttachmentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new UnreadableAttachmentException("Could not read the file as a PDF.", ex);
            }

            if (pages > MaxPdfPages) throw new TooManyPagesException(pages);
            if (pages == 0) throw new UnreadableAttachmentException("The PDF has no pages.");
            return new PreparedAttachmentFile(bytes, "application/pdf", pages);
        }
    }
}
