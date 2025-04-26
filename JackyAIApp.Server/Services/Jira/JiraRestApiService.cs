using JackyAIApp.Server.Services.Jira.DTOs;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace JackyAIApp.Server.Services.Jira
{
    public class JiraRestApiService(ILogger<JiraRestApiService> logger) : IJiraRestApiService
    {
        private readonly ILogger<JiraRestApiService> _logger = logger ?? throw new ArgumentNullException();
        private readonly HttpClient _httpClient = new();


        public async Task<JiraSearchResponse?> SearchAsync(string domain, string email, string token, string jql)
        {
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
    }
}
