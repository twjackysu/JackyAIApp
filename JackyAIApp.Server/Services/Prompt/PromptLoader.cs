using System.Collections.Concurrent;
using System.Reflection;

namespace JackyAIApp.Server.Services.Prompt
{
    /// <summary>
    /// Loads prompt templates from embedded resources with caching
    /// </summary>
    public class PromptLoader : IPromptLoader
    {
        private readonly ILogger<PromptLoader> _logger;
        private readonly Assembly _assembly;
        private readonly string _assemblyName;
        private readonly ConcurrentDictionary<string, string> _cache = new();

        public PromptLoader(ILogger<PromptLoader> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _assembly = Assembly.GetExecutingAssembly();
            _assemblyName = _assembly.GetName().Name ?? "JackyAIApp.Server";
        }

        public string? GetPrompt(string promptPath)
        {
            if (string.IsNullOrWhiteSpace(promptPath))
            {
                return null;
            }

            // Check cache first
            if (_cache.TryGetValue(promptPath, out var cached))
            {
                return cached;
            }

            // Convert path to embedded resource name
            // "Prompt/Exam/ClozeSystem.txt" -> "JackyAIApp.Server.Prompt.Exam.ClozeSystem.txt"
            var resourceName = ConvertPathToResourceName(promptPath);

            try
            {
                using var stream = _assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _logger.LogWarning("Embedded resource not found: {ResourceName} (from path: {Path})", resourceName, promptPath);
                    return null;
                }

                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();

                // Cache the result
                _cache.TryAdd(promptPath, content);

                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading embedded resource: {ResourceName}", resourceName);
                return null;
            }
        }

        public async Task<string?> GetPromptAsync(string promptPath)
        {
            if (string.IsNullOrWhiteSpace(promptPath))
            {
                return null;
            }

            // Check cache first
            if (_cache.TryGetValue(promptPath, out var cached))
            {
                return cached;
            }

            var resourceName = ConvertPathToResourceName(promptPath);

            try
            {
                using var stream = _assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _logger.LogWarning("Embedded resource not found: {ResourceName} (from path: {Path})", resourceName, promptPath);
                    return null;
                }

                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();

                // Cache the result
                _cache.TryAdd(promptPath, content);

                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading embedded resource: {ResourceName}", resourceName);
                return null;
            }
        }

        public bool PromptExists(string promptPath)
        {
            if (_cache.ContainsKey(promptPath))
            {
                return true;
            }

            var resourceName = ConvertPathToResourceName(promptPath);
            var resourceNames = _assembly.GetManifestResourceNames();
            return resourceNames.Contains(resourceName);
        }

        /// <summary>
        /// Convert file path to embedded resource name
        /// </summary>
        private string ConvertPathToResourceName(string path)
        {
            // Replace slashes with dots and prepend assembly name
            var normalized = path.Replace('/', '.').Replace('\\', '.');
            return $"{_assemblyName}.{normalized}";
        }
    }
}
