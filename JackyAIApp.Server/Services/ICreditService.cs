using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services
{
    public interface ICreditService
    {
        /// <summary>
        /// Default credit balance for new users
        /// </summary>
        const ulong DefaultInitialCredits = 200;

        /// <summary>
        /// Get the current credit balance for a user
        /// </summary>
        Task<ulong> GetBalanceAsync(string userId);

        /// <summary>
        /// Check if user has sufficient credits
        /// </summary>
        Task<bool> HasSufficientCreditsAsync(string userId, ulong requiredCredits);

        /// <summary>
        /// Consume credits from user's balance
        /// </summary>
        /// <returns>True if successful, false if insufficient credits</returns>
        Task<bool> ConsumeCreditsAsync(string userId, ulong credits, string reason, string? description = null);

        /// <summary>
        /// Add credits to user's balance (for top-up, refund, bonus)
        /// </summary>
        Task<bool> AddCreditsAsync(string userId, ulong credits, string transactionType, string reason, string? description = null, string? referenceId = null);

        /// <summary>
        /// Get transaction history for a user
        /// </summary>
        Task<List<CreditTransaction>> GetTransactionHistoryAsync(string userId, int pageNumber = 1, int pageSize = 20);

        /// <summary>
        /// Get total transaction count for a user
        /// </summary>
        Task<int> GetTransactionCountAsync(string userId);
    }
}
