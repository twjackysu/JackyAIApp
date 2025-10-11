using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using System.Net.Http.Headers;

namespace JackyAIApp.Server.Services
{
    public class MicrosoftGraphService : IMicrosoftGraphService
    {
        private readonly AzureSQLDBContext _dbContext;
        private readonly IUserService _userService;
        private readonly ILogger<MicrosoftGraphService> _logger;

        public MicrosoftGraphService(
            AzureSQLDBContext dbContext,
            IUserService userService,
            ILogger<MicrosoftGraphService> logger)
        {
            _dbContext = dbContext;
            _userService = userService;
            _logger = logger;
        }

        private async Task<GraphServiceClient> GetGraphServiceClientAsync()
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedAccessException("User not authenticated");
            }

            var token = await _dbContext.MicrosoftGraphTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.IsActive);

            if (token == null)
            {
                throw new UnauthorizedAccessException("Microsoft Graph not connected");
            }

            if (token.ExpiresAt <= DateTime.UtcNow)
            {
                // Token expired, need to refresh
                // For now, we'll throw an exception
                // In a production scenario, you'd implement token refresh logic here
                throw new UnauthorizedAccessException("Microsoft Graph token expired");
            }

            // Create HttpClient with bearer token
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", token.AccessToken);

            // Create GraphServiceClient with HttpClient
            var graphServiceClient = new GraphServiceClient(httpClient);
            return graphServiceClient;
        }

        public async Task<object> GetUserProfileAsync()
        {
            try
            {
                var graphClient = await GetGraphServiceClientAsync();
                var user = await graphClient.Me.GetAsync();

                return new
                {
                    Id = user?.Id,
                    DisplayName = user?.DisplayName,
                    Email = user?.Mail ?? user?.UserPrincipalName,
                    JobTitle = user?.JobTitle,
                    Department = user?.Department,
                    CompanyName = user?.CompanyName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user profile from Microsoft Graph");
                throw;
            }
        }

        public async Task<object> GetUserEmailsAsync()
        {
            try
            {
                var graphClient = await GetGraphServiceClientAsync();
                var messages = await graphClient.Me.Messages
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Top = 10;
                        config.QueryParameters.Select = new[] { 
                            "subject", "from", "receivedDateTime", "bodyPreview", "importance" 
                        };
                        config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                    });

                return new
                {
                    TotalCount = messages?.Value?.Count ?? 0,
                    Emails = messages?.Value?.Select(m => new
                    {
                        Subject = m.Subject,
                        From = m.From?.EmailAddress?.Name ?? m.From?.EmailAddress?.Address,
                        FromEmail = m.From?.EmailAddress?.Address,
                        ReceivedDateTime = m.ReceivedDateTime,
                        BodyPreview = m.BodyPreview,
                        Importance = m.Importance?.ToString()
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching emails from Microsoft Graph");
                throw;
            }
        }

        public async Task<object> GetUserCalendarEventsAsync()
        {
            try
            {
                var graphClient = await GetGraphServiceClientAsync();
                var events = await graphClient.Me.Events
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Top = 10;
                        config.QueryParameters.Select = new[] { 
                            "subject", "start", "end", "location", "attendees", "organizer" 
                        };
                        config.QueryParameters.Filter = $"start/dateTime ge '{DateTime.Today:yyyy-MM-ddTHH:mm:ss.fffK}'";
                        config.QueryParameters.Orderby = new[] { "start/dateTime" };
                    });

                return new
                {
                    TotalCount = events?.Value?.Count ?? 0,
                    Events = events?.Value?.Select(e => new
                    {
                        Subject = e.Subject,
                        Start = e.Start,
                        End = e.End,
                        Location = e.Location?.DisplayName,
                        Organizer = e.Organizer?.EmailAddress?.Name,
                        AttendeesCount = e.Attendees?.Count() ?? 0
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching calendar events from Microsoft Graph");
                throw;
            }
        }

        public async Task<object> GetUserTeamsAsync()
        {
            try
            {
                var graphClient = await GetGraphServiceClientAsync();
                var teams = await graphClient.Me.JoinedTeams
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Top = 10;
                        config.QueryParameters.Select = new[] { 
                            "id", "displayName", "description", "createdDateTime" 
                        };
                    });

                return new
                {
                    TotalCount = teams?.Value?.Count ?? 0,
                    Teams = teams?.Value?.Select(t => new
                    {
                        Id = t.Id,
                        DisplayName = t.DisplayName,
                        Description = t.Description,
                        CreatedDateTime = t.CreatedDateTime
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching teams from Microsoft Graph");
                throw;
            }
        }

        public async Task<object> GetUserChatsAsync()
        {
            try
            {
                var graphClient = await GetGraphServiceClientAsync();
                var chats = await graphClient.Me.Chats
                    .GetAsync(config =>
                    {
                        config.QueryParameters.Top = 10;
                        config.QueryParameters.Select = new[] { 
                            "id", "topic", "createdDateTime", "lastUpdatedDateTime", "chatType" 
                        };
                        config.QueryParameters.Orderby = new[] { "lastUpdatedDateTime desc" };
                    });

                return new
                {
                    TotalCount = chats?.Value?.Count ?? 0,
                    Chats = chats?.Value?.Select(c => new
                    {
                        Id = c.Id,
                        Topic = c.Topic ?? "Chat",
                        CreatedDateTime = c.CreatedDateTime,
                        LastUpdatedDateTime = c.LastUpdatedDateTime,
                        ChatType = c.ChatType?.ToString()
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching chats from Microsoft Graph");
                throw;
            }
        }
    }
}