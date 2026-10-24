using JackyAIApp.Server.Services;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// OAuth states that are valid when this issued them, for the user and provider it issued them for
    /// (the app's <see cref="StateService"/> signs them with a key from configuration).
    /// </summary>
    public sealed class TestStateService : IStateService
    {
        private readonly Dictionary<string, (string UserId, string Provider)> _issued = new();

        public string GenerateState(string userId, string provider)
        {
            var state = $"state-{_issued.Count + 1}";
            _issued[state] = (userId, provider);
            return state;
        }

        public bool ValidateState(string state, out string userId, out string provider)
        {
            var found = _issued.TryGetValue(state, out var issued);
            (userId, provider) = found ? issued : ("", "");
            return found;
        }
    }
}
