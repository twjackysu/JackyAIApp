using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Web;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Implementation of connector service for OAuth provider management
    /// </summary>
    public class ConnectorService : IConnectorService
    {
        private readonly AzureSQLDBContext _context;
        private readonly ITokenEncryptionService _tokenEncryption;
        private readonly IStateService _stateService;
        private readonly ConnectorOptions _connectorOptions;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ConnectorService> _logger;

        public ConnectorService(
            AzureSQLDBContext context,
            ITokenEncryptionService tokenEncryption,
            IStateService stateService,
            IOptions<ConnectorOptions> connectorOptions,
            HttpClient httpClient,
            ILogger<ConnectorService> logger)
        {
            _context = context;
            _tokenEncryption = tokenEncryption;
            _stateService = stateService;
            _connectorOptions = connectorOptions.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<ConnectorStatusDto[]> GetUserConnectorStatusAsync(string userId)
        {
            var userConnectors = await _context.UserConnectors
                .Where(uc => uc.UserId == userId && uc.IsActive)
                .ToListAsync();

            var statuses = new List<ConnectorStatusDto>();

            foreach (var (providerName, config) in _connectorOptions.Providers)
            {
                var userConnector = userConnectors.FirstOrDefault(uc => uc.ProviderName == providerName);
                var isConnected = userConnector != null &&
                                  !string.IsNullOrEmpty(userConnector.EncryptedAccessToken);

                var requiresReconnection = false;
                if (userConnector != null && userConnector.RefreshTokenExpiresAt.HasValue)
                {
                    requiresReconnection = userConnector.RefreshTokenExpiresAt.Value <= DateTime.UtcNow.AddDays(7);
                }

                statuses.Add(new ConnectorStatusDto
                {
                    Provider = providerName,
                    ProviderDisplayName = config.DisplayName,
                    IsConnected = isConnected,
                    Services = config.Services,
                    ExpiresAt = userConnector?.TokenExpiresAt,
                    RequiresReconnection = requiresReconnection
                });
            }

            return statuses.ToArray();
        }

        public async Task<string> StartConnectAsync(string userId, string provider, CustomConnectRequestDto? customConfig = null)
        {
            if (!_connectorOptions.Providers.TryGetValue(provider, out var config))
            {
                throw new ArgumentException($"Unknown provider: {provider}");
            }

            // Use custom config if provided, otherwise use system defaults
            var clientId = !string.IsNullOrWhiteSpace(customConfig?.ClientId) ? customConfig.ClientId : config.ClientId;
            var scopes = !string.IsNullOrWhiteSpace(customConfig?.Scopes) ? customConfig.Scopes : config.Scopes;
            var tenantId = customConfig?.TenantId;

            // Build auth URL based on provider
            var authUrl = config.AuthUrl;
            
            // For Microsoft, replace tenant ID placeholder if custom tenant provided
            if (provider == "Microsoft" && !string.IsNullOrWhiteSpace(tenantId))
            {
                authUrl = authUrl.Replace("{YOUR_TENANT_ID}", tenantId);
            }

            var state = _stateService.GenerateState(userId, provider);
            var callbackUrl = $"{_connectorOptions.CallbackBaseUrl}/{provider}";

            authUrl += $"?client_id={Uri.EscapeDataString(clientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
                $"&scope={Uri.EscapeDataString(scopes)}" +
                $"&response_type=code" +
                $"&state={Uri.EscapeDataString(state)}";

            // Add provider-specific parameters
            if (provider == "Microsoft")
            {
                authUrl += "&response_mode=query";
            }
            else if (provider == "Atlassian")
            {
                authUrl += "&audience=api.atlassian.com&prompt=consent";
            }
            else if (provider == "Google")
            {
                authUrl += "&access_type=offline&prompt=consent";
            }

            // Save custom config to UserConnector for use during callback
            if (customConfig != null && HasCustomConfig(customConfig))
            {
                await SaveCustomConfigAsync(userId, provider, customConfig);
            }

            _logger.LogInformation("Generated OAuth URL for user {UserId} and provider {Provider} (custom config: {HasCustom})", 
                userId, provider, customConfig != null && HasCustomConfig(customConfig));
            return authUrl;
        }

        private static bool HasCustomConfig(CustomConnectRequestDto config)
        {
            return !string.IsNullOrWhiteSpace(config.ClientId) ||
                   !string.IsNullOrWhiteSpace(config.ClientSecret) ||
                   !string.IsNullOrWhiteSpace(config.TenantId) ||
                   !string.IsNullOrWhiteSpace(config.Scopes);
        }

        private async Task SaveCustomConfigAsync(string userId, string provider, CustomConnectRequestDto customConfig)
        {
            var existingConnector = await _context.UserConnectors
                .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ProviderName == provider);

            if (existingConnector != null)
            {
                // Update existing connector with custom config
                existingConnector.CustomClientId = customConfig.ClientId;
                existingConnector.EncryptedCustomClientSecret = !string.IsNullOrWhiteSpace(customConfig.ClientSecret)
                    ? _tokenEncryption.EncryptToken(customConfig.ClientSecret)
                    : null;
                existingConnector.CustomTenantId = customConfig.TenantId;
                existingConnector.CustomScopes = customConfig.Scopes;
                existingConnector.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // Create new connector record with custom config (tokens will be added during callback)
                var newConnector = new UserConnector
                {
                    UserId = userId,
                    ProviderName = provider,
                    CustomClientId = customConfig.ClientId,
                    EncryptedCustomClientSecret = !string.IsNullOrWhiteSpace(customConfig.ClientSecret)
                        ? _tokenEncryption.EncryptToken(customConfig.ClientSecret)
                        : null,
                    CustomTenantId = customConfig.TenantId,
                    CustomScopes = customConfig.Scopes,
                    IsActive = false, // Will be activated after successful callback
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.UserConnectors.Add(newConnector);
            }

            await _context.SaveChangesAsync();
        }

        public async Task<bool> HandleCallbackAsync(string provider, string code, string state)
        {
            try
            {
                // Validate state
                if (!_stateService.ValidateState(state, out var userId, out var stateProvider))
                {
                    _logger.LogWarning("Invalid state parameter in OAuth callback");
                    return false;
                }

                if (stateProvider != provider)
                {
                    _logger.LogWarning("Provider mismatch in OAuth callback: expected {Expected}, got {Actual}",
                        stateProvider, provider);
                    return false;
                }

                if (!_connectorOptions.Providers.TryGetValue(provider, out var config))
                {
                    _logger.LogError("Unknown provider in callback: {Provider}", provider);
                    return false;
                }

                // Check if user has custom config
                var userConnector = await _context.UserConnectors
                    .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ProviderName == provider);

                // Use custom config if available, otherwise use system defaults
                var clientId = !string.IsNullOrWhiteSpace(userConnector?.CustomClientId) 
                    ? userConnector.CustomClientId 
                    : config.ClientId;
                var clientSecret = !string.IsNullOrWhiteSpace(userConnector?.EncryptedCustomClientSecret)
                    ? _tokenEncryption.DecryptToken(userConnector.EncryptedCustomClientSecret)
                    : config.ClientSecret;
                var tokenUrl = config.TokenUrl;

                // For Microsoft, use custom tenant ID if available
                if (provider == "Microsoft" && !string.IsNullOrWhiteSpace(userConnector?.CustomTenantId))
                {
                    tokenUrl = tokenUrl.Replace("{YOUR_TENANT_ID}", userConnector.CustomTenantId);
                }

                // Exchange authorization code for tokens
                var tokenResponse = await ExchangeCodeForTokensAsync(clientId, clientSecret, tokenUrl, code, provider);
                if (tokenResponse == null)
                {
                    _logger.LogError("Failed to exchange code for tokens for provider {Provider}", provider);
                    return false;
                }

                // Save or update user connector
                await SaveUserConnectorAsync(userId, provider, tokenResponse);

                _logger.LogInformation("Successfully connected user {UserId} to provider {Provider}", userId, provider);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OAuth callback for provider {Provider}", provider);
                return false;
            }
        }

        public async Task<bool> DisconnectAsync(string userId, string provider)
        {
            try
            {
                var userConnector = await _context.UserConnectors
                    .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ProviderName == provider);

                if (userConnector != null)
                {
                    userConnector.IsActive = false;
                    userConnector.EncryptedAccessToken = null;
                    userConnector.EncryptedRefreshToken = null;
                    userConnector.TokenExpiresAt = null;
                    userConnector.RefreshTokenExpiresAt = null;
                    userConnector.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Disconnected user {UserId} from provider {Provider}", userId, provider);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting user {UserId} from provider {Provider}", userId, provider);
                return false;
            }
        }

        public async Task<bool> RefreshTokenIfNeededAsync(string userId, string provider)
        {
            try
            {
                var userConnector = await _context.UserConnectors
                    .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ProviderName == provider && uc.IsActive);

                if (userConnector == null || string.IsNullOrEmpty(userConnector.EncryptedRefreshToken))
                {
                    return false;
                }

                // Check if token needs refresh (expires within 1 hour)
                if (userConnector.TokenExpiresAt.HasValue &&
                    userConnector.TokenExpiresAt.Value > DateTime.UtcNow.AddHours(1))
                {
                    return true; // Token is still valid
                }

                if (!_connectorOptions.Providers.TryGetValue(provider, out var config))
                {
                    return false;
                }

                // Use custom config if available
                var clientId = !string.IsNullOrWhiteSpace(userConnector.CustomClientId)
                    ? userConnector.CustomClientId
                    : config.ClientId;
                var clientSecret = !string.IsNullOrWhiteSpace(userConnector.EncryptedCustomClientSecret)
                    ? _tokenEncryption.DecryptToken(userConnector.EncryptedCustomClientSecret)
                    : config.ClientSecret;
                var tokenUrl = config.TokenUrl;
                if (provider == "Microsoft" && !string.IsNullOrWhiteSpace(userConnector.CustomTenantId))
                {
                    tokenUrl = tokenUrl.Replace("{YOUR_TENANT_ID}", userConnector.CustomTenantId);
                }

                var refreshToken = _tokenEncryption.DecryptToken(userConnector.EncryptedRefreshToken);
                var tokenResponse = await RefreshAccessTokenAsync(clientId, clientSecret, tokenUrl, refreshToken);

                if (tokenResponse != null)
                {
                    await UpdateUserConnectorTokensAsync(userConnector, tokenResponse);
                    _logger.LogInformation("Refreshed tokens for user {UserId} and provider {Provider}", userId, provider);
                    return true;
                }

                _logger.LogWarning("Failed to refresh tokens for user {UserId} and provider {Provider}", userId, provider);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing tokens for user {UserId} and provider {Provider}", userId, provider);
                return false;
            }
        }

        private async Task<OAuthTokenResponse?> ExchangeCodeForTokensAsync(string clientId, string clientSecret, string tokenUrl, string code, string provider)
        {
            var callbackUrl = $"{_connectorOptions.CallbackBaseUrl}/{provider}";

            var requestBody = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["redirect_uri"] = callbackUrl
            };

            var formContent = new FormUrlEncodedContent(requestBody);
            var response = await _httpClient.PostAsync(tokenUrl, formContent);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Token exchange failed for provider {Provider}: {Error}", provider, error);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<OAuthTokenResponse>(json);
        }

        private async Task<OAuthTokenResponse?> RefreshAccessTokenAsync(string clientId, string clientSecret, string tokenUrl, string refreshToken)
        {
            var requestBody = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = refreshToken
            };

            var formContent = new FormUrlEncodedContent(requestBody);
            var response = await _httpClient.PostAsync(tokenUrl, formContent);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Token refresh failed: {Error}", error);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<OAuthTokenResponse>(json);
        }

        private async Task SaveUserConnectorAsync(string userId, string provider, OAuthTokenResponse tokenResponse)
        {
            var existingConnector = await _context.UserConnectors
                .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ProviderName == provider);

            if (existingConnector != null)
            {
                await UpdateUserConnectorTokensAsync(existingConnector, tokenResponse);
            }
            else
            {
                var newConnector = new UserConnector
                {
                    UserId = userId,
                    ProviderName = provider,
                    EncryptedAccessToken = _tokenEncryption.EncryptToken(tokenResponse.access_token),
                    EncryptedRefreshToken = !string.IsNullOrEmpty(tokenResponse.refresh_token)
                        ? _tokenEncryption.EncryptToken(tokenResponse.refresh_token)
                        : null,
                    TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in),
                    RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(90), // Most providers use 90 days
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.UserConnectors.Add(newConnector);
            }

            await _context.SaveChangesAsync();
        }

        private async Task UpdateUserConnectorTokensAsync(UserConnector connector, OAuthTokenResponse tokenResponse)
        {
            connector.EncryptedAccessToken = _tokenEncryption.EncryptToken(tokenResponse.access_token);

            if (!string.IsNullOrEmpty(tokenResponse.refresh_token))
            {
                connector.EncryptedRefreshToken = _tokenEncryption.EncryptToken(tokenResponse.refresh_token);
                connector.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(90);
            }

            connector.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in);
            connector.IsActive = true;
            connector.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        public async Task<string?> GetAccessTokenAsync(string userId, string provider)
        {
            try
            {
                var userConnector = await _context.UserConnectors
                    .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ProviderName == provider && uc.IsActive);

                if (userConnector == null || string.IsNullOrEmpty(userConnector.EncryptedAccessToken))
                {
                    return null;
                }

                // Check if token is expired and try to refresh
                if (userConnector.TokenExpiresAt.HasValue && userConnector.TokenExpiresAt.Value <= DateTime.UtcNow)
                {
                    var refreshed = await RefreshTokenIfNeededAsync(userId, provider);
                    if (!refreshed)
                    {
                        _logger.LogWarning("Token expired and refresh failed for user {UserId} and provider {Provider}", userId, provider);
                        return null;
                    }

                    // Re-fetch the connector after refresh
                    userConnector = await _context.UserConnectors
                        .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ProviderName == provider && uc.IsActive);

                    if (userConnector == null || string.IsNullOrEmpty(userConnector.EncryptedAccessToken))
                    {
                        return null;
                    }
                }

                return _tokenEncryption.DecryptToken(userConnector.EncryptedAccessToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting access token for user {UserId} and provider {Provider}", userId, provider);
                return null;
            }
        }
    }
}