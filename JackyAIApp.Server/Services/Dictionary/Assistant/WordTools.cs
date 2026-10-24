using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Repository;

namespace JackyAIApp.Server.Services.Dictionary.Assistant
{
    /// <summary>The word a tool call names, if it is a plain English word (or a short phrase).</summary>
    internal static partial class WordInput
    {
        [GeneratedRegex(@"^[A-Za-z][A-Za-z' -]{0,49}$")]
        private static partial Regex Plain();

        internal const string NotAWordMessage = "word 必須是一個英文單字（或很短的片語），只能有英文字母、空格、連字號或撇號。";

        public static string? Of(JsonElement input)
        {
            var word = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("word", out var w) && w.ValueKind == JsonValueKind.String
                ? w.GetString()?.Trim()
                : null;
            return word != null && Plain().IsMatch(word) ? word.ToLowerInvariant() : null;
        }

        public static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { word = new { type = "string", description = "英文單字，例如 serendipity" } },
            required = new[] { "word" },
        });

        public static string DictionaryPath(string word) => $"/english/dictionary/{Uri.EscapeDataString(word)}";
    }

    /// <summary>Looks a word up in the site's dictionary (as its page does: one not stored yet is generated once and kept).</summary>
    public sealed class LookUpWordTool(IDictionaryService dictionary) : ISiteTool
    {
        public const string Name = "look_up_word";

        /// <summary>The most meanings, and definitions of each, the model is sent.</summary>
        private const int MaxMeanings = 4;

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "查字典",
            "在本站的英文字典查一個單字：KK 音標、詞性、中英文釋義、例句、同反義字。回答單字的意思前先查，不要憑記憶。",
            WordInput.Schema,
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            if (WordInput.Of(input) is not { } word) return AiToolResult.Error(WordInput.NotAWordMessage);
            var found = await dictionary.GetWordAsync(word, ct);
            if (!found.IsSuccess)
                return AiToolResult.Error(found.Error == ErrorCodes.NotFound || found.Error == ErrorCodes.BadRequest
                    ? $"字典裡找不到「{word}」，可能拼錯了。"
                    : "字典暫時查不到，請告訴使用者稍後再試。");

            var entry = found.Data;
            var text = new StringBuilder($"{entry.Word} {entry.KKPhonics}\n");
            foreach (var meaning in entry.Meanings.Take(MaxMeanings))
            {
                text.Append($"({meaning.PartOfSpeech}) ");
                text.AppendJoin("；", meaning.Definitions.Take(2).Select(d => $"{d.Chinese}（{d.English}）"));
                if (meaning.ExampleSentences.FirstOrDefault() is { } example)
                    text.Append($"\n  例：{example.English}｜{example.Chinese}");
                if (meaning.Synonyms.Count > 0) text.Append($"\n  同義：{string.Join(", ", meaning.Synonyms.Take(5))}");
                text.Append('\n');
            }
            return new AiToolResult(text.ToString().TrimEnd(), Card: new
            {
                kind = "link",
                title = $"{entry.Word} {entry.KKPhonics}",
                href = WordInput.DictionaryPath(word),
                description = entry.Meanings.FirstOrDefault()?.Definitions.FirstOrDefault()?.Chinese,
                actionLabel = "看完整解釋",
            });
        }
    }

    /// <summary>Adds a word to the reader's word repository — a write, so the reader is asked first.</summary>
    public sealed class AddWordToRepositoryTool(IDictionaryService dictionary, IRepositoryService repository) : ISiteTool, IDescribesCall
    {
        public const string Name = "add_word_to_repository";

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "加入單字庫",
            "把一個英文單字加入使用者的單字庫（之後會出現在複習與測驗裡）。只在使用者要求時使用；執行前會請使用者確認。",
            WordInput.Schema,
            ToolEffect.Write);

        public AiToolDescriptor Descriptor => Definition;

        public string? Describe(JsonElement input, AiToolContext context) =>
            WordInput.Of(input) is { } word ? $"把「{word}」加入你的單字庫" : null;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            if (WordInput.Of(input) is not { } word) return AiToolResult.Error(WordInput.NotAWordMessage);
            var found = await dictionary.GetWordAsync(word, ct);
            if (!found.IsSuccess) return AiToolResult.Error($"字典裡找不到「{word}」，所以沒有加入。");

            var added = await repository.AddWordAsync(found.Data.Id);
            var card = new { kind = "link", title = "我的單字庫", href = "/english/repository", description = $"「{found.Data.Word}」在你的單字庫裡", actionLabel = "前往單字庫" };
            if (added.IsSuccess) return new AiToolResult($"已把「{found.Data.Word}」加入使用者的單字庫。", Card: card);
            // The repository's one refusal for a word that exists: it is there already.
            return added.Error == ErrorCodes.BadRequest
                ? new AiToolResult($"「{found.Data.Word}」本來就在使用者的單字庫裡，沒有重複加入。", Card: card)
                : AiToolResult.Error("加入單字庫失敗，請告訴使用者稍後再試。");
        }
    }
}
