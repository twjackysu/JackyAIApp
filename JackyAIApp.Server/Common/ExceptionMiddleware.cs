using DotnetSdkUtilities.Factory.ResponseFactory;
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
            var factory = httpContext.RequestServices.GetService<IApiResponseFactory>();
            try
            {
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(httpContext, ex, factory);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception, IApiResponseFactory apiResponseFactory)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = apiResponseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, exception.Message);
            var requestId = Guid.NewGuid().ToString();
            // Log exception
            _logger.LogError(exception, $"An unhandled exception has occurred while executing the request. Request ID: {requestId}");

            // Log request data
            var request = context.Request;
            var informationSB = new StringBuilder();
            informationSB.AppendLine($"Request path: {request.Path}");
            informationSB.AppendLine($"Request method: {request.Method}");
            informationSB.AppendLine($"Request ID: {requestId}");

            if (request.QueryString.HasValue)
            {
                informationSB.AppendLine($"Request query string: {request.QueryString}");
            }

            if (request.HasFormContentType && request.Form != null)
            {
                foreach (var formField in request.Form)
                {
                    informationSB.AppendLine($"Form field {formField.Key}: {formField.Value}");
                }
            }
            else if (request.ContentType != null && request.ContentType.Contains("application/json"))
            {
                if (request.Body.CanSeek) request.Body.Position = 0; // Reset the request body stream position
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync();
                informationSB.AppendLine($"Request body: {body}");
            }
            _logger.LogInformation(informationSB.ToString());

            await context.Response.WriteAsync(JsonConvert.SerializeObject(response));
        }
    }
}
