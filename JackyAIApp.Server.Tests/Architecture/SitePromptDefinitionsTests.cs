using System.Reflection;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Connectors.SiteMcp;

namespace JackyAIApp.Server.Tests.Architecture
{
    /// <summary>
    /// Every ready-made request the site's MCP server offers (<see cref="SitePrompt"/>, docs/ai-native/CONTRIBUTING-CONNECTORS.md)
    /// is declared the same way — a <c>public static readonly SitePrompt</c> in a class whose <c>All</c> lists it — with a
    /// name a client can make a slash command of, words for the reader, and a text that asks for the site tools it names,
    /// which must be tools the site has. A new prompt is checked here without a test of its own.
    /// </summary>
    public partial class SitePromptDefinitionsTests
    {
        [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
        private static partial Regex Key();

        private static readonly Type[] Types = typeof(SitePrompt).Assembly.GetTypes();

        private static readonly (Type Owner, SitePrompt Prompt)[] Declared = Types
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(SitePrompt))
                .Select(f => (t, (SitePrompt)f.GetValue(null)!)))
            .ToArray();

        private static readonly HashSet<string> SiteToolNames = Types
            .Where(t => typeof(IAiTool).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .Select(t => t.GetField("Definition", BindingFlags.Public | BindingFlags.Static)?.GetValue(null))
            .OfType<AiToolDescriptor>()
            .Select(d => d.Name)
            .ToHashSet();

        public static TheoryData<string> Prompts()
        {
            var data = new TheoryData<string>();
            foreach (var (_, prompt) in Declared) data.Add(prompt.Name);
            return data;
        }

        private static SitePrompt Prompt(string name) => Declared.Single(d => d.Prompt.Name == name).Prompt;

        /// <summary>A value for each argument, required or not, that the text must carry.</summary>
        private static Dictionary<string, string> SampleArguments(SitePrompt prompt) =>
            prompt.Arguments.ToDictionary(a => a.Name, a => a.Name == "count" ? "2" : $"範例{a.Name}");

        [Fact]
        public void ThePrompts_AreFound_AndNoTwoShareAName()
        {
            var names = Declared.Select(d => d.Prompt.Name).ToList();

            Assert.True(names.Count >= 6, "the site's prompts were not found");
            Assert.Equal(names.Distinct().Count(), names.Count);
        }

        [Fact]
        public void EachClassDeclaringPrompts_ListsThemAllInAll_SoTheyAreRegistered()
        {
            foreach (var owner in Declared.Select(d => d.Owner).Distinct())
            {
                var all = owner.GetProperty("All", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IReadOnlyList<SitePrompt>;
                Assert.True(all != null, $"{owner.Name} declares prompts but has no All");
                Assert.Equal(
                    Declared.Where(d => d.Owner == owner).Select(d => d.Prompt.Name).OrderBy(n => n),
                    all!.Select(p => p.Name).OrderBy(n => n));
            }
        }

        [Theory]
        [MemberData(nameof(Prompts))]
        public void ItsNameAndArguments_AreKeysAClientCanUse(string name)
        {
            var prompt = Prompt(name);

            Assert.Matches(Key(), prompt.Name);
            Assert.All(prompt.Arguments, a => Assert.Matches(Key(), a.Name));
            Assert.Equal(prompt.Arguments.Count, prompt.Arguments.Select(a => a.Name).Distinct().Count());
        }

        [Theory]
        [MemberData(nameof(Prompts))]
        public void ItSaysWhatItIs_ToTheReader(string name)
        {
            var prompt = Prompt(name);

            Assert.False(string.IsNullOrWhiteSpace(prompt.Title));
            Assert.True(prompt.Title.Length <= 12, $"{name}'s title is long for a menu: {prompt.Title}");
            Assert.True(prompt.Description.Length >= 10, $"{name} says too little about what it asks for");
            Assert.All(prompt.Arguments, a => Assert.False(string.IsNullOrWhiteSpace(a.Description)));
        }

        [Theory]
        [MemberData(nameof(Prompts))]
        public void ItAsksForSiteTools_ThatExist_ByTheirNames(string name)
        {
            var prompt = Prompt(name);
            var text = prompt.Text(SampleArguments(prompt));

            Assert.NotEmpty(prompt.Tools);
            Assert.All(prompt.Tools, tool =>
            {
                Assert.Contains(tool, SiteToolNames);
                Assert.Contains(tool, text);
            });
            Assert.Contains(SiteMcpServer.ServerName, text);
        }

        [Theory]
        [MemberData(nameof(Prompts))]
        public void ItsTextCarriesEveryArgument_AndWorksWithOnlyTheRequiredOnes(string name)
        {
            var prompt = Prompt(name);
            var all = SampleArguments(prompt);

            var withAll = prompt.Text(all);
            var withRequired = prompt.Text(all.Where(a => prompt.Arguments.Single(x => x.Name == a.Key).Required)
                .ToDictionary(a => a.Key, a => a.Value));

            Assert.All(all.Values, value => Assert.Contains(value, withAll));
            Assert.False(string.IsNullOrWhiteSpace(withRequired));
        }
    }
}
