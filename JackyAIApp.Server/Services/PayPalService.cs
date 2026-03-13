using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services
{
    public class PayPalService : IPayPalService
    {
        private readonly ICreditService _creditService;
        private readonly PayPalOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<PayPalService> _logger;

        public PayPalService(
            ICreditService creditService,
            IOptions<PayPalOptions> options,
            HttpClient httpClient,
            ILogger<PayPalService> logger)
        {
            _creditService = creditService;
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> CreateOrderAsync(string userId, string packId)
        {
            var pack = StripeService.CreditPacks.FirstOrDefault(p => p.Id == packId)
                ?? throw new ArgumentException($"Unknown pack: {packId}");

            var accessToken = await GetAccessTokenAsync();
            var priceUsd = (pack.PriceInCents / 100m).ToString("F2");

            var orderBody = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = $"{userId}|{packId}|{pack.Credits}",
                        description = $"{pack.Name} — {pack.Credits:N0} credits",
                        amount = new
                        {
                            currency_code = "USD",
                            value = priceUsd,
                        },
                    }
                },
                application_context = new
                {
                    return_url = $"{_options.AppBaseUrl}/credits?status=success&provider=paypal",
                    cancel_url = $"{_options.AppBaseUrl}/credits?status=cancelled&provider=paypal",
                },
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/v2/checkout/orders");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(orderBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(json);
            var approveLink = doc.RootElement
                .GetProperty("links")
                .EnumerateArray()
                .FirstOrDefault(l => l.GetProperty("rel").GetString() == "approve")
                .GetProperty("href")
                .GetString();

            return approveLink ?? throw new Exception("PayPal did not return an approve link");
        }

        public async Task<bool> CaptureOrderAsync(string orderId)
        {
            var accessToken = await GetAccessTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/v2/checkout/orders/{orderId}/capture");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal capture failed: {Response}", json);
                return false;
            }

            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.GetProperty("status").GetString();
            if (status != "COMPLETED")
            {
                _logger.LogWarning("PayPal order {OrderId} status: {Status}", orderId, status);
                return false;
            }

            // Extract user/pack info from reference_id
            var referenceId = doc.RootElement
                .GetProperty("purchase_units")[0]
                .GetProperty("reference_id")
                .GetString() ?? "";

            var parts = referenceId.Split('|');
            if (parts.Length != 3 || !ulong.TryParse(parts[2], out var credits))
            {
                _logger.LogError("Invalid reference_id: {ReferenceId}", referenceId);
                return false;
            }

            var userId = parts[0];
            var packId = parts[1];

            return await _creditService.AddCreditsAsync(
                userId, credits, "purchase",
                $"Purchased {packId} pack via PayPal",
                $"PayPal order: {orderId}",
                orderId);
        }

        private async Task<string> GetAccessTokenAsync()
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString()!;
        }
    }
}
