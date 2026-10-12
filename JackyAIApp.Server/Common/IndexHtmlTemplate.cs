namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// The SPA's built wwwroot/index.html, read once and kept for the process's lifetime (a deploy
    /// restarts the process, so it never outlives the build it came from). Null when there is no
    /// built index.html, as in local development where Vite serves the page.
    /// </summary>
    internal sealed class IndexHtmlTemplate(IWebHostEnvironment env)
    {
        private readonly object _lock = new();
        private string? _cached;

        public string? Get()
        {
            if (_cached != null) return _cached;

            lock (_lock)
            {
                if (_cached != null) return _cached;

                var fileInfo = env.WebRootFileProvider.GetFileInfo("index.html");
                if (!fileInfo.Exists) return null;

                using var stream = fileInfo.CreateReadStream();
                using var reader = new StreamReader(stream);
                _cached = reader.ReadToEnd();
            }

            return _cached;
        }
    }
}
