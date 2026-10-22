using System.Text.Json;
using DotnetSdkUtilities.Factory.ResponseFactory;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        [Fact]
        public async Task UnhandledException_LogsWhichRequest_ButNotWhatWasInIt()
        {
            // The connector OAuth callback carries its authorization code in the query; bodies hold
            // what people typed. Neither belongs in the log.
            var http = NewContext();
            http.Request.Method = "POST";
            http.Request.Path = "/api/connectors/callback/Atlassian";
            http.Request.QueryString = new QueryString("?code=secret-auth-code&state=xyz");
            http.Request.ContentType = "application/json";
            var body = """{"sentence":"my private diary entry"}"""u8.ToArray();
            var original = new MemoryStream(body);
            http.Request.Body = original;
            http.Request.ContentLength = body.Length;
            var logger = new RecordingLogger<ExceptionMiddleware>();
            var middleware = new ExceptionMiddleware(async context =>
            {
                // The action read the body, as model binding does, then failed.
                await new StreamReader(context.Request.Body).ReadToEndAsync();
                throw new InvalidOperationException("boom");
            }, logger);

            await middleware.InvokeAsync(http);

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Contains("POST /api/connectors/callback/Atlassian", entry.Message);
            Assert.Contains("code,state", entry.Message);
            Assert.Contains($"application/json ({body.Length} bytes)", entry.Message);
            Assert.DoesNotContain("secret-auth-code", entry.Message);
            Assert.DoesNotContain("xyz", entry.Message);
            Assert.DoesNotContain("diary", entry.Message);
            Assert.Same(original, http.Request.Body); // no longer swapped for a buffered, rewindable copy
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
