using JackyAIApp.Server.Services.DataAnalysis;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>An <see cref="IServerAddressCheck"/> that finds every name on the internet, or none of <see cref="Private"/>.</summary>
    public sealed class PublicAddresses : IServerAddressCheck
    {
        public HashSet<string> Private { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Checked { get; } = [];

        public Task<bool> IsPublicAsync(string host, CancellationToken ct)
        {
            Checked.Add(host);
            return Task.FromResult(!Private.Contains(host));
        }
    }
}
