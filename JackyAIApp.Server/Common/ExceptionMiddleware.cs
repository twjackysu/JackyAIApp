using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;

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
                await _next(httpContext);
            }
            catch (BadHttpRequestException ex) when (!httpContext.Response.HasStarted)
            {
                // Kestrel refusing the request itself — e.g. a body over the action's [RequestSizeLimit]
                // (413). The client's fault, not a server error: keep its status.
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
                LogUnhandled(httpContext.Request, ex, requestId);

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

        /// <summary>
        /// Which request failed, without what was in it. The body, the form fields and the query
        /// values are what people typed — sentences, uploaded data, sign-in codes (the connector
        /// OAuth callback carries its authorization code in the query) — and are not logged. The
        /// query's parameter names are, so the call can still be told apart.
        /// </summary>
        private void LogUnhandled(HttpRequest request, Exception exception, string requestId) =>
            _logger.LogError(exception,
                "Unhandled exception. Request ID: {RequestId}, {Method} {Path}, query parameters: {QueryKeys}, body: {ContentType} ({ContentLength} bytes)",
                requestId, request.Method, request.Path.Value, string.Join(",", request.Query.Keys),
                request.ContentType ?? "none", request.ContentLength);
    }
}
