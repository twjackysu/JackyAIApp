namespace JackyAIApp.Server.DTO
{
    /// <summary>
    /// GET api/account/info: what the client needs to know about the signed-in user, and no more
    /// (the user's row also holds the usage-limit multiplier, XP and streaks).
    /// </summary>
    /// <param name="DrawdownBrakeEnabled">Whether this user may use 回落煞車 (admin, or
    /// <c>Features:DrawdownBrake:Audience</c> is <c>All</c>); the client shows the feature only then.</param>
    public record AccountInfoResponse(string Id, string? Name, string? Email, bool IsAdmin, bool DrawdownBrakeEnabled);
}
