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
        QueryOpenAIFailed = 1040001,
        OpenAIResponseUnsuccessful = 1040002,
        UnparseableFormat = 1040003,
        TheWordCannotBeFound = 1040004,
        /// <summary>A chat attachment whose content cannot be read as its file type says.</summary>
        AttachmentUnreadable = 1040005,
        /// <summary>A PDF chat attachment with more pages than the model reads at once.</summary>
        AttachmentTooManyPages = 1040006,
        Unauthorized = 1040100,
        Forbidden = 1040300,
        DrawdownBrakeNotEnabled = 1040301,
        PayloadTooLarge = 1041300,
        /// <summary>A chat attachment of a file type that is not accepted.</summary>
        AttachmentUnsupportedType = 1041500,
        /// <summary>An upstream service failed or had no data: 502. It was 1040500 until 2026-10, which
        /// by the rule above answered 405 Method Not Allowed.</summary>
        ExternalApiError = 1050200,
        UsageLimitExceeded = 1042900,
        TooManyRequests = 1042901,
        MarketHistoryUnavailable = 1050301,
    }
}
