using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Services.Jira.DTOs;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace JackyAIApp.Server.Services.Jira
{
    public class JiraRestApiService(ILogger<JiraRestApiService> logger, AzureSQLDBContext DBContext, IUserService userService, IExtendedMemoryCache memoryCache) : IJiraRestApiService
    {
        private readonly ILogger<JiraRestApiService> _logger = logger ?? throw new ArgumentNullException();
        private readonly HttpClient _httpClient = new();
        private readonly AzureSQLDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;


        public async Task<JiraSearchResponse?> SearchAsync(string jiraConfigId,  string jql)
        {
            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                throw new UserNotFoundException($"{userId} user not found.");
            }
            var config = user.JiraConfigs?.SingleOrDefault(x => x.Id == jiraConfigId);

            if(config == null)
            {
                throw new JiraConfigNotFoundException($"{jiraConfigId} config not found.");
            }
            var domain = config.Domain;
            var email = config.Email;
            var token = config.Token;

            jql = jql.Trim();
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(jql))
            {
                throw new ArgumentException("wrong input.");
            }

            var url = $"https://{domain.Trim()}.atlassian.net/rest/api/2/search";
            var requestBody = new
            {
                jql,
                fields = new[] { "summary", "description", "labels" },
                maxResults = 300
            };

            var jsonBody = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<JiraSearchResponse>(responseString);

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search error: {message}", ex.Message);
                throw;
            }
        }

        public async Task<IEnumerable<JiraConfig>> GetJiraConfigs()
        {
            var userId = _userService.GetUserId();
            var cacheKey = $"{nameof(GetJiraConfigs)}_{userId}";
            if (!_memoryCache.TryGetValue(cacheKey, out IEnumerable<JiraConfig>? configs))
            {
                var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
                configs = user?.JiraConfigs ?? [];
                if (configs.Any())
                {
                    _memoryCache.Set(cacheKey, configs, TimeSpan.FromDays(1));
                }
            }
            return configs ?? [];
        }
        public async Task<string> AddJiraConfig(string domain, string email, string token)
        {
            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                throw new UserNotFoundException($"{userId} user not found.");
            }
            if (user.JiraConfigs == null)
            {
                user.JiraConfigs = [];
            }
            var id = Guid.NewGuid().ToString();
            user.JiraConfigs.Add(new JiraConfig() { Id = id, Domain = domain, Email = email, Token = token, UserId = userId });
            await _DBContext.SaveChangesAsync();

            var cacheKey = $"{nameof(GetJiraConfigs)}_{userId}";
            _memoryCache.Remove(cacheKey);

            return id;
        }

        public async Task DeleteJiraConfig(string jiraConfigId)
        {
            var userId = _userService.GetUserId();
            var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                throw new UserNotFoundException($"{userId} user not found.");
            }
            var config = user.JiraConfigs?.SingleOrDefault(x => x.Id == jiraConfigId);
            if (config == null)
            {
                throw new JiraConfigNotFoundException($"{jiraConfigId} config not found.");
            }
            if (user.JiraConfigs != null)
            {
                user.JiraConfigs.Remove(config);
                await _DBContext.SaveChangesAsync();

                var cacheKey = $"{nameof(GetJiraConfigs)}_{userId}";
                _memoryCache.Remove(cacheKey);
            }
        }
    }
}
