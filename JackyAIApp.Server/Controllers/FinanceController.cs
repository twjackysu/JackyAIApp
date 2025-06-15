using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FinanceController(
        ILogger<FinanceController> logger,
        IOptionsMonitor<Settings> settings,
        IMyResponseFactory responseFactory,
        AzureSQLDBContext DBContext,
        IUserService userService,
        IOpenAIService openAIService,
        IHttpClientFactory httpClientFactory,
        IExtendedMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<FinanceController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        private readonly AzureSQLDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IOpenAIService _openAIService = openAIService;
        private readonly HttpClient _httpClient = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;

        /// <summary>
        /// Gets the daily important financial information from Taiwan Stock Exchange.
        /// </summary>
        /// <returns>An IActionResult containing the strategic insights or an error response.</returns>
        [HttpGet("dailyimportantinfo")]
        public async Task<IActionResult> GetDailyImportantInfo()
        {
            try
            {
                // Step 1: Fetch raw material information from Taiwan Stock Exchange API
                string rawMaterialInfo = await GetRawMaterialInfoAsync();
                if (string.IsNullOrEmpty(rawMaterialInfo))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to fetch data from Taiwan Stock Exchange API.");
                }

                var date = rawMaterialInfo.Substring(11, 7);
                var fileName = $"{date}.json";
                var cacheKey = $"{nameof(GetDailyImportantInfo)}_{fileName}";

                // Step 2: Check if the result is cached
                if (!_memoryCache.TryGetValue(cacheKey, out List<StrategicInsight>? result))
                {
                    // Step 3: List existing files in OpenAI
                    var listFilesResponse = await _openAIService.Files.ListFile();
                    if (!listFilesResponse.Successful)
                    {
                        return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to fetch list files from OpenAI files API.");
                    }

                    string? fileId = null;

                    // Step 4: Manage files in OpenAI (delete old files, keep or upload the current file)
                    foreach (var file in listFilesResponse?.Data ?? [])
                    {
                        if (file.FileName != fileName)
                        {
                            await _openAIService.Files.DeleteFile(file.Id);
                        }
                        else
                        {
                            fileId = file.Id;
                        }
                    }

                    if (fileId == null)
                    {
                        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawMaterialInfo));
                        var fileUploadResult = await _openAIService.Files.FileUpload("assistants", stream, fileName);
                        if (!fileUploadResult.Successful)
                        {
                            return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to upload data to OpenAI files API.");
                        }
                        fileId = fileUploadResult.Id;
                    }

                    var vectorStoreId = "vs_681efca3b2388191a761e64f1f7250ac";
                    // vectorStore has been created through the OpenAI webUI playground, so there is no need to create it again

                    // Step 5: List and clean vector store files
                    var listVectorStoreFilesResponse = await _openAIService.Beta.VectorStoreFiles.ListVectorStoreFiles(vectorStoreId, new VectorStoreFileListRequest());
                    if (!listVectorStoreFilesResponse.Successful)
                    {
                        return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to fetch vector store files from OpenAI files API.");
                    }

                    bool needCreateVSFiles = true;
                    foreach (var vsFile in listVectorStoreFilesResponse?.Data ?? [])
                    {
                        if (vsFile.Id == fileId)
                        {
                            needCreateVSFiles = false;
                        }
                        else
                        {
                            await _openAIService.Beta.VectorStoreFiles.DeleteVectorStoreFile(vectorStoreId, vsFile.Id);
                        }
                    }

                    // Step 6: Create vector store files if needed
                    if (needCreateVSFiles)
                    {
                        var createVSFilesResponse = await _openAIService.Beta.VectorStoreFiles.CreateVectorStoreFile(vectorStoreId, new CreateVectorStoreFileRequest { FileId = fileId });
                        if (!createVSFilesResponse.Successful)
                        {
                            return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to create vector store files in OpenAI files API.");
                        }

                        // Wait for vector store processing to complete
                        while (true)
                        {
                            await Task.Delay(2000);
                            var getVSResponse = await _openAIService.Beta.VectorStores.RetrieveVectorStore(vectorStoreId);

                            if (!getVSResponse.Successful || getVSResponse.FileCounts.Failed > 0)
                            {
                                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to retrieve vector store from OpenAI files API.");
                            }
                            if (getVSResponse.FileCounts.Completed > 0)
                            {
                                break;
                            }
                        }
                    }

                    // Step 7: Create thread and run analysis
                    string assistantID = "asst_5vCsMPtNXvVfsbptyZakpr2m";
                    // The assistant has already been created through the OpenAI webUI playground, so there is no need to create it again
                    var createThreadAndRunResponse = await _openAIService.Beta.Runs.CreateThreadAndRun(new CreateThreadAndRunRequest
                    {
                        AssistantId = assistantID,
                        Thread = new ThreadCreateRequest
                        {
                            Messages = new List<MessageCreateRequest>
                            {
                                new MessageCreateRequest
                                {
                                    Role = "user",
                                    Content = new MessageContentOneOfType(new List<MessageContent>
                                    {
                                        MessageContent.TextContent("From today's major news about listed companies, select five to ten companies with the greatest growth potential and one to five companies that may decline, and explain the reasons. So you need to list at least 6 companies (5 growing and 1 declining) and at most 15 companies (10 growing and 5 declining).")
                                    }),
                                    Attachments = new List<Attachment>
                                    {
                                        new Attachment
                                        {
                                            FileId = fileId,
                                            Tools = new List<ToolDefinition>
                                            {
                                                new ToolDefinition { Type = "file_search" }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    });

                    if (!createThreadAndRunResponse.Successful)
                    {
                        return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to create thread and run analysis in OpenAI API.");
                    }

                    string threadId = createThreadAndRunResponse.ThreadId;
                    string runId = createThreadAndRunResponse.Id;

                    // Step 8: Monitor run status
                    while (true)
                    {
                        await Task.Delay(3000);
                        var runStatusResponse = await _openAIService.Beta.Runs.RunRetrieve(threadId, runId);
                        if (runStatusResponse.Status == "completed")
                            break;
                        if (runStatusResponse.Status == "failed")
                            break;
                    }

                    // Step 9: Retrieve and process messages
                    var getMessageListResponse = await _openAIService.Beta.Messages.ListMessages(threadId);
                    var errorMessage = "Query failed, OpenAI could not analyze the financial information.";

                    if (getMessageListResponse.Successful)
                    {
                        _logger.LogInformation("Generate financial analysis result: {json}", JsonConvert.SerializeObject(getMessageListResponse.Data, Formatting.Indented));

                        var content = getMessageListResponse.Data?.FirstOrDefault()?.Content?.FirstOrDefault()?.Text?.Value ?? "";
                        content = content.Replace("```json", "").Replace("```", "");

                        if (string.IsNullOrEmpty(content))
                        {
                            return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                        }

                        List<StrategicInsight>? strategicInsights = null;

                        try
                        {
                            strategicInsights = JsonConvert.DeserializeObject<List<StrategicInsight>>(content);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                            return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                        }

                        if (strategicInsights == null)
                        {
                            return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                        }

                        _memoryCache.Set(cacheKey, strategicInsights, TimeSpan.FromDays(0.5));
                        return _responseFactory.CreateOKResponse(strategicInsights);
                    }

                    return _responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
                }

                // Step 10: Return cached result
                return _responseFactory.CreateOKResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while getting daily important information.");
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Internal server error occurred.");
            }
        }

        /// <summary>
        /// Fetches raw material information from Taiwan Stock Exchange API.
        /// </summary>
        /// <returns>The API response as a string.</returns>
        private async Task<string> GetRawMaterialInfoAsync()
        {
            try
            {
                string apiUrl = "https://openapi.twse.com.tw/v1/opendata/t187ap04_L";

                HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    // Read the content as string
                    string jsonContent = await response.Content.ReadAsStringAsync();

                    // Convert JSON to CSV
                    // return ConvertJsonToCsv(jsonContent);
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
        /// Converts JSON array to CSV format.
        /// </summary>
        /// <param name="jsonContent">The JSON string to convert.</param>
        /// <returns>CSV formatted string.</returns>
        private string ConvertJsonToCsv(string jsonContent)
        {
            try
            {
                // Parse the JSON array
                var jsonArray = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(jsonContent);

                if (jsonArray == null || jsonArray.Count == 0)
                {
                    return string.Empty;
                }

                // Use StringBuilder for efficient string concatenation
                var csv = new StringBuilder();

                // Add headers
                var headers = jsonArray[0].Keys.ToArray();
                csv.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));

                // Add rows
                foreach (var item in jsonArray)
                {
                    var row = headers.Select(header =>
                    {
                        // Get the value for this header, or empty string if null
                        string value = item.ContainsKey(header) ? item[header] : string.Empty;

                        // If the value contains commas, quotes, or newlines, enclose it in quotes
                        // Also escape any quotes within the value by doubling them
                        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                        {
                            value = $"\"{value.Replace("\"", "\"\"")}\"";
                        }

                        return value;
                    });

                    csv.AppendLine(string.Join(",", row));
                }

                return csv.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting JSON to CSV");
                return string.Empty;
            }
        }
    }
}