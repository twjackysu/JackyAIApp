namespace JackyAIApp.Server.Common
{
    public enum ErrorCodes
    {
        // The first two codes represent app (in the case of future microservice architecture, it is better to know which service problem is)
        // The middle three codes can be directly related to the Http code
        // The last two codes are used for more detailed error distinction
        InternalServerError = 1050000,
        NotFound = 1040400,
        BadRequest = 1040000,
        Unauthorized = 1040100,
        Forbidden = 1040300,
    }
}
