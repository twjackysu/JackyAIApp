namespace JackyAIApp.Server.Services
{
    public interface IECPayService
    {
        /// <summary>
        /// Creates ECPay payment form data (to POST to ECPay gateway).
        /// </summary>
        Task<Dictionary<string, string>> CreatePaymentAsync(string userId, string packId);

        /// <summary>
        /// Handles ECPay callback, verifies CheckMacValue, and adds credits.
        /// </summary>
        Task<bool> HandleCallbackAsync(IFormCollection form);
    }
}
