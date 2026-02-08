using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services
{
    public class CreditService : ICreditService
    {
        private readonly AzureSQLDBContext _dbContext;
        private readonly ILogger<CreditService> _logger;

        public CreditService(AzureSQLDBContext dbContext, ILogger<CreditService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<ulong> GetBalanceAsync(string userId)
        {
            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            return user?.CreditBalance ?? 0;
        }

        public async Task<bool> HasSufficientCreditsAsync(string userId, ulong requiredCredits)
        {
            var balance = await GetBalanceAsync(userId);
            return balance >= requiredCredits;
        }

        public async Task<bool> ConsumeCreditsAsync(string userId, ulong credits, string reason, string? description = null)
        {
            // Use a transaction to ensure atomicity
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            
            try
            {
                var user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    _logger.LogWarning("Attempt to consume credits for non-existent user: {UserId}", userId);
                    return false;
                }

                if (user.CreditBalance < credits)
                {
                    _logger.LogWarning("Insufficient credits for user {UserId}. Required: {Required}, Available: {Available}",
                        userId, credits, user.CreditBalance);
                    return false;
                }

                // Deduct credits
                user.CreditBalance -= credits;
                user.TotalCreditsUsed += credits;
                user.LastUpdated = DateTime.UtcNow;

                // Record transaction
                var creditTransaction = new CreditTransaction
                {
                    UserId = userId,
                    Amount = -(long)credits,
                    BalanceAfter = user.CreditBalance,
                    TransactionType = CreditTransactionType.Consume,
                    Reason = reason,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.CreditTransactions.Add(creditTransaction);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("User {UserId} consumed {Credits} credits for {Reason}. New balance: {Balance}",
                    userId, credits, reason, user.CreditBalance);

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error consuming credits for user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> AddCreditsAsync(string userId, ulong credits, string transactionType, string reason, string? description = null, string? referenceId = null)
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                var user = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    _logger.LogWarning("Attempt to add credits for non-existent user: {UserId}", userId);
                    return false;
                }

                // Add credits
                user.CreditBalance += credits;
                user.LastUpdated = DateTime.UtcNow;

                // Record transaction
                var creditTransaction = new CreditTransaction
                {
                    UserId = userId,
                    Amount = (long)credits,
                    BalanceAfter = user.CreditBalance,
                    TransactionType = transactionType,
                    Reason = reason,
                    Description = description,
                    ReferenceId = referenceId,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.CreditTransactions.Add(creditTransaction);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("User {UserId} received {Credits} credits ({Type}) for {Reason}. New balance: {Balance}",
                    userId, credits, transactionType, reason, user.CreditBalance);

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error adding credits for user {UserId}", userId);
                throw;
            }
        }

        public async Task<List<CreditTransaction>> GetTransactionHistoryAsync(string userId, int pageNumber = 1, int pageSize = 20)
        {
            return await _dbContext.CreditTransactions
                .AsNoTracking()
                .Where(ct => ct.UserId == userId)
                .OrderByDescending(ct => ct.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetTransactionCountAsync(string userId)
        {
            return await _dbContext.CreditTransactions
                .AsNoTracking()
                .CountAsync(ct => ct.UserId == userId);
        }
    }
}
