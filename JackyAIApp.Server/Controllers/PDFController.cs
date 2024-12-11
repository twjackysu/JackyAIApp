using DotnetSdkUtilities.Services;
using iText.Kernel.Exceptions;
using iText.Kernel.Pdf;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PDFController(ILogger<PDFController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, IUserService userService, IExtendedMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<PDFController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly IUserService _userService = userService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;
        [HttpPost("unlock")]
        public async Task<IActionResult> UnlockPdf([FromForm] IFormFile pdfFile, [FromForm] string password)
        {
            if (pdfFile == null || pdfFile.Length == 0)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "PDF file is required.");
            }

            using var memoryStream = new MemoryStream();
            await pdfFile.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            try
            {
                var readerProps = new ReaderProperties().SetPassword(System.Text.Encoding.UTF8.GetBytes(password));
                using var reader = new PdfReader(memoryStream, readerProps);
                using var outputMemoryStream = new MemoryStream();
                var writerProps = new WriterProperties().SetFullCompressionMode(true);
                using var writer = new PdfWriter(outputMemoryStream, writerProps);
                using var pdfDoc = new PdfDocument(reader, writer);

                pdfDoc.Close();

                var fileBytes = outputMemoryStream.ToArray();
                Response.Headers.ContentDisposition = "attachment; filename=unlocked_compressed.pdf";
                return File(fileBytes, "application/pdf", "unlocked_compressed.pdf");
            }
            catch (BadPasswordException)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Invalid password for the PDF file.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unlocking PDF file");
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "An error occurred while unlocking the PDF file.");
            }
        }
    }
}
