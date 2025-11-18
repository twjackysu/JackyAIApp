using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Implementation of state service with HMAC-based signing for security
    /// </summary>
    public class StateService : IStateService
    {
        private readonly byte[] _hmacKey;
        private readonly TimeSpan _stateExpiry = TimeSpan.FromMinutes(10); // State expires in 10 minutes

        public StateService(IConfiguration configuration)
        {
            // Use a separate key for state signing (different from token encryption)
            var keyString = configuration["TokenEncryption:Key"]
                ?? throw new InvalidOperationException("State signing key not found in configuration");

            // Use the second half of the master key for state signing
            var masterKey = Convert.FromBase64String(keyString);
            _hmacKey = new byte[32];
            Array.Copy(masterKey, 32, _hmacKey, 0, 32); // Use HMAC key portion
        }

        public string GenerateState(string userId, string provider)
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

            if (string.IsNullOrEmpty(provider))
                throw new ArgumentException("Provider cannot be null or empty", nameof(provider));

            var payload = new StatePayload
            {
                UserId = userId,
                Provider = provider,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Nonce = GenerateNonce()
            };

            var json = JsonSerializer.Serialize(payload);
            var payloadBytes = Encoding.UTF8.GetBytes(json);
            var payloadBase64 = Convert.ToBase64String(payloadBytes);

            // Sign the payload with HMAC
            using var hmac = new HMACSHA256(_hmacKey);
            var signature = hmac.ComputeHash(payloadBytes);
            var signatureBase64 = Convert.ToBase64String(signature);

            // Return payload.signature format
            return $"{payloadBase64}.{signatureBase64}";
        }

        public bool ValidateState(string state, out string userId, out string provider)
        {
            userId = string.Empty;
            provider = string.Empty;

            if (string.IsNullOrEmpty(state))
                return false;

            try
            {
                var parts = state.Split('.');
                if (parts.Length != 2)
                    return false;

                var payloadBase64 = parts[0];
                var signatureBase64 = parts[1];

                var payloadBytes = Convert.FromBase64String(payloadBase64);
                var expectedSignature = Convert.FromBase64String(signatureBase64);

                // Verify HMAC signature
                using (var hmac = new HMACSHA256(_hmacKey))
                {
                    var actualSignature = hmac.ComputeHash(payloadBytes);
                    if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
                    {
                        return false; // Signature verification failed
                    }
                }

                // Deserialize and validate payload
                var json = Encoding.UTF8.GetString(payloadBytes);
                var payload = JsonSerializer.Deserialize<StatePayload>(json);

                if (payload == null)
                    return false;

                // Check expiration
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(payload.Timestamp);
                if (DateTimeOffset.UtcNow - timestamp > _stateExpiry)
                {
                    return false; // State expired
                }

                userId = payload.UserId;
                provider = payload.Provider;
                return true;
            }
            catch
            {
                return false; // Any parsing error means invalid state
            }
        }

        private static string GenerateNonce()
        {
            var bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Internal structure for state payload
        /// </summary>
        private class StatePayload
        {
            public string UserId { get; set; } = string.Empty;
            public string Provider { get; set; } = string.Empty;
            public long Timestamp { get; set; }
            public string Nonce { get; set; } = string.Empty;
        }
    }
}