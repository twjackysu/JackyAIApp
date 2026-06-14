namespace JackyAIApp.Server.Services.Repository
{
    /// <summary>A curated starter set of words a learner can add to their repository in one click.</summary>
    public sealed record WordPack(string Id, string Name, string Description, IReadOnlyList<string> Words);

    /// <summary>
    /// Built-in topic word packs used to solve the cold-start problem: a new user with an empty
    /// repository can seed it instantly instead of having to look words up one at a time.
    /// Words are common, valid English words so dictionary generation succeeds.
    /// </summary>
    public static class WordPacks
    {
        public static readonly IReadOnlyList<WordPack> All = new List<WordPack>
        {
            new("workplace", "Workplace", "Everyday words for office and professional settings.", new[]
            {
                "deadline", "schedule", "colleague", "meeting", "project", "budget",
                "deliverable", "feedback", "priority", "stakeholder", "agenda", "milestone",
            }),
            new("interview", "Job Interview", "Vocabulary to help you shine in an English interview.", new[]
            {
                "experience", "strength", "weakness", "achievement", "responsibility", "teamwork",
                "leadership", "challenge", "motivation", "qualification", "opportunity", "candidate",
            }),
            new("travel", "Travel", "Essential words for trips and getting around abroad.", new[]
            {
                "luggage", "passport", "reservation", "departure", "arrival", "itinerary",
                "destination", "currency", "souvenir", "boarding", "customs", "accommodation",
            }),
            new("technology", "Technology", "Common vocabulary for the digital world.", new[]
            {
                "software", "hardware", "network", "database", "algorithm", "interface",
                "encryption", "bandwidth", "feature", "update", "device", "browser",
            }),
            new("everyday", "Everyday Conversation", "High-frequency words for daily chats.", new[]
            {
                "appreciate", "decide", "suggest", "prefer", "describe", "explain",
                "improve", "consider", "remember", "understand", "compare", "recommend",
            }),
        };

        public static WordPack? Find(string id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
