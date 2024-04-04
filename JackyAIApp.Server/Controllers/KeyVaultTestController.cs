using Azure.Security.KeyVault.Secrets;
using JackyAIApp.Server.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class KeyVaultTestController : ControllerBase
    {
        private readonly ILogger<KeyVaultTestController> _logger;
        private readonly IOptionsMonitor<Settings> _settings;

        public KeyVaultTestController(ILogger<KeyVaultTestController> logger, IOptionsMonitor<Settings> settings)
        {
            _logger = logger;
            _settings = settings;
        }

        [HttpGet(Name = "GetSecret")]
        public string Get()
        {
            return _settings.CurrentValue.Test ?? "";
        }
    }
}
