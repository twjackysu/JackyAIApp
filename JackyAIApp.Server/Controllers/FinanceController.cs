using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class FinanceController(
        ILogger<FinanceController> logger,
        IOptionsMonitor<Settings> settings,
        IMyResponseFactory responseFactory,
        AzureCosmosDBContext DBContext,
        IUserService userService,
        IOpenAIService openAIService,
        IHttpClientFactory httpClientFactory,
        IExtendedMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<FinanceController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        private readonly AzureCosmosDBContext _DBContext = DBContext;
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
                // Fetch raw material information from Taiwan Stock Exchange API
                string rawMaterialInfo = await GetRawMaterialInfoAsync();
                if (string.IsNullOrEmpty(rawMaterialInfo))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to fetch data from Taiwan Stock Exchange API.");
                }
                var date = rawMaterialInfo.Substring(11, 7);
                var fileNmae = $"{date}.json";
                var cacheKey = $"{nameof(GetDailyImportantInfo)}_{fileNmae}";
                if (!_memoryCache.TryGetValue(cacheKey, out List<StrategicInsight>? result))
                {
                    var listFilesResponse = await _openAIService.Files.ListFile();
                    if (!listFilesResponse.Successful)
                    {
                        return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to fetch list files from OpenAI files API.");
                    }
                    string? fileId = null;
                    for (var i = 0; i < listFilesResponse?.Data?.Count; i++)
                    {
                        var file = listFilesResponse.Data[i];
                        if (file != null)
                        {
                            if (file.FileName != fileNmae)
                            {
                                await _openAIService.Files.DeleteFile(file.Id);
                            }
                            else
                            {
                                fileId = file.Id;
                            }
                        }
                    }
                    if (fileId == null)
                    {
                        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawMaterialInfo));
                        var fileUploadResult = await _openAIService.Files.FileUpload("assistants", stream, fileNmae);
                        if (!fileUploadResult.Successful)
                        {
                            return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to upload data from OpenAI files API.");
                        }
                        fileId = fileUploadResult.Id;
                    }

                    var vectorStoreId = "vs_681efca3b2388191a761e64f1f7250ac";
                    // vectorStore只需要create一次

                    var listVectorStoreFilesResponse = await _openAIService.Beta.VectorStoreFiles.ListVectorStoreFiles(vectorStoreId, new VectorStoreFileListRequest()
                    {

                    });
                    if (!listVectorStoreFilesResponse.Successful)
                    {
                        return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to fetch list vector store files from OpenAI files API.");
                    }
                    // 清空VectorStoreFiles
                    bool needCreateVSFiles = true;
                    for (var i = 0; i < listVectorStoreFilesResponse?.Data?.Count; i++)
                    {
                        var vsFiles = listVectorStoreFilesResponse?.Data[i];
                        if (vsFiles != null)
                        {
                            if (vsFiles.Id == fileId)
                            {
                                // 如果已經有該fileId則不create VSFiles
                                needCreateVSFiles = false;
                            }
                            else
                            {
                                await _openAIService.Beta.VectorStoreFiles.DeleteVectorStoreFile(vectorStoreId, vsFiles.Id);
                            }
                        }
                    }
                    if (needCreateVSFiles)
                    {
                        var createVSFilesResponse = await _openAIService.Beta.VectorStoreFiles.CreateVectorStoreFile(vectorStoreId, new CreateVectorStoreFileRequest()
                        {
                            FileId = fileId,
                        });
                        if (!createVSFilesResponse.Successful)
                        {
                            return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to create vector store files from OpenAI files API.");
                        }

                        while (true)
                        {
                            await Task.Delay(2000);
                            var getVSResponse = await _openAIService.Beta.VectorStores.RetrieveVectorStore(vectorStoreId);

                            if (!getVSResponse.Successful || getVSResponse.FileCounts.Failed > 0)
                            {
                                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to fetch get vector store from OpenAI files API.");
                            }
                            if (getVSResponse.FileCounts.Completed > 0)
                            {
                                break;
                            }
                        }
                    }

                    string assistantID = "asst_5vCsMPtNXvVfsbptyZakpr2m";
                    // 因為assistant內是設vectorStore，所以只需要create一次

                    //string systemChatMessage = System.IO.File.ReadAllText("Prompt/Finance/DailyImportantInfoSystem.txt");
                    //var assistantList = await _openAIService.Beta.Assistants.AssistantList();
                    //string botName = "Finance Insight Assistants";
                    //var hasBot = assistantList?.Data?.FirstOrDefault(x => x.Name == botName);
                    //string? assistantID = hasBot?.Id;
                    //if (string.IsNullOrEmpty(assistantID))
                    //{
                    //    var createAssistantResponse = await _openAIService.Beta.Assistants.AssistantCreate(new AssistantCreateRequest()
                    //    {
                    //        Name = botName,
                    //        Instructions = systemChatMessage,
                    //        Tools = new List<ToolDefinition>()
                    //        {
                    //            new ToolDefinition() {
                    //                Type = "file_search"
                    //            }
                    //        },
                    //        Model = Models.Gpt_4o,
                    //        ToolResources = new ToolResources()
                    //        {
                    //            FileSearch = new FileSearch()
                    //            {
                    //                VectorStoreIds = new List<string>()
                    //                {
                    //                    vectorStoreId,
                    //                }
                    //            }
                    //        }
                    //    });
                    //    if (!createAssistantResponse.Successful)
                    //    {
                    //        return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to create assistant from OpenAI API.");
                    //    }
                    //    assistantID = createAssistantResponse.Id;
                    //}
                    //else
                    //{
                    //    var editAssistantResponse = await _openAIService.Beta.Assistants.AssistantModify(assistantID, new AssistantModifyRequest()
                    //    {
                    //        ToolResources = new ToolResources()
                    //        {
                    //            FileSearch = new FileSearch()
                    //            {
                    //                VectorStoreIds = new List<string>() { vectorStoreId }
                    //            }
                    //        }
                    //    });
                    //    if (!editAssistantResponse.Successful)
                    //    {
                    //        return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to modify assistant from OpenAI API.");
                    //    }
                    //}
                    var createThreadAndRunResponse = await _openAIService.Beta.Runs.CreateThreadAndRun(new CreateThreadAndRunRequest()
                    {
                        AssistantId = assistantID,
                        Thread = new ThreadCreateRequest()
                        {
                            Messages = new List<MessageCreateRequest>()
                        {
                            new MessageCreateRequest()
                            {
                                Role = "user",
                                Content = new MessageContentOneOfType(
                                    new List<MessageContent>() {
                                    MessageContent.TextContent("From today's major news about listed companies, select five to ten companies with the greatest growth potential and one to five companies that may decline, and explain the reasons.")
                                }),
                                Attachments = new List<Attachment>()
                                {
                                    new Attachment()
                                    {
                                        FileId = fileId,
                                        Tools = new List<ToolDefinition>()
                                        {
                                            new ToolDefinition()
                                            {
                                                Type = "file_search"
                                            }
                                        }
                                    }
                                },
                            }
                        }
                        }
                    });

                    //var createThreadResponse = await _openAIService.Beta.Threads.ThreadCreate();
                    //if (!createThreadResponse.Successful)
                    //{
                    //    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to create threads from OpenAI API.");
                    //}
                    //string threadId = createThreadResponse.Id;
                    //var createMessageResponse = await _openAIService.Beta.Messages.CreateMessage(threadId, new MessageCreateRequest()
                    //{
                    //    Role = "user",
                    //    Attachments = new List<Attachment>()
                    //    {
                    //        new Attachment()
                    //        {
                    //            FileId = fileId,
                    //            Tools = new List<ToolDefinition>()
                    //            {
                    //                new ToolDefinition()
                    //                {
                    //                    Type = "file_search"
                    //                }
                    //            }
                    //        }
                    //    },
                    //    Content = new MessageContentOneOfType(
                    //        new List<MessageContent>() {
                    //        MessageContent.TextContent("From today's major news about listed companies, pick out five companies with the greatest potential for growth and explain why.")
                    //    }),
                    //});
                    //if (!createMessageResponse.Successful)
                    //{
                    //    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to create messages from OpenAI API.");
                    //}
                    if (!createThreadAndRunResponse.Successful)
                    {
                        return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to create messages and threads and run from OpenAI API.");
                    }
                    string threadId = createThreadAndRunResponse.ThreadId;
                    //var runResponse = await _openAIService.Beta.Runs.RunCreate(threadId, new RunCreateRequest()
                    //{
                    //    AssistantId = assistantID,

                    //});
                    //if (!runResponse.Successful)
                    //{
                    //    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to create runs from OpenAI API.");
                    //}
                    string runId = createThreadAndRunResponse.Id;
                    while (true)
                    {
                        await Task.Delay(3000);
                        var runStatusResponse = await _openAIService.Beta.Runs.RunRetrieve(threadId, runId);
                        if (runStatusResponse.Status == "completed")
                            break;
                        if (runStatusResponse.Status == "failed")
                            break;
                    }
                    var getMessageListResponse = await _openAIService.Beta.Messages.ListMessages(threadId);

                    var errorMessage = "Query failed, OpenAI could not analyze the financial information.";

                    if (getMessageListResponse.Successful)
                    {
                        _logger.LogInformation("Generate financial analysis result: {json}",
                            JsonConvert.SerializeObject(getMessageListResponse.Data, Formatting.Indented));

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