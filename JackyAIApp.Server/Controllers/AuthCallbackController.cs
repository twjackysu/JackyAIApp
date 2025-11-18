using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/connectors/callback")]
    public class AuthCallbackController : ControllerBase
    {
        private readonly IConnectorService _connectorService;
        private readonly ILogger<AuthCallbackController> _logger;

        public AuthCallbackController(IConnectorService connectorService, ILogger<AuthCallbackController> logger)
        {
            _connectorService = connectorService;
            _logger = logger;
        }

        /// <summary>
        /// Handles OAuth callback from providers
        /// </summary>
        /// <param name="provider">The provider name (Microsoft, Atlassian, Google)</param>
        /// <param name="code">Authorization code from OAuth provider</param>
        /// <param name="state">State parameter to prevent CSRF attacks</param>
        /// <param name="error">Error parameter if OAuth failed</param>
        /// <param name="error_description">Error description if OAuth failed</param>
        /// <returns>Redirects back to frontend with success/error status</returns>
        [HttpGet("{provider}")]
        public async Task<IActionResult> HandleCallback(
            string provider,
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            [FromQuery] string? error_description)
        {
            var frontendBaseUrl = GetFrontendBaseUrl();

            try
            {
                // Check for OAuth errors
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogWarning("OAuth error for provider {Provider}: {Error} - {Description}",
                        provider, error, error_description);

                    return Redirect($"{frontendBaseUrl}/connectors?error={Uri.EscapeDataString(error)}&provider={provider}");
                }

                // Validate required parameters
                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                {
                    _logger.LogWarning("Missing required parameters in OAuth callback for provider {Provider}", provider);
                    return Redirect($"{frontendBaseUrl}/connectors?error=missing_parameters&provider={provider}");
                }

                // Normalize provider name
                provider = NormalizeProviderName(provider);
                if (provider == null)
                {
                    _logger.LogWarning("Invalid provider name in callback: {Provider}", provider);
                    return Redirect($"{frontendBaseUrl}/connectors?error=invalid_provider");
                }

                // Handle the callback
                var success = await _connectorService.HandleCallbackAsync(provider, code, state);

                if (success)
                {
                    _logger.LogInformation("Successfully handled OAuth callback for provider {Provider}", provider);
                    return Redirect($"{frontendBaseUrl}/connectors?success=true&provider={provider}");
                }
                else
                {
                    _logger.LogError("Failed to handle OAuth callback for provider {Provider}", provider);
                    return Redirect($"{frontendBaseUrl}/connectors?error=callback_failed&provider={provider}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in OAuth callback for provider {Provider}", provider);
                return Redirect($"{frontendBaseUrl}/connectors?error=internal_error&provider={provider}");
            }
        }

        private string GetFrontendBaseUrl()
        {
            // In development, use localhost:5173 (Vite default)
            // In production, use the actual domain
            if (HttpContext.Request.Host.Host.Contains("localhost"))
            {
                return "https://localhost:5173";
            }

            // For production, you might want to get this from configuration
            return $"https://{HttpContext.Request.Host}";
        }

        private static string? NormalizeProviderName(string provider)
        {
            return provider?.ToLowerInvariant() switch
            {
                "microsoft" => "Microsoft",
                "atlassian" => "Atlassian",
                "google" => "Google",
                _ => null
            };
        }
    }
}