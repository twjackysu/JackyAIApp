namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>A record keyed by a string <c>Id</c>. Lets one generic helper pick a random row and
    /// exclude recently-seen ids across the exam banks (listening, listening-response, reading),
    /// which previously each had their own near-identical pool query.</summary>
    public interface IStringId
    {
        string Id { get; }
    }
}
