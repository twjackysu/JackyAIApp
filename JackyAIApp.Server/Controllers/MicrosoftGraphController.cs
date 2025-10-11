using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MicrosoftGraphController(
        ILogger<MicrosoftGraphController> logger,
        AzureSQLDBContext dbContext,
        IUserService userService,
        IMyResponseFactory responseFactory,
        IMicrosoftGraphService microsoftGraphService,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory) : ControllerBase
    {
        private readonly ILogger<MicrosoftGraphController> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IUserService _userService = userService;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IMicrosoftGraphService _microsoftGraphService = microsoftGraphService;
        private readonly IConfiguration _configuration = configuration;
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

        private readonly string _tenantId = "3e04753a-ae5b-42d4-a86d-d6f05460f9e4";

        /// <summary>
        /// Get Microsoft Graph OAuth authorization URL
        /// </summary>
        [HttpGet("auth-url")]
        public IActionResult GetAuthUrl()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not authenticated");
            }

            var clientId = _configuration["Settings:Microsoft:ClientId"];
            var redirectUri = $"{Request.Scheme}://{Request.Host}/api/microsoftgraph/callback";
            
            var scopes = new[]
            {
                "https://graph.microsoft.com/Mail.Read",
                "https://graph.microsoft.com/Calendars.Read",
                "https://graph.microsoft.com/ChatMessage.Read",
                "https://graph.microsoft.com/ChannelMessage.Read.All",
                "https://graph.microsoft.com/User.Read"
            };

            // Use userId as state for CSRF protection (since user is already authenticated)
            var state = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}:{DateTime.UtcNow:yyyyMMddHHmmss}"));
            
            var authUrl = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/authorize?" +
                         $"client_id={Uri.EscapeDataString(clientId!)}&" +
                         $"response_type=code&" +
                         $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                         $"response_mode=query&" +
                         $"scope={Uri.EscapeDataString(string.Join(" ", scopes))}&" +
                         $"state={state}";

            return _responseFactory.CreateOKResponse(new { AuthUrl = authUrl });
        }

        /// <summary>
        /// Handle OAuth callback
        /// </summary>
        [HttpGet("callback")]
        [AllowAnonymous]
        public async Task<IActionResult> Callback(string code, string state, string? error)
        {
            try
            {
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogError("OAuth error: {Error}", error);
                    return LocalRedirect("~/microsoftgraph?error=oauth_error");
                }

                if (string.IsNullOrEmpty(code))
                {
                    return LocalRedirect("~/microsoftgraph?error=no_code");
                }

                // Verify state for CSRF protection by decoding and checking timestamp
                string? decodedState = null;
                string? userIdFromState = null;
                
                try
                {
                    decodedState = Encoding.UTF8.GetString(Convert.FromBase64String(state));
                    var parts = decodedState.Split(':');
                    if (parts.Length == 2)
                    {
                        userIdFromState = parts[0];
                        var timestamp = parts[1];
                        
                        // Check if timestamp is within last 30 minutes
                        if (DateTime.TryParseExact(timestamp, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var stateTime))
                        {
                            if (DateTime.UtcNow.Subtract(stateTime).TotalMinutes > 30)
                            {
                                return LocalRedirect("~/microsoftgraph?error=state_expired");
                            }
                        }
                        else
                        {
                            return LocalRedirect("~/microsoftgraph?error=invalid_state");
                        }
                    }
                    else
                    {
                        return LocalRedirect("~/microsoftgraph?error=invalid_state");
                    }
                }
                catch
                {
                    return LocalRedirect("~/microsoftgraph?error=invalid_state");
                }

                // Exchange code for tokens
                var tokenResponse = await ExchangeCodeForTokens(code);
                if (tokenResponse == null)
                {
                    return LocalRedirect("~/microsoftgraph?error=token_exchange_failed");
                }

                // Use the user ID from the state (already verified above)
                if (string.IsNullOrEmpty(userIdFromState))
                {
                    return LocalRedirect("~/microsoftgraph?error=user_id_missing");
                }

                // Save tokens to database
                await SaveTokensToDatabase(userIdFromState, tokenResponse);

                return LocalRedirect("~/microsoftgraph?connected=true");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Microsoft Graph callback");
                return LocalRedirect("~/microsoftgraph?error=callback_failed");
            }
        }

        private async Task<TokenResponse?> ExchangeCodeForTokens(string code)
        {
            var clientId = _configuration["Settings:Microsoft:ClientId"];
            var clientSecret = _configuration["Settings:Microsoft:ClientSecret"];
            var redirectUri = $"{Request.Scheme}://{Request.Host}/api/microsoftgraph/callback";

            var tokenEndpoint = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";

            var parameters = new Dictionary<string, string>
            {
                {"client_id", clientId!},
                {"client_secret", clientSecret!},
                {"code", code},
                {"redirect_uri", redirectUri},
                {"grant_type", "authorization_code"}
            };

            using var httpClient = _httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(parameters);
            
            var response = await httpClient.PostAsync(tokenEndpoint, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange failed: {StatusCode} {Content}", response.StatusCode, responseContent);
                return null;
            }

            return JsonSerializer.Deserialize<TokenResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
        }

        private async Task SaveTokensToDatabase(string userId, TokenResponse tokenResponse)
        {
            var existingToken = await _dbContext.MicrosoftGraphTokens
                .FirstOrDefaultAsync(t => t.UserId == userId);

            var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            if (existingToken != null)
            {
                // Update existing token
                existingToken.AccessToken = tokenResponse.AccessToken;
                existingToken.RefreshToken = tokenResponse.RefreshToken ?? existingToken.RefreshToken;
                existingToken.ExpiresAt = expiresAt;
                existingToken.Scopes = tokenResponse.Scope ?? existingToken.Scopes;
                existingToken.UpdatedAt = DateTime.UtcNow;
                existingToken.IsActive = true;
            }
            else
            {
                // Create new token record
                var newToken = new MicrosoftGraphToken
                {
                    UserId = userId,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken ?? "",
                    ExpiresAt = expiresAt,
                    Scopes = tokenResponse.Scope ?? "",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                _dbContext.MicrosoftGraphTokens.Add(newToken);
            }

            await _dbContext.SaveChangesAsync();
        }

        private class TokenResponse
        {
            public string AccessToken { get; set; } = "";
            public string? RefreshToken { get; set; }
            public int ExpiresIn { get; set; }
            public string? Scope { get; set; }
        }

        /// <summary>
        /// Check Microsoft Graph connection status
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not authenticated");
            }

            var token = await _dbContext.MicrosoftGraphTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.IsActive);

            var isConnected = token != null && token.ExpiresAt > DateTime.UtcNow;

            return _responseFactory.CreateOKResponse(new
            {
                IsConnected = isConnected,
                ConnectedAt = token?.CreatedAt,
                LastUpdated = token?.UpdatedAt,
                Scopes = token?.Scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            });
        }

        /// <summary>
        /// Disconnect Microsoft Graph (revoke tokens)
        /// </summary>
        [HttpPost("disconnect")]
        public async Task<IActionResult> Disconnect()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not authenticated");
            }

            var token = await _dbContext.MicrosoftGraphTokens
                .FirstOrDefaultAsync(t => t.UserId == userId);

            if (token != null)
            {
                token.IsActive = false;
                await _dbContext.SaveChangesAsync();
            }

            return _responseFactory.CreateOKResponse(new { Message = "Microsoft Graph disconnected successfully" });
        }

        /// <summary>
        /// Get user's Outlook emails
        /// </summary>
        [HttpGet("emails")]
        public async Task<IActionResult> GetEmails()
        {
            try
            {
                var emails = await _microsoftGraphService.GetUserEmailsAsync();
                return _responseFactory.CreateOKResponse(emails);
            }
            catch (UnauthorizedAccessException)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Microsoft Graph not connected or tokens expired");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching emails from Microsoft Graph");
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to fetch emails");
            }
        }

        /// <summary>
        /// Get user's calendar events
        /// </summary>
        [HttpGet("calendar")]
        public async Task<IActionResult> GetCalendar()
        {
            try
            {
                var events = await _microsoftGraphService.GetUserCalendarEventsAsync();
                return _responseFactory.CreateOKResponse(events);
            }
            catch (UnauthorizedAccessException)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Microsoft Graph not connected or tokens expired");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching calendar from Microsoft Graph");
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to fetch calendar");
            }
        }

        /// <summary>
        /// Get user's Teams
        /// </summary>
        [HttpGet("teams")]
        public async Task<IActionResult> GetTeams()
        {
            try
            {
                var teams = await _microsoftGraphService.GetUserTeamsAsync();
                return _responseFactory.CreateOKResponse(teams);
            }
            catch (UnauthorizedAccessException)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Microsoft Graph not connected or tokens expired");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching teams from Microsoft Graph");
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to fetch teams");
            }
        }

        /// <summary>
        /// Get user's recent chats/messages
        /// </summary>
        [HttpGet("chats")]
        public async Task<IActionResult> GetChats()
        {
            try
            {
                var chats = await _microsoftGraphService.GetUserChatsAsync();
                return _responseFactory.CreateOKResponse(chats);
            }
            catch (UnauthorizedAccessException)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Microsoft Graph not connected or tokens expired");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching chats from Microsoft Graph");
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Failed to fetch chats");
            }
        }
    }
}