using Newtonsoft.Json;
using System.Net;
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
            catch (Exception ex)
            {
                await HandleExceptionAsync(httpContext, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var responseFactory = context.RequestServices.GetService<IMyResponseFactory>();
            
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var requestId = Guid.NewGuid().ToString();
            
            // Log exception with request details
            _logger.LogError(exception, "Unhandled exception. Request ID: {RequestId}", requestId);
            await LogRequestDetailsAsync(context, requestId);

            // Create standardized error response
            var response = responseFactory?.CreateErrorResponse(
                ErrorCodes.InternalServerError, 
                $"An unexpected error occurred. Request ID: {requestId}");

            await context.Response.WriteAsync(JsonConvert.SerializeObject(response));
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
