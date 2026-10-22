using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Tests.Architecture
{
    /// <summary>
    /// The server's modules and which of them each may use (JackyAIApp.Server/CLAUDE.md, "Modules").
    /// A source file belongs to the module of its namespace; every namespace it names — a <c>using</c>,
    /// or a fully qualified name in code — must be its own module's or one its module may use.
    /// Controllers and the DI registration (<c>Extensions</c>, <c>Program</c>) wire the modules
    /// together, and DTOs are plain data shapes, so neither is held to it. Each table belongs to one
    /// module too, and only that module's code touches it.
    /// </summary>
    public partial class ModuleBoundariesTests
    {
        private const string Root = "JackyAIApp.Server";

        /// <summary>Namespace (and everything under it) → module. The longest match wins.</summary>
        private static readonly Dictionary<string, string> Modules = new()
        {
            [$"{Root}.Common"] = "Platform",
            [$"{Root}.Configuration"] = "Platform",
            [$"{Root}.Data"] = "Platform",
            [$"{Root}.Services"] = "Platform", // only the services directly in it (see ExactOnly): current user, accounts, usage limits, connectors
            [$"{Root}.Services.AiUsage"] = "Platform",
            [$"{Root}.Services.OpenAI"] = "Platform",
            [$"{Root}.Services.Prompt"] = "Platform",
            [$"{Root}.Services.Speech"] = "Platform",
            [$"{Root}.Services.ExternalApi"] = "Platform", // the third-party gateway

            // Not "Services.MarketData": a namespace of that name would hide the DTO class MarketData
            // from every service under Services.
            [$"{Root}.Services.Market"] = "MarketData",
            [$"{Root}.Services.ExternalApi.Twse"] = "MarketData",

            [$"{Root}.Services.Dictionary"] = "English",
            [$"{Root}.Services.Repository"] = "English",
            [$"{Root}.Services.Exam"] = "English",
            [$"{Root}.Services.Listening"] = "English",
            [$"{Root}.Services.SpacedRepetition"] = "English",
            [$"{Root}.Services.DailyChallenge"] = "English",
            [$"{Root}.Services.WordOfTheDay"] = "English",
            [$"{Root}.Services.SpellChecker"] = "English",
            [$"{Root}.Mappers"] = "English",

            [$"{Root}.Services.Finance"] = "Finance",
            [$"{Root}.Services.Chatbot"] = "Finance",

            [$"{Root}.Services.Portfolio"] = "Portfolio",

            [$"{Root}.Services.DataAnalysis"] = "DataAnalysis",

            [$"{Root}.Services.Job"] = "Tools",
        };

        /// <summary>
        /// Entries that are not prefixes: a new <c>Services.Something</c> must be given a module,
        /// rather than fall into Platform, which anything may use.
        /// </summary>
        private static readonly HashSet<string> ExactOnly = [$"{Root}.Services"];

        /// <summary>Which modules a module's code may use, besides its own.</summary>
        private static readonly Dictionary<string, string[]> MayUse = new()
        {
            ["Platform"] = [],
            ["MarketData"] = ["Platform"],
            ["English"] = ["Platform"],
            ["Finance"] = ["MarketData", "Platform"],
            ["Portfolio"] = ["MarketData", "Platform"],
            ["DataAnalysis"] = ["Platform"],
            ["Tools"] = ["Platform"],
        };

        /// <summary>
        /// The module that owns each table (by its <c>DbSet</c> on <c>AzureSQLDBContext</c>). Another module
        /// that needs the data asks the owner's services: Finance and Portfolio read prices through
        /// MarketData, not from <c>DailyStockPrices</c>.
        /// </summary>
        private static readonly Dictionary<string, string> TableOwners = new()
        {
            ["Users"] = "Platform", ["DailyUsages"] = "Platform", ["AiUsageLogs"] = "Platform",
            ["UserConnectors"] = "Platform", ["JiraConfigs"] = "Platform",

            ["DailyStockPrices"] = "MarketData", ["StockSymbolInfos"] = "MarketData", ["TwListedCompanies"] = "MarketData",

            ["Words"] = "English", ["WordMeanings"] = "English", ["Definitions"] = "English",
            ["ExampleSentences"] = "English", ["WordMeaningTags"] = "English", ["UserWords"] = "English",
            ["ClozeTests"] = "English", ["ClozeTestOptions"] = "English", ["TranslationTests"] = "English",
            ["SentenceTests"] = "English", ["ExamAttempts"] = "English", ["ListeningTests"] = "English",
            ["ListeningTestOptions"] = "English", ["ListeningResponseTests"] = "English",
            ["ListeningResponseOptions"] = "English", ["ReadingPassages"] = "English", ["ReadingQuestions"] = "English",
            ["ReadingQuestionOptions"] = "English", ["DailyChallengeResults"] = "English", ["DailyChallengeSets"] = "English",

            ["StockPredictions"] = "Finance", ["ChatConversations"] = "Finance", ["ChatMessages"] = "Finance",
            ["ChatAttachments"] = "Finance",

            ["HoldingSnapshots"] = "Portfolio", ["HoldingPositions"] = "Portfolio", ["FxRates"] = "Portfolio",
            ["StockClassifications"] = "Portfolio",
            ["TaiexTotalReturnDailies"] = "Portfolio", // the 回落煞車 signal, read by nothing else

            ["DataSourceConfigs"] = "DataAnalysis", ["Dashboards"] = "DataAnalysis", ["DashboardWidgets"] = "DataAnalysis",
            ["DashboardShares"] = "DataAnalysis",
        };

        /// <summary>
        /// Tables every module may touch. <c>Users</c> is the signed-in user's row: modules read it, and
        /// English also writes it, because the XP and streak live on it (a known exception to ownership;
        /// moving them to a table of English's own would need a data migration).
        /// </summary>
        private static readonly HashSet<string> SharedTables = ["Users"];

        /// <summary>Not held to the rule as a source (they and what is under them); anyone may name them.
        /// The root namespace itself is too, but not what is under it.</summary>
        private static readonly string[] Unchecked = [$"{Root}.Controllers", $"{Root}.Extensions", $"{Root}.DTO"];

        [GeneratedRegex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline)]
        private static partial Regex NamespaceDeclaration();

        [GeneratedRegex(@"DbSet<\w+>\s+(\w+)\s*\{")]
        private static partial Regex DbSetDeclaration();

        /// <summary>A variable holding a context: a field, parameter or local of its type, or a context a
        /// factory made (<c>await using var db = await dbFactory.CreateDbContextAsync(…)</c>).</summary>
        [GeneratedRegex(@"\bAzureSQLDBContext\s+(\w+)|\bvar\s+(\w+)\s*=\s*(?:await\s+)?[\w.]*CreateDbContext(?:Async)?\(")]
        private static partial Regex ContextVariable();

        [GeneratedRegex(@"\bJackyAIApp\.Server(?:\.\w+)+")]
        private static partial Regex QualifiedName();

        private static string ServerSourceDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, Root, $"{Root}.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, Root);
        }

        private static IEnumerable<string> ServerSources()
        {
            var server = ServerSourceDir();
            var excluded = new[] { "bin", "obj", "Migrations" }.Select(d => $"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}").ToArray();
            return Directory.EnumerateFiles(server, "*.cs", SearchOption.AllDirectories)
                .Where(f => !excluded.Any(f.Contains));
        }

        /// <summary>The module of a declared namespace: the longest entry it is, or is under.</summary>
        internal static string? ModuleOfNamespace(string ns)
        {
            var match = Modules.Keys
                .Where(prefix => ns == prefix || (!ExactOnly.Contains(prefix) && ns.StartsWith(prefix + ".", StringComparison.Ordinal)))
                .MaxBy(prefix => prefix.Length);
            return match == null ? null : Modules[match];
        }

        /// <summary>
        /// The module of a name in code. It may run on into a type and its members
        /// (…Portfolio.PortfolioService.Foo), so the longest namespace it starts with is the one meant;
        /// or name a type directly in an exact-only namespace (…Services.ICurrentUser).
        /// </summary>
        internal static string? ModuleOfName(string name)
        {
            if (ModuleOfNamespace(name) is { } module)
                return module;
            var segments = name.Split('.');
            for (var i = segments.Length - 1; i > 0; i--)
            {
                var prefix = string.Join('.', segments[..i]);
                if (ExactOnly.Contains(prefix) && !IsNamespace($"{prefix}.{segments[i]}"))
                    return Modules[prefix];
            }
            return null;
        }

        /// <summary>Whether <paramref name="name"/> is a namespace the server declares (as opposed to a type in one).</summary>
        private static bool IsNamespace(string name) => DeclaredNamespaces.Value.Contains(name)
            || DeclaredNamespaces.Value.Any(ns => ns.StartsWith(name + ".", StringComparison.Ordinal));

        private static readonly Lazy<HashSet<string>> DeclaredNamespaces = new(() =>
            ServerSources()
                .Select(f => NamespaceDeclaration().Match(File.ReadAllText(f)))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .ToHashSet());

        private static bool IsUnchecked(string ns) =>
            ns == Root || Unchecked.Any(prefix => ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal));

        /// <summary>Code without its comments: a <c>cref</c> or a remark naming another module is no dependency.</summary>
        private static string WithoutComments(string source) =>
            string.Join('\n', source.Split('\n').Select(line =>
            {
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                return comment < 0 ? line : line[..comment];
            }));

        /// <summary>"file: its module → the module it uses (the namespace named)" for every use not allowed.</summary>
        private static List<string> Violations()
        {
            var server = ServerSourceDir();
            var violations = new List<string>();
            foreach (var file in ServerSources())
            {
                var source = File.ReadAllText(file);
                var declared = NamespaceDeclaration().Match(source);
                if (!declared.Success)
                    continue;
                var ns = declared.Groups[1].Value;
                if (IsUnchecked(ns))
                    continue;
                var module = ModuleOfNamespace(ns);
                if (module == null)
                {
                    violations.Add($"{Path.GetRelativePath(server, file)}: namespace {ns} is in no module");
                    continue;
                }

                foreach (var named in QualifiedName().Matches(WithoutComments(source)).Select(m => m.Value).Distinct())
                {
                    var target = ModuleOfName(named);
                    if (target == null || target == module || MayUse[module].Contains(target))
                        continue;
                    violations.Add($"{Path.GetRelativePath(server, file)}: {module} → {target} ({named})");
                }
            }
            return violations;
        }

        private static HashSet<string> DbSets() =>
            DbSetDeclaration().Matches(File.ReadAllText(Path.Combine(ServerSourceDir(), "Data", "AzureSQLDBContext.cs")))
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

        /// <summary>"file: its module → the table (its owner)" for every table it touches that it may not.</summary>
        private static List<string> TableViolations()
        {
            var server = ServerSourceDir();
            var tables = DbSets();
            var violations = new List<string>();
            foreach (var file in ServerSources())
            {
                var source = WithoutComments(File.ReadAllText(file));
                var declared = NamespaceDeclaration().Match(source);
                if (!declared.Success || IsUnchecked(declared.Groups[1].Value))
                    continue;
                var module = ModuleOfNamespace(declared.Groups[1].Value);
                var contexts = ContextVariable().Matches(source)
                    .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                    .ToHashSet();
                if (module == null || contexts.Count == 0)
                    continue;

                var access = new Regex($@"\b(?:{string.Join('|', contexts.Select(Regex.Escape))})\s*\.\s*(\w+)\b");
                foreach (var table in access.Matches(source).Select(m => m.Groups[1].Value).Where(tables.Contains).Distinct())
                {
                    var owner = TableOwners.GetValueOrDefault(table);
                    if (owner == module || SharedTables.Contains(table))
                        continue;
                    violations.Add($"{Path.GetRelativePath(server, file)}: {module} → {table} ({owner ?? "no owner"})");
                }
            }
            return violations;
        }

        [Fact]
        public void EachModule_TouchesOnlyItsOwnTables()
        {
            var violations = TableViolations();
            Assert.True(violations.Count == 0, "Tables touched by a module that does not own them:\n" + string.Join("\n", violations.Order()));
        }

        [Fact]
        public void EveryTable_HasAnOwner()
        {
            var tables = DbSets();
            Assert.True(tables.Count > 30);
            Assert.All(tables, t => Assert.True(TableOwners.ContainsKey(t), $"{t} has no owning module in TableOwners"));
            Assert.All(TableOwners.Keys, t => Assert.True(tables.Contains(t), $"{t} is in TableOwners but is no DbSet"));
            Assert.All(TableOwners.Values.Distinct(), m => Assert.Contains(m, MayUse.Keys));
        }

        [Fact]
        public void EachModule_UsesOnlyTheModulesItMay()
        {
            var violations = Violations();
            Assert.True(violations.Count == 0, "Uses across module boundaries:\n" + string.Join("\n", violations.Order()));
        }

        [Fact]
        public void TheMayUseGraph_HasNoCycle()
        {
            // A cycle would let two modules lean on each other again, the knot MarketData was cut out to undo.
            var visiting = new HashSet<string>();
            var done = new HashSet<string>();
            bool Cyclic(string module)
            {
                if (done.Contains(module)) return false;
                if (!visiting.Add(module)) return true;
                var cyclic = MayUse[module].Any(Cyclic);
                visiting.Remove(module);
                done.Add(module);
                return cyclic;
            }

            Assert.DoesNotContain(MayUse.Keys, Cyclic);
            Assert.All(MayUse.Values.SelectMany(v => v), m => Assert.Contains(m, MayUse.Keys));
            Assert.All(Modules.Values.Distinct(), m => Assert.Contains(m, MayUse.Keys));
            Assert.All(Modules.Keys, ns => Assert.True(IsNamespace(ns), $"{ns} is in the module map but no source declares it"));
        }

        [Theory]
        [InlineData("JackyAIApp.Server.Services.Market", "MarketData")]
        [InlineData("JackyAIApp.Server.Services.Market.US", "MarketData")]
        [InlineData("JackyAIApp.Server.Services.ExternalApi.Twse.TwseOpenApi", "MarketData")]
        [InlineData("JackyAIApp.Server.Services.ExternalApi.ThirdPartyGateway", "Platform")]
        [InlineData("JackyAIApp.Server.Services.Portfolio.DrawdownBrake", "Portfolio")]
        [InlineData("JackyAIApp.Server.Services.ICurrentUser", "Platform")]
        public void AName_BelongsToTheModuleOfItsLongestPrefix(string name, string module) =>
            Assert.Equal(module, ModuleOfName(name));

        [Fact]
        public void ANewServicesNamespace_IsInNoModule_UntilGivenOne() =>
            Assert.Null(ModuleOfNamespace("JackyAIApp.Server.Services.SomethingNew"));

        [Fact]
        public void TheCheck_ReadsTheServersSources()
        {
            // Guards the test above against passing because it found nothing to look at.
            Assert.Contains(ServerSources(), f => f.EndsWith("TwseOpenApi.cs", StringComparison.Ordinal));
            Assert.True(ServerSources().Count() > 200);
        }
    }
}
