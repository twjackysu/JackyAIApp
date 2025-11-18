using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ConnectorsController : ControllerBase
    {
        private readonly IConnectorService _connectorService;
        private readonly ILogger<ConnectorsController> _logger;

        public ConnectorsController(IConnectorService connectorService, ILogger<ConnectorsController> logger)
        {
            _connectorService = connectorService;
            _logger = logger;
        }

        /// <summary>
        /// Gets the connection status of all providers for the current user
        /// </summary>
        /// <returns>Array of connector statuses</returns>
        [HttpGet("status")]
        public async Task<ActionResult<ConnectorStatusDto[]>> GetStatus()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in claims");
                }

                var statuses = await _connectorService.GetUserConnectorStatusAsync(userId);
                return Ok(statuses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting connector status");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Initiates OAuth flow for a specific provider
        /// </summary>
        /// <param name="provider">The provider name (Microsoft, Atlassian, Google)</param>
        /// <returns>OAuth authorization URL</returns>
        [HttpPost("{provider}/connect")]
        public async Task<ActionResult<ConnectResponseDto>> Connect(string provider)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in claims");
                }

                if (string.IsNullOrEmpty(provider))
                {
                    return BadRequest("Provider name is required");
                }

                // Normalize provider name
                provider = NormalizeProviderName(provider);
                if (provider == null)
                {
                    return BadRequest("Invalid provider name. Supported providers: Microsoft, Atlassian, Google");
                }

                var redirectUrl = await _connectorService.StartConnectAsync(userId, provider);
                // google的如果localhost想測試要改回 測試版 否則發布後無法用localhost https://console.cloud.google.com/auth/audience?project=fleet-breaker-423004-t2
                return Ok(new ConnectResponseDto { RedirectUrl = redirectUrl });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid provider request: {Provider}", provider);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting OAuth flow for provider {Provider}", provider);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Disconnects a provider for the current user
        /// </summary>
        /// <param name="provider">The provider name</param>
        /// <returns>Success or error response</returns>
        [HttpDelete("{provider}")]
        public async Task<ActionResult> Disconnect(string provider)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in claims");
                }

                if (string.IsNullOrEmpty(provider))
                {
                    return BadRequest("Provider name is required");
                }

                provider = NormalizeProviderName(provider);
                if (provider == null)
                {
                    return BadRequest("Invalid provider name");
                }

                var success = await _connectorService.DisconnectAsync(userId, provider);
                if (success)
                {
                    return Ok(new { message = $"Successfully disconnected from {provider}" });
                }
                else
                {
                    return StatusCode(500, "Failed to disconnect provider");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting provider {Provider}", provider);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Manually refresh tokens for a provider (optional endpoint for debugging)
        /// </summary>
        /// <param name="provider">The provider name</param>
        /// <returns>Success or error response</returns>
        [HttpPost("{provider}/refresh")]
        public async Task<ActionResult> RefreshTokens(string provider)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in claims");
                }

                provider = NormalizeProviderName(provider);
                if (provider == null)
                {
                    return BadRequest("Invalid provider name");
                }

                var success = await _connectorService.RefreshTokenIfNeededAsync(userId, provider);
                if (success)
                {
                    return Ok(new { message = $"Tokens refreshed successfully for {provider}" });
                }
                else
                {
                    return BadRequest($"Failed to refresh tokens for {provider}. May need to reconnect.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing tokens for provider {Provider}", provider);
                return StatusCode(500, "Internal server error");
            }
        }

        private string? GetCurrentUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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