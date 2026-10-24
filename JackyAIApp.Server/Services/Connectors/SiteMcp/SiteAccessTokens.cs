using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Connectors.SiteMcp
{
    /// <summary>
    /// What a personal access token looks like: <c>jsk_</c> and 43 base64url characters (32 random bytes). Only its
    /// SHA-256 is stored, so a leaked table gives no token away.
    /// </summary>
    public static class SiteAccessTokenFormat
    {
        public const string Prefix = "jsk_";

        private const int RandomBytes = 32;

        private static readonly SearchValues<char> Base64UrlChars =
            SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

        /// <summary>The length of every token this site makes.</summary>
        public static readonly int Length = Prefix.Length + Base64UrlLength(RandomBytes);

        public static string Generate() => Prefix + Base64Url(RandomNumberGenerator.GetBytes(RandomBytes));

        /// <summary>Whether <paramref name="token"/> could be one of ours, before anything is looked up.</summary>
        public static bool IsWellFormed(string? token) =>
            token != null && token.Length == Length && token.StartsWith(Prefix, StringComparison.Ordinal) &&
            token.AsSpan(Prefix.Length).IndexOfAnyExcept(Base64UrlChars) < 0;

        /// <summary>The token's SHA-256 as lowercase hex, which is what is stored and looked up.</summary>
        public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        /// <summary>Enough of the token for its owner to tell it from their others ("jsk_AbC…xYz9"), and no more.</summary>
        public static string Hint(string token) => $"{token[..(Prefix.Length + 3)]}…{token[^4..]}";

        private static int Base64UrlLength(int bytes) => (bytes * 4 + 2) / 3;

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Who a request's token stands for, once it checked out.</summary>
    public sealed record SiteTokenHolder(string UserId, string TokenId, bool AllowWrites);

    /// <summary>
    /// The reader's personal access tokens for the site's MCP server (made, listed and revoked on the connectors
    /// page), and the check of a request's token.
    /// </summary>
    public interface ISiteAccessTokenService
    {
        Task<ServiceResult<SiteAccessTokenListDto>> ListAsync(CancellationToken ct);

        /// <summary>Makes a token; the answer is the only time it is shown.</summary>
        Task<ServiceResult<CreatedSiteAccessTokenDto>> CreateAsync(CreateSiteAccessTokenRequestDto request, CancellationToken ct);

        /// <summary>Revokes one of the reader's tokens: from then on it is refused. Someone else's is NotFound.</summary>
        Task<ServiceResult<NoData>> RevokeAsync(string tokenId, CancellationToken ct);

        /// <summary>
        /// Who <paramref name="token"/> stands for, or null for one that is not a token of ours, was revoked or
        /// expired. Notes when it was used (at most once a minute, so a busy client does not write every request).
        /// </summary>
        Task<SiteTokenHolder?> AuthenticateAsync(string token, CancellationToken ct);
    }

    public sealed class SiteAccessTokenService(AzureSQLDBContext db, ICurrentUser currentUser, TimeProvider time) : ISiteAccessTokenService
    {
        /// <summary>The most tokens one reader may have at a time.</summary>
        public const int Limit = 10;

        public const int MaxNameLength = 50;

        /// <summary>The lifetimes a token may be given, in days.</summary>
        public static readonly IReadOnlyList<int> Lifetimes = [30, 90, 365];

        private static readonly TimeSpan UsageNoteInterval = TimeSpan.FromMinutes(1);

        public async Task<ServiceResult<SiteAccessTokenListDto>> ListAsync(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var rows = await db.SiteAccessTokens.AsNoTracking()
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(ct);
            return ServiceResult.Ok(new SiteAccessTokenListDto(rows.Select(Dto).ToList(), Limit));
        }

        public async Task<ServiceResult<CreatedSiteAccessTokenDto>> CreateAsync(CreateSiteAccessTokenRequestDto request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var name = request.Name?.Trim() ?? "";
            if (name.Length == 0 || name.Length > MaxNameLength)
                return ServiceResult.Fail(ErrorCodes.BadRequest, $"請替這個 token 取個名字（{MaxNameLength} 字以內）。");
            if (!Lifetimes.Contains(request.ExpiresInDays))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "有效期限只能是 30、90 或 365 天。");
            if (await db.SiteAccessTokens.CountAsync(t => t.UserId == userId, ct) >= Limit)
                return ServiceResult.Fail(ErrorCodes.BadRequest, $"最多只能有 {Limit} 個 token，請先撤銷用不到的。");

            var token = SiteAccessTokenFormat.Generate();
            var now = time.GetUtcNow().UtcDateTime;
            var row = new SQL.SiteAccessToken
            {
                UserId = userId,
                Name = name,
                TokenHash = SiteAccessTokenFormat.Hash(token),
                Hint = SiteAccessTokenFormat.Hint(token),
                AllowWrites = request.AllowWrites,
                CreatedAt = now,
                ExpiresAt = now.AddDays(request.ExpiresInDays),
            };
            db.SiteAccessTokens.Add(row);
            await db.SaveChangesAsync(ct);
            return ServiceResult.Ok(new CreatedSiteAccessTokenDto(Dto(row), token));
        }

        public async Task<ServiceResult<NoData>> RevokeAsync(string tokenId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var row = await db.SiteAccessTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId, ct);
            if (row == null) return ServiceResult.Fail(ErrorCodes.NotFound, "找不到這個 token。");

            db.SiteAccessTokens.Remove(row);
            await db.SaveChangesAsync(ct);
            return ServiceResult.Ok();
        }

        public async Task<SiteTokenHolder?> AuthenticateAsync(string token, CancellationToken ct)
        {
            if (!SiteAccessTokenFormat.IsWellFormed(token)) return null;
            var hash = SiteAccessTokenFormat.Hash(token);
            var row = await db.SiteAccessTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            var now = time.GetUtcNow().UtcDateTime;
            if (row == null || row.ExpiresAt <= now) return null;

            if (row.LastUsedAt is not { } used || now - used >= UsageNoteInterval)
            {
                row.LastUsedAt = now;
                // Two requests at once may both note it; the later write wins, which is as good.
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { /* revoked meanwhile: the next request is refused */ }
            }
            return new SiteTokenHolder(row.UserId, row.Id, row.AllowWrites);
        }

        private static SiteAccessTokenDto Dto(SQL.SiteAccessToken t) =>
            new(t.Id, t.Name, t.Hint, t.AllowWrites, t.CreatedAt, t.ExpiresAt, t.LastUsedAt);
    }
}
