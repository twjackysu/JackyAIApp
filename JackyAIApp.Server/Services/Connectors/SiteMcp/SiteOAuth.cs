using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Connectors.SiteMcp
{
    // The site as an OAuth authorization server for its own MCP server (design/03 §9.1), so an AI that signs in with
    // OAuth only (Claude.ai, ChatGPT) can use it too. These are the parts with rules of their own; the endpoints and the
    // reader's consent come on top of them.

    /// <summary>
    /// Which redirect URIs a client may register (OAuth 2.1 §2.3.1, RFC 8252): an absolute https URI, or http to the
    /// machine itself (a native app's loopback listener); never a fragment or credentials in it.
    /// </summary>
    public static class SiteOAuthRedirects
    {
        public const int MaxLength = 500;

        public static bool IsAllowed(string? uri)
        {
            if (string.IsNullOrEmpty(uri) || uri.Length > MaxLength) return false;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
            if (parsed.Fragment.Length > 0 || parsed.UserInfo.Length > 0) return false;
            return parsed.Scheme == Uri.UriSchemeHttps ||
                (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback);
        }

        /// <summary>
        /// The part of the address the reader is shown on the consent page ("claude.ai"), an international name in its
        /// ASCII (punycode) form, so a lookalike ("сlaude.ai" with a Cyrillic с) does not read as the real one (D-190).
        /// </summary>
        public static string HostOf(string uri) => new Uri(uri).IdnHost;
    }

    /// <summary>A client as it registered: what it calls itself and where its sign-ins may come back to.</summary>
    public sealed record SiteOAuthClient(string Name, IReadOnlyList<string> RedirectUris);

    /// <summary>
    /// Dynamic client registration (RFC 7591) without a table: a client id is the registration itself, protected
    /// (signed and encrypted) by ASP.NET Core data protection, so the site only has to read it back. Public clients only
    /// (PKCE, no secret). A client id the keys can no longer read is unknown: the client registers again, as MCP
    /// clients do on <c>invalid_client</c>.
    /// </summary>
    public sealed class SiteOAuthClients(IDataProtectionProvider protection)
    {
        public const int MaxRedirectUris = 10;
        public const int MaxNameLength = 40;

        /// <summary>The most the redirect URIs of one registration may come to, together.</summary>
        public const int MaxRedirectUrisLength = 2_000;

        private readonly IDataProtector _protector = protection.CreateProtector("SiteMcp.OAuthClient.v1");

        private sealed record Stored(string N, string[] R);

        /// <summary>The client id for a registration, or null when what it asks for is not allowed.</summary>
        public string? Register(string? clientName, IReadOnlyList<string>? redirectUris)
        {
            if (redirectUris is not { Count: > 0 and <= MaxRedirectUris } || !redirectUris.All(SiteOAuthRedirects.IsAllowed)) return null;
            // The client id carries them all and travels in /oauth/authorize's query: keep it well inside a request line.
            if (redirectUris.Sum(u => u.Length) > MaxRedirectUrisLength) return null;
            var name = NameOf(clientName);
            var json = JsonSerializer.Serialize(new Stored(name, redirectUris.Distinct(StringComparer.Ordinal).ToArray()));
            return _protector.Protect(json);
        }

        public SiteOAuthClient? Read(string? clientId)
        {
            if (string.IsNullOrEmpty(clientId) || clientId.Length > 8_000) return null;
            try
            {
                var stored = JsonSerializer.Deserialize<Stored>(_protector.Unprotect(clientId));
                return stored is { N: not null, R.Length: > 0 } ? new SiteOAuthClient(stored.N, stored.R) : null;
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// The client's own name, cut to fit and stripped of control and format characters (a right-to-left override or
        /// a zero-width space could make it read as another name, D-190); "AI" when it gave none.
        /// </summary>
        internal static string NameOf(string? clientName)
        {
            var cleaned = new string((clientName ?? "")
                .Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format)
                .ToArray()).Trim();
            if (cleaned.Length == 0) return "AI";
            return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength] : cleaned;
        }
    }

    /// <summary>What a reader allowed, held by an authorization code until the client trades it for a token.</summary>
    /// <param name="CodeChallenge">The PKCE S256 challenge the authorization request carried.</param>
    public sealed record SiteOAuthGrant(
        string UserId, string ClientId, string ClientName, string RedirectUri, string CodeChallenge, bool AllowWrites);

    /// <summary>
    /// Authorization codes: random, kept in memory for <see cref="Lifetime"/>, good once. (One app instance, as the site
    /// runs; a code issued before a restart is lost, and the reader signs in again.)
    /// </summary>
    public sealed class SiteOAuthCodes(IMemoryCache cache)
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        private readonly Lock _redeeming = new();

        private static string Key(string code) => $"SiteMcp:OAuthCode:{SiteAccessTokenFormat.Hash(code)}";

        public string Issue(SiteOAuthGrant grant)
        {
            var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            cache.Set(Key(code), grant, Lifetime);
            return code;
        }

        /// <summary>The grant a code holds, once: a second try finds nothing.</summary>
        public SiteOAuthGrant? Redeem(string? code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 200) return null;
            lock (_redeeming)
            {
                if (!cache.TryGetValue(Key(code), out SiteOAuthGrant? grant)) return null;
                cache.Remove(Key(code));
                return grant;
            }
        }
    }

    /// <summary>PKCE (RFC 7636) with S256, the only method the site takes.</summary>
    public static class Pkce
    {
        /// <summary>Whether a challenge looks like an S256 one (43 base64url characters).</summary>
        public static bool IsS256Challenge(string? challenge) =>
            challenge is { Length: 43 } && challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

        /// <summary>Whether <paramref name="verifier"/> (43–128 unreserved characters) is the one behind <paramref name="challenge"/>.</summary>
        public static bool Verifies(string? verifier, string challenge)
        {
            if (verifier is not { Length: >= 43 and <= 128 } ||
                !verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~'))
                return false;
            var computed = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
        }
    }
}
