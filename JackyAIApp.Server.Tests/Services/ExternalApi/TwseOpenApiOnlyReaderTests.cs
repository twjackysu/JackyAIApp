namespace JackyAIApp.Server.Tests.Services.ExternalApi
{
    /// <summary>
    /// TWSE's OpenAPI is read in one place, <c>Services/ExternalApi/Twse</c> (JackyAIApp.Server/CLAUDE.md):
    /// no other server source names openapi.twse.com.tw. A new dataset goes into <c>ITwseOpenApi</c>,
    /// so it gets the same client, cache and parsing as the rest.
    /// </summary>
    public class TwseOpenApiOnlyReaderTests
    {
        private static string ServerSourceDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JackyAIApp.Server", "JackyAIApp.Server.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "JackyAIApp.Server");
        }

        [Fact]
        public void OnlyTheClient_NamesTwsesOpenApiHost()
        {
            var server = ServerSourceDir();
            var client = Path.Combine(server, "Services", "ExternalApi", "Twse") + Path.DirectorySeparatorChar;

            var offenders = Directory.EnumerateFiles(server, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.StartsWith(client, StringComparison.OrdinalIgnoreCase))
                .Where(f => File.ReadAllText(f).Contains("openapi.twse.com.tw", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetRelativePath(server, f))
                .ToList();

            Assert.Empty(offenders);
        }

        [Fact]
        public void TheCheck_ReadsTheServersSources()
        {
            // Guards the test above against passing because it found no sources to look at.
            Assert.True(File.Exists(Path.Combine(ServerSourceDir(), "Services", "ExternalApi", "Twse", "TwseOpenApi.cs")));
        }
    }
}
