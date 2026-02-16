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

        public ConnectorsController(IConnectorService connectorService)
        {
            _connectorService = connectorService;
        }

        /// <summary>
        /// Gets the connection status of all providers for the current user
        /// </summary>
        /// <returns>Array of connector statuses</returns>
        [HttpGet("status")]
        public async Task<ActionResult<ConnectorStatusDto[]>> GetStatus()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in claims");
            }

            var statuses = await _connectorService.GetUserConnectorStatusAsync(userId);
            return Ok(statuses);
        }

        /// <summary>
        /// Initiates OAuth flow for a specific provider
        /// </summary>
        /// <param name="provider">The provider name (Microsoft, Atlassian, Google)</param>
        /// <param name="customConfig">Optional custom OAuth configuration</param>
        /// <returns>OAuth authorization URL</returns>
        [HttpPost("{provider}/connect")]
        public async Task<ActionResult<ConnectResponseDto>> Connect(string provider, [FromBody] CustomConnectRequestDto? customConfig = null)
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
            var normalizedProvider = NormalizeProviderName(provider);
            if (normalizedProvider == null)
            {
                return BadRequest("Invalid provider name. Supported providers: Microsoft, Atlassian, Google");
            }

            var redirectUrl = await _connectorService.StartConnectAsync(userId, normalizedProvider, customConfig);
            return Ok(new ConnectResponseDto { RedirectUrl = redirectUrl });
        }

        /// <summary>
        /// Disconnects a provider for the current user
        /// </summary>
        /// <param name="provider">The provider name</param>
        /// <returns>Success or error response</returns>
        [HttpDelete("{provider}")]
        public async Task<ActionResult> Disconnect(string provider)
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

            var normalizedProvider = NormalizeProviderName(provider);
            if (normalizedProvider == null)
            {
                return BadRequest("Invalid provider name");
            }

            var success = await _connectorService.DisconnectAsync(userId, normalizedProvider);
            if (success)
            {
                return Ok(new { message = $"Successfully disconnected from {normalizedProvider}" });
            }
            
            return StatusCode(500, "Failed to disconnect provider");
        }

        /// <summary>
        /// Manually refresh tokens for a provider (optional endpoint for debugging)
        /// </summary>
        /// <param name="provider">The provider name</param>
        /// <returns>Success or error response</returns>
        [HttpPost("{provider}/refresh")]
        public async Task<ActionResult> RefreshTokens(string provider)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in claims");
            }

            var normalizedProvider = NormalizeProviderName(provider);
            if (normalizedProvider == null)
            {
                return BadRequest("Invalid provider name");
            }

            var success = await _connectorService.RefreshTokenIfNeededAsync(userId, normalizedProvider);
            if (success)
            {
                return Ok(new { message = $"Tokens refreshed successfully for {normalizedProvider}" });
            }
            
            return BadRequest($"Failed to refresh tokens for {normalizedProvider}. May need to reconnect.");
        }

        /// <summary>
        /// Gets the access token for a specific provider (for MCP Server usage)
        /// </summary>
        /// <param name="provider">The provider name</param>
        /// <returns>The access token</returns>
        [HttpGet("{provider}/token")]
        public async Task<ActionResult> GetAccessToken(string provider)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in claims");
            }

            var normalizedProvider = NormalizeProviderName(provider);
            if (normalizedProvider == null)
            {
                return BadRequest("Invalid provider name. Supported providers: Microsoft, Atlassian, Google");
            }

            var accessToken = await _connectorService.GetAccessTokenAsync(userId, normalizedProvider);
            if (string.IsNullOrEmpty(accessToken))
            {
                return NotFound($"No active connection found for {normalizedProvider}. Please connect first.");
            }

            return Ok(new { 
                provider = normalizedProvider,
                accessToken,
                retrievedAt = DateTime.UtcNow
            });
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
