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
    public class PDFController(ILogger<PDFController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, IUserService userService, IMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<PDFController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly IUserService _userService = userService;
        private readonly IMemoryCache _memoryCache = memoryCache;
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
                string outputPath = Path.Combine(Path.GetTempPath(), "unlocked.pdf");

                var readerProps = new ReaderProperties().SetPassword(System.Text.Encoding.UTF8.GetBytes(password));
                var reader = new PdfReader(memoryStream, readerProps);
                var writerProps = new WriterProperties();

                using var pdfDoc = new PdfDocument(reader, new PdfWriter(outputPath, writerProps));

                var fileBytes = System.IO.File.ReadAllBytes(outputPath);

                System.IO.File.Delete(outputPath);

                return File(fileBytes, "application/pdf", "unlocked.pdf");
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
