using System.Security.Claims;
using DataAnalysisLibrary.Core.Engine;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Services.Prompt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// Builds the DataAnalysis controller for tests, so what the controller is made of is written in this
    /// one file. Set only what the test exercises; what it leaves out is not touched by the endpoint it calls.
    /// </summary>
    public sealed class DataAnalysisControllerBuilder
    {
        /// <summary>The engine of the built-in default source.</summary>
        public required DataAnalysisEngine DefaultEngine { get; init; }

        /// <summary>Where saved custom sources are read from; needed only for a custom source.</summary>
        public AzureSQLDBContext? Db { get; init; }

        public IDataSourceConfigService? ConfigService { get; init; }

        public ISessionDataStore? Sessions { get; init; }

        public IPromptLoader? Prompts { get; init; }

        public INlQueryPlanner? NlPlanner { get; init; }

        public IMemoryCache? Cache { get; init; }

        /// <summary>The signed-in caller, or null for an anonymous request.</summary>
        public string? UserId { get; init; }

        /// <summary>Defaults to <see cref="MessageResponses"/>.</summary>
        public IMyResponseFactory? Responses { get; init; }

        public DataAnalysisController Build()
        {
            var http = new DefaultHttpContext
            {
                User = UserId is null
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId)], "Cookies")),
            };
            http.Response.Body = new MemoryStream();
            var currentUser = TestCurrentUser.Of(http);
            var cache = Cache ?? new MemoryCache(new MemoryCacheOptions());

            var engines = new EngineSources(
                new DataSourceResolver(DefaultEngine, Db!, ConfigService!, NullLogger<DataSourceResolver>.Instance),
                currentUser, cache, NullLogger<EngineSources>.Instance);
            return new DataAnalysisController(
                Responses ?? MessageResponses(),
                new DataSourceStore(NullLogger<DataSourceStore>.Instance, Db!, ConfigService!, currentUser),
                Sessions!,
                new DataAnalysisQueryService(engines, Sessions!, NullLogger<DataAnalysisQueryService>.Instance),
                new NlSummarizeService(Prompts!, NlPlanner!, Sessions!, engines,
                    cache, currentUser, NullLogger<NlSummarizeService>.Instance))
            {
                ControllerContext = new ControllerContext { HttpContext = http },
            };
        }

        /// <summary>
        /// The responses the controller tests read: a failure is an <see cref="ObjectResult"/> holding its
        /// message, with its status; a success an <see cref="OkObjectResult"/> holding its payload.
        /// </summary>
        public static IMyResponseFactory MessageResponses()
        {
            var factory = new Mock<IMyResponseFactory>();
            factory.Setup(f => f.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                   .Returns((ErrorCodes code, string message) => new ObjectResult(message) { StatusCode = (int)code / 100 % 1000 });
            factory.Setup(f => f.CreateOKResponse(It.IsAny<object>()))
                   .Returns((object data) => new OkObjectResult(data));
            return factory.Object;
        }
    }
}
