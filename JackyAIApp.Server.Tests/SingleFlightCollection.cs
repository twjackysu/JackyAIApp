namespace JackyAIApp.Server.Tests
{
    /// <summary>
    /// Test classes that exercise a service built on <see cref="Server.Common.SingleFlight"/>
    /// (PortfolioService, DividendService, FundDataService) belong to this collection, which xUnit
    /// runs one class at a time.
    ///
    /// SingleFlight is process-wide by design: concurrent fetches for the same key share one
    /// in-flight task, so in production a burst of requests for the same symbol costs one upstream
    /// call. Under xUnit's default parallelism, though, two test classes each mocking different
    /// prices for the same symbol (most of them use "2330") can land on the same key at the same
    /// moment, and one class then receives the other's mocked data. That made the drawdown and
    /// fee/tax tests fail intermittently — on master as well, roughly every other full run.
    ///
    /// Serialising only these classes keeps the rest of the suite parallel. A new test class that
    /// constructs one of those services must join this collection.
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class SingleFlightCollection
    {
        public const string Name = "Services sharing the process-wide SingleFlight";
    }
}
