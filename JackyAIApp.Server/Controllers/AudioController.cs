using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        public AudioController(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
        }
        [HttpGet("normal")]
        public async Task<IActionResult> Normal([FromQuery] string text)
        {
            string url = $"https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl=en&q={Uri.EscapeDataString(text)}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var audioBytes = await response.Content.ReadAsByteArrayAsync();
                return File(audioBytes, "audio/mpeg");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch TTS audio: {ex.Message}");
                return StatusCode(500, "Internal Server Error");
            }
        }
        [HttpGet("slow")]
        public async Task<IActionResult> Slow([FromQuery] string text)
        {
            string url = $"https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl=en&ttsspeed=0.1&q={Uri.EscapeDataString(text)}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var audioBytes = await response.Content.ReadAsByteArrayAsync();
                return File(audioBytes, "audio/mpeg");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch TTS audio: {ex.Message}");
                return StatusCode(500, "Internal Server Error");
            }
        }
    }
}
