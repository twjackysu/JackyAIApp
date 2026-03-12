namespace JackyAIApp.Server.Services
{
    public interface IPayPalService
    {
        Task<string> CreateOrderAsync(string userId, string packId);
        Task<bool> CaptureOrderAsync(string orderId);
    }
}
