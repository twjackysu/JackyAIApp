using System.Text.Json;
using Azure.Storage.Blobs;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.Listening;
using Microsoft.EntityFrameworkCore;

// Bulk-seeds the Listening exam question bank from a manually-produced Gemini batch:
// a questions.json file (array of {id, imagePrompt, options:[{text,isCorrect,explanation}]})
// plus a sibling images/ folder containing {id}.jpg for whichever ids already have art.
// Safe to re-run on the same folder as more images trickle in — already-imported ids are skipped.

if (args.Length < 1)
{
    Console.WriteLine("Usage: dotnet run -- <path-to-folder-containing-questions.json-and-images/>");
    return 1;
}

var inputDir = args[0];
var questionsPath = Path.Combine(inputDir, "questions.json");
var imagesDir = Path.Combine(inputDir, "images");

if (!File.Exists(questionsPath))
{
    Console.WriteLine($"Not found: {questionsPath}");
    return 1;
}
if (!Directory.Exists(imagesDir))
{
    Console.WriteLine($"Not found: {imagesDir}");
    return 1;
}

var (sqlConnectionString, blobConnectionString) = LoadConnectionStrings();
if (string.IsNullOrEmpty(sqlConnectionString))
{
    Console.WriteLine("SQLConnection not found in JackyAIApp.Server/appsettings.json.");
    return 1;
}
if (string.IsNullOrEmpty(blobConnectionString))
{
    Console.WriteLine("BlobStorage:ConnectionString not found in JackyAIApp.Server/appsettings.json.");
    return 1;
}

var questionsJson = await File.ReadAllTextAsync(questionsPath);
var questions = JsonSerializer.Deserialize<List<QuestionDto>>(questionsJson, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
});

if (questions == null || questions.Count == 0)
{
    Console.WriteLine("No questions found in questions.json.");
    return 1;
}

var optionsBuilder = new DbContextOptionsBuilder<AzureSQLDBContext>();
optionsBuilder.UseSqlServer(sqlConnectionString);
await using var db = new AzureSQLDBContext(optionsBuilder.Options);

var blobServiceClient = new BlobServiceClient(blobConnectionString);
IListeningImageProcessor imageProcessor = new ListeningImageProcessor();
IListeningImageStorageService imageStorageService = new ListeningImageStorageService(blobServiceClient);

var existingIds = (await db.ListeningTests.Select(t => t.Id).ToListAsync()).ToHashSet();

int imported = 0, skippedExisting = 0, skippedMissingImage = 0, skippedInvalid = 0, failed = 0;

foreach (var q in questions)
{
    if (existingIds.Contains(q.Id))
    {
        skippedExisting++;
        continue;
    }

    var imagePath = Path.Combine(imagesDir, $"{q.Id}.jpg");
    if (!File.Exists(imagePath))
    {
        skippedMissingImage++;
        continue;
    }

    if (q.Options == null || q.Options.Count != 4 || q.Options.Count(o => o.IsCorrect) != 1)
    {
        Console.WriteLine($"[SKIP] {q.Id}: expected exactly 4 options with exactly 1 correct.");
        skippedInvalid++;
        continue;
    }

    try
    {
        var rawImageBytes = await File.ReadAllBytesAsync(imagePath);
        var processedImageBytes = imageProcessor.ResizeAndCompress(rawImageBytes);
        var imageUrl = await imageStorageService.UploadAsync($"{q.Id}.jpg", processedImageBytes);

        var listeningTest = new ListeningTest
        {
            Id = q.Id,
            ImageUrl = imageUrl,
            ImagePrompt = q.ImagePrompt,
            CreatedAt = DateTime.UtcNow,
            Options = q.Options.Select(o => new ListeningTestOption
            {
                Id = Guid.NewGuid().ToString(),
                Text = o.Text,
                IsCorrect = o.IsCorrect,
                Explanation = o.Explanation,
                ListeningTestId = q.Id,
            }).ToList(),
        };

        db.ListeningTests.Add(listeningTest);
        await db.SaveChangesAsync();

        imported++;
        Console.WriteLine($"[OK] {q.Id} ({processedImageBytes.Length / 1024}KB)");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"[FAIL] {q.Id}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"Imported: {imported}, already existed: {skippedExisting}, missing image: {skippedMissingImage}, invalid: {skippedInvalid}, failed: {failed}");
return 0;

static (string? sql, string? blob) LoadConnectionStrings()
{
    // Reuses JackyAIApp.Server's own appsettings.json (local-only, never committed) rather than
    // duplicating secrets into a second config file — this tool is a one-off admin utility run
    // from the same dev machine that already has that file configured.
    var serverAppSettingsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "JackyAIApp.Server", "appsettings.json");
    serverAppSettingsPath = Path.GetFullPath(serverAppSettingsPath);

    if (!File.Exists(serverAppSettingsPath))
    {
        return (null, null);
    }

    using var doc = JsonDocument.Parse(File.ReadAllText(serverAppSettingsPath));
    var root = doc.RootElement;

    string? sql = root.TryGetProperty("ConnectionStrings", out var cs) && cs.TryGetProperty("SQLConnection", out var sqlEl)
        ? sqlEl.GetString()
        : null;

    string? blob = root.TryGetProperty("BlobStorage", out var bs) && bs.TryGetProperty("ConnectionString", out var blobEl)
        ? blobEl.GetString()
        : null;

    return (sql, blob);
}

internal class QuestionDto
{
    public string Id { get; set; } = "";
    public string? ImagePrompt { get; set; }
    public List<OptionDto> Options { get; set; } = new();
}

internal class OptionDto
{
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public string Explanation { get; set; } = "";
}
