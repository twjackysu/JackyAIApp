using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.DTO
{
    // The site as an OAuth authorization server for its MCP server (docs/ai-native/design/03 §9.1). The OAuth messages
    // keep OAuth's own snake_case names; what the consent page reads is the app's camelCase.

    /// <summary>An authorization request (OAuth 2.1 §4.1.1), as the client sent it in the query, and as the consent page passes it on.</summary>
    public sealed class SiteOAuthAuthorizeRequestDto
    {
        [FromQuery(Name = "response_type"), JsonPropertyName("response_type")]
        public string? ResponseType { get; set; }

        [FromQuery(Name = "client_id"), JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [FromQuery(Name = "redirect_uri"), JsonPropertyName("redirect_uri")]
        public string? RedirectUri { get; set; }

        [FromQuery(Name = "code_challenge"), JsonPropertyName("code_challenge")]
        public string? CodeChallenge { get; set; }

        [FromQuery(Name = "code_challenge_method"), JsonPropertyName("code_challenge_method")]
        public string? CodeChallengeMethod { get; set; }

        [FromQuery(Name = "state"), JsonPropertyName("state")]
        public string? State { get; set; }

        [FromQuery(Name = "scope"), JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [FromQuery(Name = "resource"), JsonPropertyName("resource")]
        public string? Resource { get; set; }
    }

    /// <summary>The reader's answer on the consent page, with the request it answers.</summary>
    public sealed class SiteOAuthDecisionDto
    {
        public SiteOAuthAuthorizeRequestDto Request { get; set; } = new();
        public bool Approve { get; set; }
        public bool AllowWrites { get; set; }
    }

    /// <summary>What the consent page shows: who asks, where the reader goes back to, and whether it asked to write.</summary>
    public sealed record SiteOAuthConsentDto(string ClientName, string RedirectHost, bool WantsWrites);

    /// <summary>Where to send the reader after their answer (the client's redirect URI with a code, or an error).</summary>
    public sealed record SiteOAuthRedirectDto(string RedirectUrl);

    /// <summary>A token request (OAuth 2.1 §4.1.3), form-encoded.</summary>
    public sealed class SiteOAuthTokenRequestDto
    {
        [FromForm(Name = "grant_type")] public string? GrantType { get; set; }
        [FromForm(Name = "code")] public string? Code { get; set; }
        [FromForm(Name = "redirect_uri")] public string? RedirectUri { get; set; }
        [FromForm(Name = "client_id")] public string? ClientId { get; set; }
        [FromForm(Name = "code_verifier")] public string? CodeVerifier { get; set; }
        [FromForm(Name = "resource")] public string? Resource { get; set; }
    }

    /// <summary>A dynamic client registration request (RFC 7591), the fields the site reads.</summary>
    public sealed class SiteOAuthRegisterRequestDto
    {
        [JsonPropertyName("client_name")] public string? ClientName { get; set; }
        [JsonPropertyName("redirect_uris")] public List<string>? RedirectUris { get; set; }
        [JsonPropertyName("token_endpoint_auth_method")] public string? TokenEndpointAuthMethod { get; set; }
    }
}
