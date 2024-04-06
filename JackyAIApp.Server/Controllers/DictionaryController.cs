
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]/{word}")]
    public class DictionaryController : ControllerBase
    {
        private readonly ILogger<DictionaryController> _logger;
        private readonly IOptionsMonitor<Settings> _settings;
        private readonly IMyResponseFactory _responseFactory;

        public DictionaryController(ILogger<DictionaryController> logger, IOptionsMonitor<Settings> settings)
        {
            _logger = logger;
            _settings = settings;
        }

        [HttpGet(Name = "Search word")]
        public string Get(string word)
        {
            return word;
        }
    }
}
