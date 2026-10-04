using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// Public client-side configuration (TASK-029). Anonymous by design: the Application Insights
    /// browser SDK needs the connection string in the page anyway, so it is not a secret.
    /// </summary>
    [ApiController]
    [Route("api/config")]
    public class ConfigController(IConfiguration configuration) : ControllerBase
    {
        private readonly IConfiguration _configuration = configuration;

        /// <summary>
        /// Connection string for the browser-side page-view telemetry, or null when unset
        /// (the frontend then does not initialise the SDK). Same sources as Program.cs.
        /// </summary>
        [HttpGet("telemetry")]
        public IActionResult GetTelemetry()
        {
            var connectionString = _configuration["ApplicationInsights:ConnectionString"]
                ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
            return Ok(new { connectionString = string.IsNullOrWhiteSpace(connectionString) ? null : connectionString });
        }
    }
}
