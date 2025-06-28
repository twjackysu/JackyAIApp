using System.Collections.Concurrent;
using System.Text;
using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Service for TWSE data handling including raw data fetching and file management.
    /// </summary>
    public class TWSEDataService : ITWSEDataService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TWSEDataService> _logger;
        private readonly IOpenAIService _openAIService;
        private readonly IExtendedMemoryCache _memoryCache;
        private const string VECTOR_STORE_ID = "vs_681efca3b2388191a761e64f1f7250ac";
        private const int MAX_RETRY_ATTEMPTS = 3;
        
        // Static cache for file IDs to avoid repeated uploads across requests
        private static readonly ConcurrentDictionary<string, string> _fileIdCache = new();

        public TWSEDataService(
            IHttpClientFactory httpClientFactory,
            ILogger<TWSEDataService> logger,
            IOpenAIService openAIService,
            IExtendedMemoryCache memoryCache)
        {
            _httpClient = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        /// <summary>
        /// Fetches raw material information from Taiwan Stock Exchange API with caching.
        /// </summary>
        public async Task<string> GetRawMaterialInfoWithCacheAsync(CancellationToken cancellationToken = default)
        {
            var today = DateTime.Today.ToString("yyyyMMdd");
            var apiCacheKey = $"twse_raw_data_{today}";

            // Check cache first

            if (_memoryCache.TryGetValue(apiCacheKey, out string? cachedData) && !string.IsNullOrEmpty(cachedData))
            {
                return cachedData;
            }

            try
            {
                string apiUrl = "https://openapi.twse.com.tw/v1/opendata/t187ap04_L";

                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    // Cache for 1 hour to avoid frequent API calls
                    _memoryCache.Set(apiCacheKey, jsonContent, TimeSpan.FromHours(1));
                    
                    return jsonContent;
                }
                else
                {
                    _logger.LogError("Failed to fetch data from Taiwan Stock Exchange API. Status code: {StatusCode}", response.StatusCode);
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching data from Taiwan Stock Exchange API.");
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets or uploads file to OpenAI with retry mechanism and caching.
        /// </summary>
        public async Task<string> GetOrUploadFileWithRetryAsync(string content, string fileName, CancellationToken cancellationToken = default)
        {
            // Check static cache first
            if (_fileIdCache.TryGetValue(fileName, out string? cachedFileId))
            {
                // We'll verify file existence during the list operation below
                // For now, trust the cache but verify during the main flow
                return cachedFileId;
            }

            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    // List and clean old files
                    var listFilesResponse = await _openAIService.Files.ListFile();
                    if (!listFilesResponse.Successful)
                    {
                        if (attempt == MAX_RETRY_ATTEMPTS) return string.Empty;
                        await Task.Delay(1000 * attempt, cancellationToken);
                        continue;
                    }

                    string? fileId = null;
                    var filesToDelete = new List<string>();

                    // Batch identify files to delete and find existing file
                    foreach (var file in listFilesResponse?.Data ?? [])
                    {
                        if (file.FileName == fileName)
                        {
                            fileId = file.Id;
                            // Update cache with verified file ID
                            _fileIdCache.TryAdd(fileName, fileId);
                        }
                        else if (file.FileName.EndsWith(".json"))
                        {
                            filesToDelete.Add(file.Id);
                        }
                    }

                    // If we had a cached file ID but it wasn't found in the list, remove it from cache
                    if (_fileIdCache.TryGetValue(fileName, out string? oldCachedId) && fileId != oldCachedId)
                    {
                        _fileIdCache.TryRemove(fileName, out _);
                        fileId = null;
                    }

                    // Batch delete old files (fire and forget for performance)
                    _ = Task.Run(async () =>
                    {
                        foreach (var fileIdToDelete in filesToDelete)
                        {
                            try
                            {
                                await _openAIService.Files.DeleteFile(fileIdToDelete);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to delete old file {fileId}", fileIdToDelete);
                            }
                        }
                    }, cancellationToken);

                    // Upload file if not exists
                    if (fileId == null)
                    {
                        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                        var fileUploadResult = await _openAIService.Files.FileUpload("assistants", stream, fileName);
                        if (fileUploadResult.Successful)
                        {
                            fileId = fileUploadResult.Id;
                        }
                        else if (attempt == MAX_RETRY_ATTEMPTS)
                        {
                            return string.Empty;
                        }
                    }

                    if (!string.IsNullOrEmpty(fileId))
                    {
                        _fileIdCache.TryAdd(fileName, fileId);
                        return fileId;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Attempt {attempt} failed for file upload", attempt);
                    if (attempt == MAX_RETRY_ATTEMPTS) return string.Empty;
                    await Task.Delay(1000 * attempt, cancellationToken);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Ensures vector store is ready with optimized polling.
        /// </summary>
        public async Task<bool> EnsureVectorStoreReadyAsync(string fileId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if file already exists in vector store
                var listVectorStoreFilesResponse = await _openAIService.Beta.VectorStoreFiles.ListVectorStoreFiles(VECTOR_STORE_ID, new VectorStoreFileListRequest());
                if (!listVectorStoreFilesResponse.Successful)
                {
                    return false;
                }

                bool fileExistsInVectorStore = false;
                var filesToDelete = new List<string>();

                foreach (var vsFile in listVectorStoreFilesResponse?.Data ?? [])
                {
                    if (vsFile.Id == fileId)
                    {
                        fileExistsInVectorStore = true;
                    }
                    else
                    {
                        filesToDelete.Add(vsFile.Id);
                    }
                }

                // Clean old vector store files (fire and forget)
                _ = Task.Run(async () =>
                {
                    foreach (var fileIdToDelete in filesToDelete)
                    {
                        try
                        {
                            await _openAIService.Beta.VectorStoreFiles.DeleteVectorStoreFile(VECTOR_STORE_ID, fileIdToDelete);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete vector store file {fileId}", fileIdToDelete);
                        }
                    }
                }, cancellationToken);

                if (fileExistsInVectorStore)
                {
                    return true;
                }

                // Create vector store file
                var createVSFilesResponse = await _openAIService.Beta.VectorStoreFiles.CreateVectorStoreFile(VECTOR_STORE_ID, new CreateVectorStoreFileRequest { FileId = fileId });
                if (!createVSFilesResponse.Successful)
                {
                    return false;
                }

                // Wait for processing with exponential backoff and timeout
                var maxWaitTime = TimeSpan.FromMinutes(3);
                var startTime = DateTime.UtcNow;
                var delay = TimeSpan.FromSeconds(2);
                var maxDelay = TimeSpan.FromSeconds(10);

                while (DateTime.UtcNow - startTime < maxWaitTime)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    await Task.Delay(delay, cancellationToken);
                    
                    var getVSResponse = await _openAIService.Beta.VectorStores.RetrieveVectorStore(VECTOR_STORE_ID);
                    
                    if (!getVSResponse.Successful)
                    {
                        return false;
                    }
                    
                    if (getVSResponse.FileCounts.Failed > 0)
                    {
                        _logger.LogError("Vector store processing failed for file {fileId}", fileId);
                        return false;
                    }
                    
                    if (getVSResponse.FileCounts.Completed > 0)
                    {
                        return true;
                    }
                    
                    // Exponential backoff
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, maxDelay.TotalMilliseconds));
                }

                _logger.LogWarning("Vector store processing timed out for file {fileId}", fileId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring vector store readiness");
                return false;
            }
        }
    }
}