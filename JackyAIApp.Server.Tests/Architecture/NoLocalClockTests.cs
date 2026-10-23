using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Tests.Architecture
{
    /// <summary>
    /// The server never reads the machine's local clock (JackyAIApp.Server/CLAUDE.md, "Taipei time"):
    /// <c>DateTime.Now</c> and <c>DateTime.Today</c> are whatever zone the host runs in — UTC on App
    /// Service, Taipei on a developer's machine — so a day built on them turns at a different hour in
    /// each. The day of the market is <c>GetTaipeiToday()</c>, the usage budget's day and stored times
    /// are UTC (<c>GetUtcNow()</c>), both read through the injected <see cref="TimeProvider"/>.
    /// </summary>
    public partial class NoLocalClockTests
    {
        [GeneratedRegex(@"\bDateTime(Offset)?\.(Now|Today)\b")]
        private static partial Regex LocalClock();

        private static string ServerSourceDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JackyAIApp.Server", "JackyAIApp.Server.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "JackyAIApp.Server");
        }

        private static IEnumerable<string> ServerSources()
        {
            var server = ServerSourceDir();
            var sep = Path.DirectorySeparatorChar;
            return Directory.EnumerateFiles(server, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}Migrations{sep}"));
        }

        [Fact]
        public void NoServerSource_ReadsTheLocalClock()
        {
            var server = ServerSourceDir();
            var offenders = ServerSources()
                .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line.Trim())))
                // A comment may name them, as this rule's own explanations do.
                .Where(l => !l.Text.StartsWith("//") && !l.Text.StartsWith('*') && LocalClock().IsMatch(l.Text))
                .Select(l => $"{Path.GetRelativePath(server, l.File)}:{l.Line}: {l.Text}")
                .ToList();

            Assert.Empty(offenders);
        }

        [Theory]
        [InlineData("var today = DateTime.Today;", true)]
        [InlineData("var key = $\"Day_{DateTime.Now:yyyyMMdd}\";", true)]
        [InlineData("var at = DateTimeOffset.Now;", true)]
        [InlineData("var at = DateTime.UtcNow;", false)]
        [InlineData("private DateTime Today => time.GetUtcNow().UtcDateTime.Date;", false)]
        public void TheCheck_CatchesTheLocalClock_AndNothingElse(string line, bool caught)
        {
            Assert.Equal(caught, LocalClock().IsMatch(line));
        }

        [Fact]
        public void TheCheck_ReadsTheServersSources()
        {
            // Guards the test above against passing because it found no sources to look at.
            Assert.Contains(ServerSources(), f => f.EndsWith("TaipeiTime.cs"));
        }
    }
}
