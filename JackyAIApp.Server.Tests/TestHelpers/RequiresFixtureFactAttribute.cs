namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// A <c>[Fact]</c> that is reported as skipped (not passed) while its fixture file does not
    /// exist yet. For acceptance tests waiting on data someone else has to supply: unlike an early
    /// <c>return</c>, the skip shows up in the test results, so the gap cannot go unnoticed.
    /// </summary>
    public sealed class RequiresFixtureFactAttribute : FactAttribute
    {
        public RequiresFixtureFactAttribute(string fixtureFile)
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureFile)))
                Skip = $"Fixtures/{fixtureFile} has not been provided yet.";
        }
    }
}
