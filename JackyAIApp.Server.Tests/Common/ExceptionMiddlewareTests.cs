using System.Text.Json;
using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Common
{
    public class ExceptionMiddlewareTests
    {
        private static DefaultHttpContext NewContext()
        {
            var services = new ServiceCollection().AddLogging();
            services.AddMvcCore();
            services.AddSingleton<IMyResponseFactory>(new ResponseFactory(new ApiResponseFactory()));
            var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            http.Response.Body = new MemoryStream();
            return http;
        }

        [Fact]
        public async Task UnhandledException_IsAnswered500_InTheErrorEnvelope()
        {
            // It used to be the ObjectResult itself, serialized: {"Value":…,"Formatters":…,"StatusCode":500}.
            var http = NewContext();
            var middleware = new ExceptionMiddleware(
                _ => throw new InvalidOperationException("boom"), NullLogger<ExceptionMiddleware>.Instance);

            await middleware.InvokeAsync(http);

            Assert.Equal(StatusCodes.Status500InternalServerError, http.Response.StatusCode);
            http.Response.Body.Position = 0;
            using var body = await JsonDocument.ParseAsync(http.Response.Body);
            var message = body.RootElement.GetProperty("error").GetProperty("message").GetString();
            Assert.StartsWith("An unexpected error occurred. Request ID: ", message);
        }

        private sealed class StartedResponseFeature : HttpResponseFeature
        {
            public override bool HasStarted => true;
        }

        [Fact]
        public async Task ExceptionAfterTheResponseStarted_IsRethrown_NotWrittenOverIt()
        {
            var http = NewContext();
            http.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
            var middleware = new ExceptionMiddleware(
                _ => throw new InvalidOperationException("mid-stream"), NullLogger<ExceptionMiddleware>.Instance);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(http));

            Assert.Equal("mid-stream", thrown.Message);
        }
    }
}
