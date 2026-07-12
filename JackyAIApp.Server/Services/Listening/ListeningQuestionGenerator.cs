using Betalgo.Ranul.OpenAI.Contracts.Enums.Image;
using Betalgo.Ranul.OpenAI.Contracts.Enums.Model;
using Betalgo.Ranul.OpenAI.Contracts.Requests.Image;
using Betalgo.Ranul.OpenAI.Interfaces;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.OpenAI;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Listening
{
    public class ListeningQuestionGenerator(
        ILogger<ListeningQuestionGenerator> logger,
        AzureSQLDBContext dbContext,
        IOpenAIPromptService promptService,
        IOpenAIService openAIService,
        IListeningImageProcessor imageProcessor,
        IListeningImageStorageService imageStorageService,
        IAiUsageLogger usageLogger) : IListeningQuestionGenerator
    {
        private const string ImageModelId = "gpt-image-1-mini";
        private const string Feature = "exam.listening_admin_generate";

        private static readonly string[] Scenarios =
        [
            "office work", "restaurant dining", "outdoor park activity", "retail shopping",
            "public transportation", "construction or repair work", "sports or recreation",
            "household chores", "library or classroom", "business meeting or presentation",
        ];

        public async Task<ServiceResult> GenerateAsync(string? userId)
        {
            var scenario = Scenarios[Random.Shared.Next(Scenarios.Length)];

            var (dto, error) = await promptService.GetCompletionAsync<ListeningQuestionGenerationResult>(
                "Prompt/Exam/ListeningQuestionSystem.txt", scenario,
                model: NewModels.GPT_4_1_NANO, feature: Feature);

            if (dto == null)
            {
                logger.LogError("Failed to generate listening question text: {Error}", error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the question text.");
            }
            if (dto.Options.Count != 4 || dto.Options.Count(o => o.IsCorrect) != 1)
            {
                logger.LogError("Listening question generation returned malformed options ({Count} options, {CorrectCount} correct)",
                    dto.Options.Count, dto.Options.Count(o => o.IsCorrect));
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Generated question had an invalid option shape (expected 4 options, 1 correct).");
            }

            var imageResponse = await openAIService.Image.CreateImage(new CreateImageRequest
            {
                Prompt = dto.ImagePrompt,
                Model = ImageModel.GptImage1Mini,
                Quality = ImageQuality.Low,
                Size = ImageSize.Size1792x1024,
                N = 1,
            });

            await usageLogger.LogImageGenerationAsync(Feature, ImageModelId, 1, imageResponse.Successful, userId);

            var b64 = imageResponse.Data?.FirstOrDefault()?.B64Json;
            if (!imageResponse.Successful || string.IsNullOrEmpty(b64))
            {
                logger.LogError("Failed to generate listening question image: {Error}", imageResponse.Error?.Message);
                return ServiceResult.Fail(ErrorCodes.ExternalApiError,
                    "Query failed, OpenAI could not generate the question image.");
            }

            var rawImageBytes = Convert.FromBase64String(b64);
            var processedImageBytes = imageProcessor.ResizeAndCompress(rawImageBytes);

            var listeningTestId = Guid.NewGuid().ToString();
            var imageUrl = await imageStorageService.UploadAsync($"{listeningTestId}.jpg", processedImageBytes);

            var listeningTest = new SQL.ListeningTest
            {
                Id = listeningTestId,
                ImageUrl = imageUrl,
                ImagePrompt = dto.ImagePrompt,
                CreatedAt = DateTime.UtcNow,
                Options = dto.Options.Select(o => new SQL.ListeningTestOption
                {
                    Id = Guid.NewGuid().ToString(),
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    Explanation = o.Explanation,
                    ListeningTestId = listeningTestId,
                }).ToList(),
            };

            dbContext.ListeningTests.Add(listeningTest);
            await dbContext.SaveChangesAsync();

            logger.LogInformation("Generated listening question {Id} for scenario '{Scenario}'", listeningTestId, scenario);

            return ServiceResult.Ok(new ListeningTestResponse
            {
                Id = listeningTest.Id,
                ImageUrl = listeningTest.ImageUrl,
                Options = listeningTest.Options.Select(o => new ListeningTestOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    Explanation = o.Explanation,
                }).ToList(),
            });
        }
    }
}
