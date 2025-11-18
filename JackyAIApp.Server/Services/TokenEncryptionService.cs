using System.Security.Cryptography;
using System.Text;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Implementation of token encryption service using AES-256-CBC with HMAC
    /// </summary>
    public class TokenEncryptionService : ITokenEncryptionService
    {
        private readonly byte[] _encryptionKey;
        private readonly byte[] _hmacKey;

        public TokenEncryptionService(IConfiguration configuration)
        {
            // Get encryption key from Azure Key Vault or configuration
            var keyString = configuration["TokenEncryption:Key"]
                ?? throw new InvalidOperationException("Token encryption key not found in configuration");

            var masterKey = Convert.FromBase64String(keyString);

            if (masterKey.Length != 64) // 512 bits total: 256 for AES + 256 for HMAC
            {
                throw new ArgumentException("Master key must be 512 bits (64 bytes)");
            }

            _encryptionKey = new byte[32]; // First 32 bytes for AES-256
            _hmacKey = new byte[32];       // Last 32 bytes for HMAC-SHA256

            Array.Copy(masterKey, 0, _encryptionKey, 0, 32);
            Array.Copy(masterKey, 32, _hmacKey, 0, 32);
        }

        public string EncryptToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                throw new ArgumentException("Token cannot be null or empty", nameof(token));

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _encryptionKey;

            // Generate random IV
            aes.GenerateIV();
            var iv = aes.IV;

            var plainTextBytes = Encoding.UTF8.GetBytes(token);
            byte[] cipherBytes;

            using (var encryptor = aes.CreateEncryptor())
            using (var msEncrypt = new MemoryStream())
            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            {
                csEncrypt.Write(plainTextBytes, 0, plainTextBytes.Length);
                csEncrypt.FlushFinalBlock();
                cipherBytes = msEncrypt.ToArray();
            }

            // Combine IV + ciphertext
            var payload = new byte[iv.Length + cipherBytes.Length];
            Array.Copy(iv, 0, payload, 0, iv.Length);
            Array.Copy(cipherBytes, 0, payload, iv.Length, cipherBytes.Length);

            // Generate HMAC for integrity
            using var hmac = new HMACSHA256(_hmacKey);
            var hash = hmac.ComputeHash(payload);

            // Combine payload + HMAC
            var result = new byte[payload.Length + hash.Length];
            Array.Copy(payload, 0, result, 0, payload.Length);
            Array.Copy(hash, 0, result, payload.Length, hash.Length);

            return Convert.ToBase64String(result);
        }

        public string DecryptToken(string encryptedToken)
        {
            if (string.IsNullOrEmpty(encryptedToken))
                throw new ArgumentException("Encrypted token cannot be null or empty", nameof(encryptedToken));

            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedToken);

                if (encryptedBytes.Length < 48) // 16 (IV) + 16 (min cipher) + 32 (HMAC) minimum
                    throw new ArgumentException("Invalid encrypted token format");

                // Extract payload and HMAC
                var payloadLength = encryptedBytes.Length - 32; // 32 bytes for HMAC-SHA256
                var payload = new byte[payloadLength];
                var expectedHmac = new byte[32];

                Array.Copy(encryptedBytes, 0, payload, 0, payloadLength);
                Array.Copy(encryptedBytes, payloadLength, expectedHmac, 0, 32);

                // Verify HMAC
                using (var hmac = new HMACSHA256(_hmacKey))
                {
                    var actualHmac = hmac.ComputeHash(payload);
                    if (!CryptographicOperations.FixedTimeEquals(expectedHmac, actualHmac))
                    {
                        throw new InvalidOperationException("Token integrity check failed");
                    }
                }

                // Extract IV and ciphertext
                var iv = new byte[16];
                var cipherBytes = new byte[payload.Length - 16];

                Array.Copy(payload, 0, iv, 0, 16);
                Array.Copy(payload, 16, cipherBytes, 0, cipherBytes.Length);

                // Decrypt
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = _encryptionKey;
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor();
                using var msDecrypt = new MemoryStream(cipherBytes);
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var srDecrypt = new StreamReader(csDecrypt);

                return srDecrypt.ReadToEnd();
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                throw new InvalidOperationException("Failed to decrypt token", ex);
            }
        }
    }
}