using iText.Kernel.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace JackyAIApp.Server.Services.Chatbot
{
    /// <param name="Bytes">What is stored and sent to the model.</param>
    /// <param name="MediaType">The media type of <paramref name="Bytes"/>.</param>
    /// <param name="PageCount">Pages of a PDF; null for an image.</param>
    public sealed record PreparedAttachmentFile(byte[] Bytes, string MediaType, int? PageCount);

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
        /// The model scales any larger image down to this before reading it, so a bigger one only
        /// costs storage and upload time: the long edge is capped here, and the area at about
        /// 1.15 megapixels.
        /// </summary>
        public const int MaxImageEdge = 1568;
        public const int MaxImagePixels = 1_150_000;

        /// <exception cref="UnreadableAttachmentException">Not an image, or not one that can be read.</exception>
        public static PreparedAttachmentFile PrepareImage(Stream content)
        {
            Image image;
            SixLabors.ImageSharp.Formats.IImageFormat format;
            try
            {
                image = Image.Load(content);
                format = image.Metadata.DecodedImageFormat
                    ?? throw new UnreadableAttachmentException("Unknown image format.");
            }
            catch (UnreadableAttachmentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new UnreadableAttachmentException("Could not read the file as an image.", ex);
            }

            using (image)
            {
                var scale = Math.Min(1.0, Math.Min(
                    (double)MaxImageEdge / Math.Max(image.Width, image.Height),
                    Math.Sqrt((double)MaxImagePixels / ((double)image.Width * image.Height))));
                if (scale < 1.0)
                {
                    image.Mutate(x => x.Resize(
                        Math.Max(1, (int)(image.Width * scale)),
                        Math.Max(1, (int)(image.Height * scale))));
                }
                // An animated GIF is read as its first frame.
                while (image.Frames.Count > 1) image.Frames.RemoveFrame(image.Frames.Count - 1);

                // A PNG stays lossless (screenshots, charts with text); everything else becomes a JPEG.
                using var output = new MemoryStream();
                if (format.Name == "PNG")
                {
                    image.Save(output, new PngEncoder());
                    return new PreparedAttachmentFile(output.ToArray(), "image/png", null);
                }
                image.Save(output, new JpegEncoder { Quality = 85 });
                return new PreparedAttachmentFile(output.ToArray(), "image/jpeg", null);
            }
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
