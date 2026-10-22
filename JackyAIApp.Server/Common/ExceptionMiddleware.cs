using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using System.Text;

namespace JackyAIApp.Server.Common
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                httpContext.Request.EnableBuffering();
                await _next(httpContext);
            }
            catch (BadHttpRequestException ex) when (!httpContext.Response.HasStarted)
            {
                // Kestrel refusing the request itself — e.g. a body over the action's [RequestSizeLimit]
                // (413). The client's fault, not a server error: keep its status, and don't go on to log
                // (and re-read) a body that was refused for being too big.
                _logger.LogWarning("Request rejected by the server: {status} {message}", ex.StatusCode, ex.Message);
                httpContext.Response.StatusCode = ex.StatusCode;
            }
            catch (NotSignedInException) when (!httpContext.Response.HasStarted)
            {
                // A signed-in user was required (ICurrentUser.RequireId) and there is none. Not a
                // server error: answer it as the sign-in middleware would, in the usual envelope.
                var responseFactory = httpContext.RequestServices.GetRequiredService<IMyResponseFactory>();
                var result = responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not signed in.");
                await result.ExecuteResultAsync(new ActionContext(httpContext, httpContext.GetRouteData(), new ActionDescriptor()));
            }
            catch (Exception ex)
            {
                var requestId = Guid.NewGuid().ToString();
                _logger.LogError(ex, "Unhandled exception. Request ID: {RequestId}", requestId);
                await LogRequestDetailsAsync(httpContext, requestId);

                // Part of the response is already sent (a stream): it can't become an error now.
                // Rethrown, the server aborts it, so the client sees it fail rather than end early.
                if (httpContext.Response.HasStarted) throw;

                // The usual { error: { code, message } } envelope, written by MVC like any other
                // result. It used to be the ObjectResult itself serialized by Newtonsoft
                // ({"Value":…,"Formatters":…}), which the client could not read a message from.
                var responseFactory = httpContext.RequestServices.GetRequiredService<IMyResponseFactory>();
                var result = responseFactory.CreateErrorResponse(
                    ErrorCodes.InternalServerError, $"An unexpected error occurred. Request ID: {requestId}");
                await result.ExecuteResultAsync(new ActionContext(httpContext, httpContext.GetRouteData(), new ActionDescriptor()));
            }
        }

        private async Task LogRequestDetailsAsync(HttpContext context, string requestId)
        {
            var request = context.Request;
            var sb = new StringBuilder();
            
            sb.AppendLine($"Request ID: {requestId}");
            sb.AppendLine($"Request path: {request.Path}");
            sb.AppendLine($"Request method: {request.Method}");

            if (request.QueryString.HasValue)
            {
                sb.AppendLine($"Query string: {request.QueryString}");
            }

            if (request.HasFormContentType && request.Form != null)
            {
                foreach (var formField in request.Form)
                {
                    sb.AppendLine($"Form field {formField.Key}: {formField.Value}");
                }
            }
            else if (request.ContentType != null && request.ContentType.Contains("application/json"))
            {
                if (request.Body.CanSeek)
                {
                    request.Body.Position = 0;
                    using var reader = new StreamReader(request.Body, leaveOpen: true);
                    var body = await reader.ReadToEndAsync();
                    sb.AppendLine($"Request body: {body}");
                }
            }
            
            _logger.LogInformation(sb.ToString());
        }
    }
}
