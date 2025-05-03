using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Services.Jira;
using JackyAIApp.Server.Services.Jira.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class JiraController(ILogger<JiraController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, IJiraRestApiService jiraRestApiService): ControllerBase
    {
        private readonly ILogger<JiraController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly IJiraRestApiService _jiraRestApiService = jiraRestApiService;

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] JiraSearchRequest request)
        {
            var issues = await _jiraRestApiService.SearchAsync(request.JiraConfigId, request.Jql);
            return _responseFactory.CreateOKResponse(issues);
        }
        [HttpGet("configs")]
        public async Task<IActionResult> GetConfigs()
        {
            var issues = await _jiraRestApiService.GetJiraConfigs();
            return _responseFactory.CreateOKResponse(issues);
        }
        [HttpPost("configs")]
        public async Task<IActionResult> PostConfigs([FromBody] JiraConfigRequest request)
        {
            var issues = await _jiraRestApiService.AddJiraConfig(request.Domain, request.Email, request.Token);
            return _responseFactory.CreateOKResponse(issues);
        }
    }
}
