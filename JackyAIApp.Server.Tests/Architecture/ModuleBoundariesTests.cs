using System.Text.RegularExpressions;
using JackyAIApp.Server.Services.Market;

namespace JackyAIApp.Server.Tests.Architecture
{
    /// <summary>
    /// The server's modules and which of them each may use (JackyAIApp.Server/CLAUDE.md, "Modules").
    /// A source file belongs to the module of its namespace; every namespace it names — a <c>using</c>,
    /// or a fully qualified name in code — must be its own module's or one its module may use.
    /// Controllers and the DI registration (<c>Extensions</c>, <c>Program</c>) wire the modules
    /// together, and DTOs are plain data shapes, so neither is held to it.
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

        /// <summary>Not held to the rule as a source (they and what is under them); anyone may name them.
        /// The root namespace itself is too, but not what is under it.</summary>
        private static readonly string[] Unchecked = [$"{Root}.Controllers", $"{Root}.Extensions", $"{Root}.DTO"];

        [GeneratedRegex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline)]
        private static partial Regex NamespaceDeclaration();

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
