using DotnetSdkUtilities.Factory.ResponseFactory;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Common
{
    public interface IMyResponseFactory : IResponseFactory<IActionResult, ErrorCodes>
    {
    }
    public class ResponseFactory : IMyResponseFactory
    {
        private readonly IApiResponseFactory _apiResponseFactory;
        public ResponseFactory(IApiResponseFactory apiResponseFactory)
        {
            _apiResponseFactory = apiResponseFactory;
        }
        public IActionResult CreateOKResponse()
        {
            return new NoContentResult();
        }
        public IActionResult CreateOKResponse<T>(T data)
        {
            return new OkObjectResult(_apiResponseFactory.CreateOKResponse(data));
        }
        public IActionResult CreateErrorResponse(ErrorCodes code, string message = "")
        {
            int httpCode = (int)code / 100 % 1000;
            return new ObjectResult(_apiResponseFactory.CreateErrorResponse(code, message))
            {
                StatusCode = httpCode
            };
        }
    }
}
