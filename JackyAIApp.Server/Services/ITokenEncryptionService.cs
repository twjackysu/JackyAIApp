namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Service for encrypting and decrypting OAuth tokens
    /// </summary>
    public interface ITokenEncryptionService
    {
        /// <summary>
        /// Encrypts a token using AES-256-GCM
        /// </summary>
        /// <param name="token">The plain text token to encrypt</param>
        /// <returns>Base64 encoded encrypted token with IV and tag</returns>
        string EncryptToken(string token);

        /// <summary>
        /// Decrypts a token that was encrypted with EncryptToken
        /// </summary>
        /// <param name="encryptedToken">The encrypted token from database</param>
        /// <returns>The decrypted plain text token</returns>
        string DecryptToken(string encryptedToken);
    }
}